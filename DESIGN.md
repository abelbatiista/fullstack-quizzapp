# QuizApp: Design

A time-boxed MVP: users take seeded quizzes, submit their answers, and get a score. An email notification is sent asynchronously through an outbox.

## Stack
- .NET 10 minimal APIs, EF Core + SQLite, xUnit.
- Schema via `EnsureCreated()` + a startup seed. No migrations: any schema change means deleting the `.db` file.
- Solution: `src/QuizApp.Api`, `tests/QuizApp.Tests`.

## Entities
| Entity | Key fields |
|---|---|
| User | Id, Name, Email (unique index) |
| Quiz | Id, Title, Description |
| Question | Id, QuizId, Text, Order, **Explanation (required)** |
| Option | Id, QuestionId, Text, IsCorrect (exactly one per question) |
| Attempt | Id, UserId, QuizId, Status (InProgress/Completed), StartedAt, CompletedAt, CorrectCount, TotalQuestions, Score (`double`, 0–100) |
| AttemptAnswer | Id, AttemptId, QuestionId, OptionId (nullable = unanswered), IsCorrect |
| Notification | Id, AttemptId (unique index), Status (Pending/Sending/Sent/Failed), RetryCount, LastError, NextAttemptAt, CreatedAt, SentAt |

- `Score` is a `double` because EF Core SQLite can't aggregate `decimal` (`Avg`/`Max`/`Sum`). It's rounded to 2 decimals in DTOs.
- Enums are stored as strings, so the database stays readable.
- All `DateTime` values are UTC. A value converter sets `Kind = Utc` on read, because SQLite drops the Kind.

## Endpoints
| Method | Route | Notes |
|---|---|---|
| POST | `/users` | 400 on invalid name/email, 409 on duplicate email |
| GET | `/users/{id}` | 404 |
| GET | `/quizzes` | summaries |
| GET | `/quizzes/{id}` | questions + options, **no IsCorrect, no Explanation** |
| POST | `/quizzes` | 201; 400 unless title, ≥1 question, each with text + explanation, ≥2 options, exactly one correct |
| POST | `/attempts` | body `{ userId, quizId }` → 201, 404 if user/quiz is missing |
| POST | `/attempts/{id}/submit` | body `{ userId, answers: [{ questionId, optionId }] }` → 200 + result |
| GET | `/attempts/{id}` | 404 if missing; status only while in progress, full result once completed |
| GET | `/users/{id}/attempts` | history, newest first |
| GET | `/users/{id}/stats` | attempts, completed, average/best score, per-quiz breakdown |

**DTO rule:** quiz and in-progress attempt DTOs never expose `IsCorrect` or `Explanation`. The **completed result** is the only place that shows the selected option, the correct option, and the explanation for each question, plus the notification status.

## Submit and scoring
- Check order: attempt not found → 404, already completed → 409, then validation → 400. Validation covers a null body, null `userId`/`answers`/item/`questionId`/`optionId`, a `userId` that isn't the attempt's owner, a question that isn't in the quiz, an option that doesn't belong to its question, and a duplicate `questionId`.
- Single choice. A question with no answer counts as incorrect. A partial submission (or an empty list) is allowed. To leave a question unanswered, omit it; `optionId: null` is a 400.
- Emails are trimmed and lowercased before the uniqueness check.
- `Scoring` (Grade, CalculateScore, Summarize), `PerformanceMessages.For` (bands 100 / ≥80 / ≥60 / ≥40 / below) and `SubmitValidator.Validate` are **pure static functions** with no EF.
- **Concurrent submit → 409, never 500:** both racers can pass the status check. The loser's `SaveChanges` then fails, either on the unique index (`AttemptAnswers(AttemptId, QuestionId)`, `Notification.AttemptId`) → `DbUpdateException` with SQLite code 2067, or on the `Status` concurrency token → `DbUpdateConcurrencyException`. Both map to 409. The smoke test hit the unique-index path in 4 of 5 rounds. EF still writes its own `fail:` log entry for the failed save.
- The answers, the completed attempt, and a `Pending` Notification are saved in **one `SaveChanges`** (the outbox).
- A retake is a new attempt.

## Notification worker (outbox)
- `NotificationDispatcherService` (`BackgroundService`) uses `IServiceScopeFactory`. One scope fetches the due ids, then **each notification gets its own scope and its own try/catch**, so one bad row never poisons the batch or the change tracker.
- **Due:** `(Status = Pending OR Sending) AND NextAttemptAt <= now`, oldest first, `BatchSize` per poll. `PollInterval` and `TimeProvider` come from config.
- **Claim with a lease:** before sending, the row is set to `Sending` with `NextAttemptAt = now + LeaseDuration`. If the process dies mid-send, the lease expires and the row is due again, so nothing is stuck in `Sending`.
- **Success:** `Sent` + `SentAt`. This save uses `CancellationToken.None`, because the email has already gone out.
- **Failure** (`NotificationRetryPolicy.MarkFailed`, pure): stores `LastError` (type + message, truncated to 2000 chars). If `RetryCount < MaxRetries`, it does `RetryCount++` and sets `NextAttemptAt = now + BaseDelay * 2^(RetryCount-1)`, capped at `MaxDelay`. Otherwise the row becomes `Failed`. So `MaxRetries = 2` means 3 sends, then `Failed` with `RetryCount = 2`.
- **Cancelled mid-send:** the row goes back to `Pending` (due now, not counted as a failure). It's saved with `CancellationToken.None` and the cancellation is rethrown.
- Poll-level errors (e.g. a locked DB) are logged, and the loop continues on the next interval. `Notifications:Enabled=false` turns the worker off (for tests). `DispatchDueAsync` is public, so tests can drive one batch directly.
- `IEmailSender` → `MockEmailSender` logs the email after a `SimulatedLatency` delay and throws `EmailDeliveryException` when `Random.Shared.NextDouble() < FailureRate`. This uses `Random.Shared` as requested, not injected randomness. Tests stay deterministic with `FailureRate` 0 (never fails) or 1 (always fails), or with a fake `IEmailSender`.
- The email (`NotificationEmailBuilder.Build`, pure) includes the user's name + email, quiz title, correct/total, percentage, performance message, and completedAt (UTC).
- Options are bound via `IOptions` (`Notifications`, `Email` sections) with data-annotation validation and `ValidateOnStart`.
- The `failing-email` launch profile sets `FailureRate=1`, `MaxRetries=2`, `BaseDelay=2s`, `PollInterval=1s`.
- Delivery is **at-least-once**: a crash between the send and the `Sent` save, or an expired lease on a slow send, can send the email twice. That's acceptable for the mock.

## Dev tooling
- `./run.sh [profile] [--keep-db]`: runs from `src/QuizApp.Api` (the DB path is relative). It deletes `quizapp.db*` unless `--keep-db` is passed, and validates the profile name.
- `src/QuizApp.Api/QuizApp.http`: a Rider HTTP client walkthrough with `client.global.set` chaining and `client.test` assertions. The correct options are picked by their known seed positions, because the API never exposes them.

## Assumptions
No auth: `userId` comes in the request and anyone can read any user's data. Quizzes are seeded and can be created via POST, but there's no update or delete (attempts reference them). Submitting twice → 409. Explanations appear only after completion. SQLite runs as a single process, which is fine for one worker.

## Implementation plan
Each phase ends with: `dotnet build` passes → stop any running server → commit.

1. **Scaffold + entities + seed + quiz endpoints.** Solution, API, and an empty test project. Entities + DbContext, UTC DateTime converter, string enums, unique indexes. Seed with the correct option in varied positions. EF command logs at Warning. `AsSplitQuery` for quiz details. `launchBrowser: false`. `.gitignore` (bin/obj/*.db; **keeps `.chat-history/`**). `GET /quizzes`, `GET /quizzes/{id}`, `POST /quizzes`.
2. **Users, attempts, submit, scoring, stats.** Pure scoring and message functions. All validations (400/404/409, null bodies). Unique index + concurrency token → 409 on concurrent submit. Pending Notification in the same `SaveChanges`. `GET /attempts/{id}`, history, stats endpoints.
3. **Notification worker.** Scope factory, per-row try/catch, `Sending` claim with a lease, exponential backoff, `Failed` after max retries, back to `Pending` on cancel, email with name + email, failing-email profile, `run.sh`, Rider `.http` file. Build only, no run.
4. **Tests + review with subagents.** `test-writer` for scoring, messages, validations, and the worker. `reviewer` for answer-key leaks, outbox robustness, and data integrity. Fix the findings.
5. **README, GitHub push, zip.** README (run, endpoints, design choices, assumptions). Push (I'll confirm the repo name and visibility first). Zip without bin/obj/db.
