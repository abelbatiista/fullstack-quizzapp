using Microsoft.EntityFrameworkCore;
using QuizApp.Api.Data;
using QuizApp.Api.Domain;

namespace QuizApp.Api.Features.Quizzes;

public static class QuizEndpoints
{
    public static RouteGroupBuilder MapQuizEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/quizzes").WithTags("Quizzes");

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById).WithName("GetQuizById");
        group.MapPost("/", Create);

        return group;
    }

    private static async Task<IResult> GetAll(QuizDbContext db, CancellationToken ct)
    {
        var quizzes = await db.Quizzes
            .AsNoTracking()
            .OrderBy(q => q.Id)
            .Select(q => new QuizSummaryDto(q.Id, q.Title, q.Description, q.Questions.Count))
            .ToListAsync(ct);

        return Results.Ok(quizzes);
    }

    private static async Task<IResult> GetById(int id, QuizDbContext db, CancellationToken ct)
    {
        var quiz = await LoadDetail(db, id, ct);
        return quiz is null ? Results.NotFound() : Results.Ok(quiz);
    }

    private static async Task<IResult> Create(CreateQuizRequest? request, QuizDbContext db, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var quiz = new Quiz
        {
            Title = request!.Title!.Trim(),
            Description = request.Description?.Trim() ?? "",
            CreatedAt = DateTime.UtcNow,
            Questions = request.Questions!.Select((q, qi) => new Question
            {
                Order = qi + 1,
                Text = q.Text!.Trim(),
                Explanation = q.Explanation!.Trim(),
                Options = q.Options!.Select((o, oi) => new Option
                {
                    Order = oi + 1,
                    Text = o.Text!.Trim(),
                    IsCorrect = o.IsCorrect,
                }).ToList(),
            }).ToList(),
        };

        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync(ct);

        var dto = await LoadDetail(db, quiz.Id, ct);
        return Results.CreatedAtRoute("GetQuizById", new { id = quiz.Id }, dto);
    }

    private static Task<QuizDetailDto?> LoadDetail(QuizDbContext db, int id, CancellationToken ct) =>
        db.Quizzes
            .AsNoTracking()
            .AsSplitQuery()
            .Where(q => q.Id == id)
            .Select(q => new QuizDetailDto(
                q.Id,
                q.Title,
                q.Description,
                q.Questions
                    .OrderBy(x => x.Order)
                    .Select(x => new QuestionDto(
                        x.Id,
                        x.Order,
                        x.Text,
                        x.Options.OrderBy(o => o.Order).Select(o => new OptionDto(o.Id, o.Text)).ToList()))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

    internal static Dictionary<string, string[]> Validate(CreateQuizRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string key, string message) => errors[key] = [.. errors.GetValueOrDefault(key, []), message];

        if (request is null)
        {
            Add("body", "Request body is required.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.Title)) Add("title", "Title is required.");
        else if (request.Title.Trim().Length > 200) Add("title", "Title must be at most 200 characters.");

        if (request.Description?.Length > 1000) Add("description", "Description must be at most 1000 characters.");

        if (request.Questions is null || request.Questions.Count == 0)
        {
            Add("questions", "At least one question is required.");
            return errors;
        }

        for (var i = 0; i < request.Questions.Count; i++)
        {
            var q = request.Questions[i];
            var key = $"questions[{i}]";
            if (q is null) { Add(key, "Question is required."); continue; }

            if (string.IsNullOrWhiteSpace(q.Text)) Add($"{key}.text", "Question text is required.");
            if (string.IsNullOrWhiteSpace(q.Explanation)) Add($"{key}.explanation", "Explanation is required.");

            if (q.Options is null || q.Options.Count < 2)
            {
                Add($"{key}.options", "At least two options are required.");
                continue;
            }

            if (q.Options.Any(o => o is null || string.IsNullOrWhiteSpace(o.Text)))
                Add($"{key}.options", "Every option needs text.");
            if (q.Options.Count(o => o is { IsCorrect: true }) != 1)
                Add($"{key}.options", "Exactly one option must be correct.");
        }

        return errors;
    }
}
