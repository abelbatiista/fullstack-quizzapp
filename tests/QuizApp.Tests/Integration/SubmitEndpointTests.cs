using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuizApp.Api.Data;
using QuizApp.Api.Domain;
using QuizApp.Api.Features.Attempts;
using QuizApp.Api.Features.Quizzes;
using QuizApp.Api.Features.Users;

namespace QuizApp.Tests.Integration;

public sealed class QuizAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"quizapp-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath};Pooling=False");
        builder.UseSetting("Notifications:Enabled", "false");
        // The outbox must not depend on email delivery at submit time.
        builder.UseSetting("Email:FailureRate", "1");
        builder.UseSetting("Email:SimulatedLatency", "00:00:00");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }
}

public sealed class SubmitEndpointTests(QuizAppFactory factory) : IClassFixture<QuizAppFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private async Task<(int UserId, int AttemptId, QuizDetailDto Quiz)> StartAttemptAsync(HttpClient client)
    {
        var userRes = await client.PostAsJsonAsync("/users",
            new { name = "Test User", email = $"u{Guid.NewGuid():N}@example.com" });
        Assert.Equal(HttpStatusCode.Created, userRes.StatusCode);
        var user = (await userRes.Content.ReadFromJsonAsync<UserDto>(Json))!;

        var quiz = (await client.GetFromJsonAsync<QuizDetailDto>("/quizzes/1", Json))!;

        var attemptRes = await client.PostAsJsonAsync("/attempts", new { userId = user.Id, quizId = quiz.Id });
        Assert.Equal(HttpStatusCode.Created, attemptRes.StatusCode);
        var attempt = (await attemptRes.Content.ReadFromJsonAsync<AttemptStatusDto>(Json))!;

        return (user.Id, attempt.Id, quiz);
    }

    [Fact]
    public async Task Submit_PartialAnswers_PersistsAttemptAnswersAndPendingNotificationTogether()
    {
        var client = factory.CreateClient();
        var (userId, attemptId, quiz) = await StartAttemptAsync(client);
        var answered = quiz.Questions.Take(2)
            .Select(q => new { questionId = q.Id, optionId = q.Options[0].Id })
            .ToList();

        var res = await client.PostAsJsonAsync($"/attempts/{attemptId}/submit", new { userId, answers = answered });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var result = (await res.Content.ReadFromJsonAsync<AttemptResultDto>(Json))!;
        Assert.Equal(AttemptStatus.Completed, result.Status);
        Assert.Equal(6, result.TotalQuestions);
        Assert.Equal(Scoring.CalculateScore(result.CorrectCount, 6), result.Score);
        Assert.Equal(PerformanceMessages.For(result.Score), result.Message);
        Assert.Equal(NotificationStatus.Pending, result.NotificationStatus);
        Assert.Equal(6, result.Questions.Count);
        Assert.All(result.Questions.Skip(2), q =>
        {
            Assert.Null(q.SelectedOptionId);
            Assert.False(q.IsCorrect);
        });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
        var attempt = await db.Attempts.AsNoTracking()
            .Include(a => a.Answers).Include(a => a.Notification)
            .SingleAsync(a => a.Id == attemptId);

        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.NotNull(attempt.CompletedAt);
        Assert.Equal(6, attempt.Answers.Count);
        Assert.Equal(4, attempt.Answers.Count(a => a.OptionId is null));
        Assert.NotNull(attempt.Notification);
        Assert.Equal(NotificationStatus.Pending, attempt.Notification.Status);
        Assert.Equal(0, attempt.Notification.RetryCount);
        Assert.Equal(attempt.CompletedAt, attempt.Notification.NextAttemptAt);
    }

    [Fact]
    public async Task Submit_Twice_SecondReturns409AndDoesNotDuplicate()
    {
        var client = factory.CreateClient();
        var (userId, attemptId, _) = await StartAttemptAsync(client);

        var first = await client.PostAsJsonAsync($"/attempts/{attemptId}/submit", new { userId, answers = Array.Empty<object>() });
        var second = await client.PostAsJsonAsync($"/attempts/{attemptId}/submit", new { userId, answers = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.AttemptId == attemptId));
        Assert.Equal(6, await db.AttemptAnswers.CountAsync(a => a.AttemptId == attemptId));
    }

    [Fact]
    public async Task Submit_NullOptionId_Returns400AndAttemptStaysInProgress()
    {
        var client = factory.CreateClient();
        var (userId, attemptId, quiz) = await StartAttemptAsync(client);

        var res = await client.PostAsJsonAsync($"/attempts/{attemptId}/submit",
            new { userId, answers = new[] { new { questionId = quiz.Questions[0].Id, optionId = (int?)null } } });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
        var attempt = await db.Attempts.AsNoTracking().Include(a => a.Notification).SingleAsync(a => a.Id == attemptId);
        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
        Assert.Null(attempt.Notification);
    }

    [Fact]
    public async Task GetAttempt_Owner_SeesCompletedResultWithAnswerKey()
    {
        var client = factory.CreateClient();
        var (userId, attemptId, _) = await StartAttemptAsync(client);
        await client.PostAsJsonAsync($"/attempts/{attemptId}/submit", new { userId, answers = Array.Empty<object>() });

        var res = await client.GetAsync($"/attempts/{attemptId}?userId={userId}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var result = (await res.Content.ReadFromJsonAsync<AttemptResultDto>(Json))!;
        Assert.Equal(6, result.Questions.Count);
        Assert.All(result.Questions, q => Assert.False(string.IsNullOrWhiteSpace(q.Explanation)));
    }

    [Fact]
    public async Task GetAttempt_NonOwner_Gets404WithNoAnswerKey()
    {
        var client = factory.CreateClient();
        var (ownerId, attemptId, _) = await StartAttemptAsync(client);
        await client.PostAsJsonAsync($"/attempts/{attemptId}/submit", new { userId = ownerId, answers = Array.Empty<object>() });
        var (otherUserId, _, _) = await StartAttemptAsync(client);

        var res = await client.GetAsync($"/attempts/{attemptId}?userId={otherUserId}");
        var body = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.DoesNotContain("explanation", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctOption", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAttempt_NonOwnerAndMissingAttempt_AreIndistinguishable()
    {
        var client = factory.CreateClient();
        var (_, attemptId, _) = await StartAttemptAsync(client);

        var foreign = await client.GetAsync($"/attempts/{attemptId}?userId=999999");
        var missing = await client.GetAsync("/attempts/999999?userId=999999");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task GetAttempt_WithoutUserId_Returns400()
    {
        var client = factory.CreateClient();
        var (_, attemptId, _) = await StartAttemptAsync(client);

        var res = await client.GetAsync($"/attempts/{attemptId}");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task StartAttempt_LocationHeader_IncludesUserId()
    {
        var client = factory.CreateClient();
        var userRes = await client.PostAsJsonAsync("/users", new { name = "Loc", email = $"l{Guid.NewGuid():N}@example.com" });
        var user = (await userRes.Content.ReadFromJsonAsync<UserDto>(Json))!;

        var res = await client.PostAsJsonAsync("/attempts", new { userId = user.Id, quizId = 1 });
        var follow = await client.GetAsync(res.Headers.Location);

        Assert.Contains($"userId={user.Id}", res.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, follow.StatusCode);
    }

    [Fact]
    public async Task Submit_UnknownAttempt_Returns404()
    {
        var client = factory.CreateClient();

        var res = await client.PostAsJsonAsync("/attempts/999999/submit", new { userId = 1, answers = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
