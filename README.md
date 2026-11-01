# QuizApp

A small quiz API: users take multiple-choice quizzes about AI development, submit their answers, and get a score, a performance message, and a per-question breakdown with explanations. A result email is delivered asynchronously through a **transactional outbox** with retries and exponential backoff (through a mock sender that can be configured to fail).

Built as a time-boxed MVP with **.NET 10 minimal APIs, EF Core + SQLite, and xUnit**. The design decisions are in [DESIGN.md](DESIGN.md).

## Run it

Requires the .NET 10 SDK.

```bash
./run.sh                    # "http" profile on http://localhost:5003, fresh database
./run.sh failing-email      # every email fails: watch retries, backoff, then Failed
./run.sh http --keep-db     # keep the existing quizapp.db and its data
./run.sh --help
```

- `run.sh` runs from `src/QuizApp.Api` because the connection string is relative (`Data Source=quizapp.db`). By default it **deletes `quizapp.db*`**: the schema comes from `EnsureCreated()` (no migrations) and the seed only runs on an empty database, so a fresh DB is the normal way to start.
- Profiles (`src/QuizApp.Api/Properties/launchSettings.json`):

| Profile | What it does |
|---|---|
| `http` | Normal run. `Email:FailureRate` 0.2, `MaxRetries` 5, 5 s base delay, 5 s poll |
| `https` | Same, plus `https://localhost:7079` |
| `failing-email` | `FailureRate` 1, `MaxRetries` 2, `BaseDelay` 2 s, `PollInterval` 1 s: 3 sends (~2 s and ~4 s apart), then `Failed` |

The browser doesn't open (`launchBrowser: false`). `GET /` redirects to `/quizzes`.

## Try it with QuizApp.http

Open `src/QuizApp.Api/QuizApp.http` in Rider (or IntelliJ) with the app running, and run the requests top to bottom. Each one stores ids for the next (`client.global.set`) and asserts its own result (`client.test`):

1. create user → 2. list quizzes → 3. get quiz (checks no answer key leaks) → 4. start attempt → 5. get in-progress (status only) → 6. submit 5 correct with question 6 unanswered (83.33) → 7. get result (breakdown, explanations, notification status) → 7b. another user can't read it (404) → 8. double submit (409) → 9. retake (100) → 10. history → 11. stats.

The file picks correct options by their **known seed positions**, because the API never exposes them, so it expects a freshly seeded DB (plain `./run.sh`). Re-run step 7 after a few seconds to see `notificationStatus` change from `Pending` to `Sent` (or `Failed` under `failing-email`). The worker logs every mock email to the console.

## Test it

```bash
dotnet test
```

117 xUnit tests, all passing:

| Area | Covers |
|---|---|
| `ScoringTests` | all, none, partial (5/6 = 83.33), unanswered = incorrect, empty quiz, rounding and midpoints, invalid arguments |
| `PerformanceMessagesTests` | every band edge (100 / 80 / 60 / 40) just below, at, and above; out-of-range, NaN, ±∞ |
| `SubmitValidatorTests` | every 400 case: nulls, owner mismatch, foreign question/option, duplicates |
| `NotificationRulesTests` | backoff sequence and cap (no overflow), retry → `Failed` transitions, error truncation, email content |
| `NotificationDispatcherServiceTests` | in-memory SQLite + fake clock: success, **always-throwing sender** (retry 1 → 2 → `Failed`, sender called exactly 3 times), not-due skip, one failure doesn't block the batch, cancel mid-send → `Pending`, expired lease reclaimed |
| `SubmitEndpointTests` | `WebApplicationFactory`: atomic outbox write, second submit 409 with no duplicates, 400 rolls back, owner-only result |
| `SeedIntegrityTests` | one correct option per question, positions balanced 3/3/3/3, "pick the longest/shortest option" doesn't beat chance |

## Endpoints

| Method | Route | Notes |
|---|---|---|
| POST | `/users` | `{ name, email }` → 201. Email trimmed + lowercased. 400 invalid, 409 duplicate |
| GET | `/users/{id}` | 404 if missing |
| GET | `/quizzes` | summaries with question counts |
| GET | `/quizzes/{id}` | questions + options, **no `isCorrect`, no explanations** |
| POST | `/quizzes` | 201. 400 unless title, ≥1 question, each with text + explanation, ≥2 options, exactly one correct |
| POST | `/attempts` | `{ userId, quizId }` → 201. 400 nulls, 404 unknown user/quiz |
| POST | `/attempts/{id}/submit` | `{ userId, answers: [{ questionId, optionId }] }` → 200 + result. 404 unknown, 409 already submitted (including concurrent submits), 400 validation |
| GET | `/attempts/{id}?userId=` | owner only. In progress: status only. Completed: score, message, breakdown with correct option + explanation, `notificationStatus`. 400 without `userId`, 404 if missing or not the owner |
| GET | `/users/{id}/attempts` | history, newest first |
| GET | `/users/{id}/stats` | totals, average/best score, last completed, per-quiz average/best/latest |

Errors are RFC 7807 `ProblemDetails`. Validation errors list every problem at once, keyed by field (e.g. `answers[2].optionId`).

## Key decisions

- **Outbox in the same transaction.** Submit saves the completed attempt, every answer (unanswered = `null` option, incorrect), and a `Pending` notification in **one `SaveChanges`**. The request never waits on email, and a result can't exist without its notification.
- **409 on double submit, never 500.** Two racing submits can both pass the status check. The loser's save then fails on a unique index (`AttemptAnswers(AttemptId, QuestionId)`, `Notification.AttemptId`) or on the `Attempt.Status` concurrency token, and both errors map to 409. The smoke test hit the unique-index path in 4 of 5 parallel rounds.
- **Notification worker:**
  - **Scopes:** `BackgroundService` with `IServiceScopeFactory`; each notification gets its own scope and try/catch.
  - **Claiming:** rows are claimed as `Sending` with a **lease**, so a crash mid-send is retried once the lease expires.
  - **Retries:** exponential backoff `BaseDelay × 2^(n-1)` capped at `MaxDelay`, then `Failed` after `MaxRetries`. `RetryCount` and `LastError` are saved.
  - **Cancellation:** cancelled mid-send → back to `Pending`, then the cancellation is rethrown.
  - **Configuration:** `IOptions` with validation at startup.
- **Pure core logic.** `Scoring`, `PerformanceMessages`, `SubmitValidator`, `NotificationRetryPolicy`, and `NotificationEmailBuilder` are static functions with no EF, which made the tests cheap and fast. Time comes from an injected `TimeProvider`.
- **The answer key stays hidden.** Public DTOs never carry `IsCorrect` or explanations. Quiz reads project directly to DTOs, so those columns aren't even loaded. The seed spreads correct answers evenly across positions *and* lengths.
- **SQLite details.** `Score` is a `double` because EF Core SQLite can't aggregate `decimal`. A global value converter marks every `DateTime` as UTC on read (SQLite drops the kind), so JSON dates end in `Z`. Enums are stored as strings. EF command logs are at `Warning`, and multi-collection reads use `AsSplitQuery`.

## Trade-offs and known limitations

- **No auth.** `userId` travels in the request. The result endpoint is owner-only, but without authentication `userId` is a claim, not a proof: someone who knows another user's id and attempt id can read that result. A user can also submit an empty attempt, read the key, and retake for 100. Real auth (and possibly hiding the correct option until a cooldown) would fix both.
- **No migrations.** `EnsureCreated` + seed. Any schema change requires deleting the DB (which `run.sh` does by default).
- **At-least-once email.** A crash between sending and saving `Sent` (or a send slower than the lease) can send a duplicate. This is acceptable for a mock; a real provider would use an idempotency key (the notification id).
- **Single process.** One worker, one SQLite file. Under heavy write contention, `SQLITE_BUSY` can surface as a 500 (no data is half-written). Multiple workers would need a stronger claim (e.g. a concurrency token on `Notification`).
- **Open review findings** (reported by the reviewer subagent, deliberately deferred):
  - if saving `Sent` fails after delivery, the email is recorded as a failure and re-sent;
  - malformed JSON may return 500 instead of 400 in Development (unverified);
  - a notification row that can't be loaded is retried every poll;
  - a failed claim save counts as a retry;
  - `POST /quizzes` doesn't enforce text-length or count caps;
  - the first poll runs during startup;
  - `./run.sh profiles` passes the profile check.
- **Quizzes are create-only.** No update or delete (attempts reference them). Single-choice questions only.

## How AI was used

This was built with **Claude Code** in phases from a design I wrote and reviewed first.

- **Design first.** I gave my decisions; Claude challenged them and wrote [DESIGN.md](DESIGN.md) with a 5-phase plan. For example, it pointed out that a 409 on concurrent submits needs a real mechanism, and that "back to Pending on cancel" needs a claim state. Each phase was triggered with "implement phase N" and ended with a build, a stopped server, and a commit.
- **Subagents for the review phase**, run in parallel with project-defined agents in `.claude/agents/`:
  - **`test-writer`** (could only touch the test project) wrote the test suite and ran `dotnet test`. It was told to report bugs, not fix production code.
  - **`reviewer`** (read-only) audited answer-key leakage, outbox robustness, and data integrity. It returned 13 ranked findings. **I chose which to fix**: the two High ones (the correct option was always the longest in the seed; anyone could read anyone's result). Both now have regression tests. The rest are listed above.
- **Human checkpoints.** Every phase was smoke-tested against the running API (status codes, the parallel-submit race, the leak check), and Claude reported its own assumptions and deviations for me to accept or reject.
- **Chat log.** `CLAUDE.md` instructs the assistant to read and append to [`.chat-history/log.md`](.chat-history/log.md) on every exchange: timestamp, prompt, a summary of what was done, and the files affected. It's the full record of how this repo was built, including what each subagent did.

## Project layout

```
QuizApp.slnx
run.sh
DESIGN.md
CLAUDE.md                  # assistant instructions (chat logging)
.chat-history/log.md       # prompt-by-prompt build log
.claude/agents/            # reviewer + test-writer subagent definitions
src/QuizApp.Api/
  Program.cs
  QuizApp.http
  Domain/                  # entities, Scoring, PerformanceMessages
  Data/                    # QuizDbContext (UTC converter, indexes), DbSeeder
  Features/
    Quizzes/  Users/  Attempts/  Notifications/  Common/
tests/QuizApp.Tests/
  Domain/  Features/  Integration/  Fakes/
```
