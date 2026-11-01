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
| Notification | Id, AttemptId (unique index), Status (Pending/Sent/Failed), RetryCount, LastError, NextAttemptAt, CreatedAt, SentAt |

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
- `BackgroundService` + `IServiceScopeFactory`. It creates a new scope per poll.
- Each poll picks rows where `Status = Pending` and `NextAttemptAt <= now`, oldest first, in small batches.
- Each row has its own try/catch, so one failure never stops the batch.
- On failure: `RetryCount++`, set `LastError`, set `NextAttemptAt = now + base * 2^RetryCount` (capped). After `MaxRetries` the row becomes `Failed`.
- On shutdown or cancel, a row being processed is put back to `Pending` and is not counted as a failure.
- `IEmailSender` → `MockEmailSender` logs the email (user name + email, quiz, score). `FailureRate` comes from config. Randomness is injected, so tests are deterministic.
- A `FailingEmail` launch profile / appsettings sets a high failure rate to demonstrate retries.
- Delivery is **at-least-once**: a crash between the send and the save can send the email twice. That's acceptable for the mock and is documented.

## Assumptions
No auth: `userId` comes in the request and anyone can read any user's data. Quizzes are seeded and can be created via POST, but there's no update or delete (attempts reference them). Submitting twice → 409. Explanations appear only after completion. SQLite runs as a single process, which is fine for one worker.

## Implementation plan
Each phase ends with: `dotnet build` passes → stop any running server → commit.

1. **Scaffold + entities + seed + quiz endpoints.** Solution, API, and an empty test project. Entities + DbContext, UTC DateTime converter, string enums, unique indexes. Seed with the correct option in varied positions. EF command logs at Warning. `AsSplitQuery` for quiz details. `launchBrowser: false`. `.gitignore` (bin/obj/*.db; **keeps `.chat-history/`**). `GET /quizzes`, `GET /quizzes/{id}`, `POST /quizzes`.
2. **Users, attempts, submit, scoring, stats.** Pure scoring and message functions. All validations (400/404/409, null bodies). Unique index + concurrency token → 409 on concurrent submit. Pending Notification in the same `SaveChanges`. `GET /attempts/{id}`, history, stats endpoints.
3. **Notification worker.** Scope factory, per-row try/catch, exponential backoff, `Failed` after max retries, back to `Pending` on cancel, email with name + email, failing-email profile, `run.sh`, Rider `.http` file.
4. **Tests + review with subagents.** `test-writer` for scoring, messages, validations, and the worker. `reviewer` for answer-key leaks, outbox robustness, and data integrity. Fix the findings.
5. **README, GitHub push, zip.** README (run, endpoints, design choices, assumptions). Push (I'll confirm the repo name and visibility first). Zip without bin/obj/db.
