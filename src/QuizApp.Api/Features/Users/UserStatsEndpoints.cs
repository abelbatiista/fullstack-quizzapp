using Microsoft.EntityFrameworkCore;
using QuizApp.Api.Data;
using QuizApp.Api.Domain;
using QuizApp.Api.Features.Attempts;
using QuizApp.Api.Features.Common;

namespace QuizApp.Api.Features.Users;

public static class UserStatsEndpoints
{
    public static void MapUserStatsEndpoints(this RouteGroupBuilder users)
    {
        users.MapGet("/{id:int}/attempts", History);
        users.MapGet("/{id:int}/stats", Stats);
    }

    private static async Task<IResult> History(int id, QuizDbContext db, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id, ct)) return ApiErrors.NotFound($"User {id} not found.");

        var history = await db.Attempts
            .AsNoTracking()
            .Where(a => a.UserId == id)
            .OrderByDescending(a => a.StartedAt).ThenByDescending(a => a.Id)
            .Select(a => new AttemptSummaryDto(
                a.Id,
                a.QuizId,
                a.Quiz.Title,
                a.Status,
                a.StartedAt,
                a.CompletedAt,
                a.Status == AttemptStatus.Completed ? a.CorrectCount : null,
                a.TotalQuestions,
                a.Status == AttemptStatus.Completed ? a.Score : null,
                a.Notification != null ? a.Notification.Status : null))
            .ToListAsync(ct);

        return Results.Ok(history);
    }

    private static async Task<IResult> Stats(int id, QuizDbContext db, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id, ct)) return ApiErrors.NotFound($"User {id} not found.");

        var counts = await db.Attempts
            .Where(a => a.UserId == id)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var completed = db.Attempts.Where(a => a.UserId == id && a.Status == AttemptStatus.Completed);

        // Aggregated in SQL; Score is a double precisely so SQLite can do this.
        // Nullable projections: SQL AVG/MAX over zero rows yields NULL instead of throwing.
        var average = await completed.AverageAsync(a => (double?)a.Score, ct);
        var best = await completed.MaxAsync(a => (double?)a.Score, ct);

        var lastCompletedAt = await completed
            .OrderByDescending(a => a.CompletedAt)
            .Select(a => a.CompletedAt)
            .FirstOrDefaultAsync(ct);

        var perQuiz = await completed
            .GroupBy(a => new { a.QuizId, a.Quiz.Title })
            .Select(g => new
            {
                g.Key.QuizId,
                g.Key.Title,
                Count = g.Count(),
                Avg = g.Average(a => a.Score),
                Best = g.Max(a => a.Score),
                Latest = g.OrderByDescending(a => a.CompletedAt).ThenByDescending(a => a.Id).Select(a => a.Score).First(),
            })
            .OrderBy(x => x.QuizId)
            .ToListAsync(ct);

        int CountOf(AttemptStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;

        var dto = new UserStatsDto(
            id,
            counts.Sum(c => c.Count),
            CountOf(AttemptStatus.Completed),
            CountOf(AttemptStatus.InProgress),
            average is null ? null : Round(average.Value),
            best,
            lastCompletedAt,
            perQuiz.Select(q => new QuizStatsDto(q.QuizId, q.Title, q.Count, Round(q.Avg), q.Best, q.Latest)).ToList());

        return Results.Ok(dto);
    }

    private static double Round(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
