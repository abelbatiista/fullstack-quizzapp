using Microsoft.EntityFrameworkCore;
using QuizApp.Api.Data;
using QuizApp.Api.Domain;
using QuizApp.Api.Features.Common;

namespace QuizApp.Api.Features.Attempts;

public static class AttemptEndpoints
{
    public static RouteGroupBuilder MapAttemptEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/attempts").WithTags("Attempts");

        group.MapPost("/", Start);
        group.MapGet("/{id:int}", GetById).WithName("GetAttemptById");
        group.MapPost("/{id:int}/submit", Submit);

        return group;
    }

    private static async Task<IResult> Start(StartAttemptRequest? request, QuizDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (request is null) return ApiErrors.BadRequest("body", "Request body is required.");

        var errors = new Dictionary<string, string[]>();
        if (request.UserId is null) errors["userId"] = ["userId is required."];
        if (request.QuizId is null) errors["quizId"] = ["quizId is required."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        if (!await db.Users.AnyAsync(u => u.Id == request.UserId, ct))
            return ApiErrors.NotFound($"User {request.UserId} not found.");

        var quiz = await db.Quizzes
            .Where(q => q.Id == request.QuizId)
            .Select(q => new { q.Id, q.Title, QuestionCount = q.Questions.Count })
            .FirstOrDefaultAsync(ct);
        if (quiz is null) return ApiErrors.NotFound($"Quiz {request.QuizId} not found.");

        var attempt = new Attempt
        {
            UserId = request.UserId!.Value,
            QuizId = quiz.Id,
            Status = AttemptStatus.InProgress,
            StartedAt = clock.GetUtcNow().UtcDateTime,
            TotalQuestions = quiz.QuestionCount,
        };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(ct);

        var dto = new AttemptStatusDto(attempt.Id, attempt.UserId, attempt.QuizId, quiz.Title,
            attempt.Status, attempt.StartedAt, attempt.TotalQuestions);
        return Results.CreatedAtRoute("GetAttemptById", new { id = attempt.Id }, dto);
    }

    private static async Task<IResult> GetById(int id, QuizDbContext db, CancellationToken ct)
    {
        var dto = await LoadAttemptView(db, id, ct);
        return dto is null ? ApiErrors.NotFound($"Attempt {id} not found.") : Results.Ok(dto);
    }

    private static async Task<IResult> Submit(int id, SubmitAttemptRequest? request, QuizDbContext db, TimeProvider clock, CancellationToken ct)
    {
        // Tracked: Status is a concurrency token, so the UPDATE carries "WHERE Status = 'InProgress'".
        var attempt = await db.Attempts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attempt is null) return ApiErrors.NotFound($"Attempt {id} not found.");
        if (attempt.Status == AttemptStatus.Completed) return AlreadySubmitted(id);

        var questions = await db.Questions
            .AsNoTracking()
            .Where(q => q.QuizId == attempt.QuizId)
            .Select(q => new
            {
                q.Id,
                OptionIds = q.Options.Select(o => o.Id).ToList(),
                CorrectOptionId = q.Options.Where(o => o.IsCorrect).Select(o => o.Id).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var optionsByQuestion = questions.ToDictionary(q => q.Id, q => (IReadOnlySet<int>)q.OptionIds.ToHashSet());
        var errors = SubmitValidator.Validate(request, attempt.UserId, optionsByQuestion);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var key = questions.Select(q => new QuestionKey(q.Id, q.CorrectOptionId)).ToList();
        var answers = request!.Answers!.ToDictionary(a => a!.QuestionId!.Value, a => a!.OptionId!.Value);
        var graded = Scoring.Grade(key, answers);
        var summary = Scoring.Summarize(graded);
        var now = clock.GetUtcNow().UtcDateTime;

        attempt.Status = AttemptStatus.Completed;
        attempt.CompletedAt = now;
        attempt.CorrectCount = summary.CorrectCount;
        attempt.TotalQuestions = summary.TotalQuestions;
        attempt.Score = summary.Score;
        attempt.Answers.AddRange(graded.Select(g => new AttemptAnswer
        {
            QuestionId = g.QuestionId,
            OptionId = g.SelectedOptionId,
            IsCorrect = g.IsCorrect,
        }));
        // Outbox: the notification is committed atomically with the result.
        attempt.Notification = new Notification
        {
            Status = NotificationStatus.Pending,
            CreatedAt = now,
            NextAttemptAt = now,
        };

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another submit completed the attempt between our read and our write.
            return AlreadySubmitted(id);
        }
        catch (DbUpdateException ex) when (ApiErrors.IsUniqueViolation(ex))
        {
            // Unique index on Notification.AttemptId / (AttemptId, QuestionId) caught the race.
            return AlreadySubmitted(id);
        }

        db.ChangeTracker.Clear();
        var dto = await LoadAttemptView(db, id, ct);
        return Results.Ok(dto);
    }

    private static IResult AlreadySubmitted(int id) => ApiErrors.Conflict($"Attempt {id} has already been submitted.");

    /// <summary>Status-only view while in progress; full result once completed.</summary>
    private static async Task<object?> LoadAttemptView(QuizDbContext db, int id, CancellationToken ct)
    {
        var header = await db.Attempts
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new { a.Status, a.UserId, a.QuizId, QuizTitle = a.Quiz.Title, a.StartedAt, a.TotalQuestions })
            .FirstOrDefaultAsync(ct);
        if (header is null) return null;

        if (header.Status != AttemptStatus.Completed)
            return new AttemptStatusDto(id, header.UserId, header.QuizId, header.QuizTitle,
                header.Status, header.StartedAt, header.TotalQuestions);

        var attempt = await db.Attempts
            .AsNoTracking()
            .AsSplitQuery()
            .Include(a => a.Quiz).ThenInclude(q => q.Questions).ThenInclude(q => q.Options)
            .Include(a => a.Answers)
            .Include(a => a.Notification)
            .FirstAsync(a => a.Id == id, ct);

        return ToResultDto(attempt);
    }

    internal static AttemptResultDto ToResultDto(Attempt attempt)
    {
        var answers = attempt.Answers.ToDictionary(a => a.QuestionId);

        var questions = attempt.Quiz.Questions
            .OrderBy(q => q.Order)
            .Select(q =>
            {
                var correct = q.Options.Single(o => o.IsCorrect);
                answers.TryGetValue(q.Id, out var answer);
                var selected = answer?.OptionId is { } sid ? q.Options.FirstOrDefault(o => o.Id == sid) : null;
                return new QuestionResultDto(
                    q.Id, q.Order, q.Text,
                    selected?.Id, selected?.Text,
                    correct.Id, correct.Text,
                    answer?.IsCorrect ?? false,
                    q.Explanation);
            })
            .ToList();

        return new AttemptResultDto(
            attempt.Id, attempt.UserId, attempt.QuizId, attempt.Quiz.Title,
            attempt.Status, attempt.StartedAt, attempt.CompletedAt,
            attempt.CorrectCount, attempt.TotalQuestions, attempt.Score,
            PerformanceMessages.For(attempt.Score),
            attempt.Notification?.Status,
            questions);
    }
}
