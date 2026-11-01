using QuizApp.Api.Domain;
using QuizApp.Api.Features.Notifications;

namespace QuizApp.Tests.Features;

public class NotificationRetryPolicyTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static NotificationOptions Options(int maxRetries = 2) => new()
    {
        MaxRetries = maxRetries,
        BaseDelay = TimeSpan.FromSeconds(2),
        MaxDelay = TimeSpan.FromSeconds(30),
    };

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    [InlineData(5, 30)]   // 32 capped to 30
    [InlineData(6, 30)]
    public void ComputeBackoff_RetryNumber_DoublesUntilCapped(int retry, int expectedSeconds)
    {
        var delay = NotificationRetryPolicy.ComputeBackoff(retry, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Fact]
    public void ComputeBackoff_ExactlyAtCap_ReturnsCap()
    {
        var delay = NotificationRetryPolicy.ComputeBackoff(4, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(16));

        Assert.Equal(TimeSpan.FromSeconds(16), delay);
    }

    [Theory]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void ComputeBackoff_HugeRetryNumber_CapsWithoutOverflow(int retry)
    {
        var max = TimeSpan.FromMinutes(5);

        Assert.Equal(max, NotificationRetryPolicy.ComputeBackoff(retry, TimeSpan.FromSeconds(5), max));
    }

    [Fact]
    public void ComputeBackoff_ZeroBaseDelay_ReturnsZero()
    {
        Assert.Equal(TimeSpan.Zero, NotificationRetryPolicy.ComputeBackoff(3, TimeSpan.Zero, TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ComputeBackoff_RetryNumberBelowOne_Throws(int retry)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NotificationRetryPolicy.ComputeBackoff(retry, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void MarkFailed_RepeatedFailures_RetriesThenFails()
    {
        var n = new Notification { Status = NotificationStatus.Sending, NextAttemptAt = Now };
        var o = Options(maxRetries: 2);

        NotificationRetryPolicy.MarkFailed(n, "e1", Now, o);
        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.Equal(1, n.RetryCount);
        Assert.Equal(Now.AddSeconds(2), n.NextAttemptAt);
        Assert.Equal("e1", n.LastError);

        var t2 = Now.AddSeconds(2);
        n.Status = NotificationStatus.Sending;
        NotificationRetryPolicy.MarkFailed(n, "e2", t2, o);
        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.Equal(2, n.RetryCount);
        Assert.Equal(t2.AddSeconds(4), n.NextAttemptAt);
        Assert.Equal("e2", n.LastError);

        var t3 = t2.AddSeconds(4);
        n.Status = NotificationStatus.Sending;
        NotificationRetryPolicy.MarkFailed(n, "e3", t3, o);
        Assert.Equal(NotificationStatus.Failed, n.Status);
        Assert.Equal(2, n.RetryCount);
        Assert.Equal("e3", n.LastError);
    }

    [Fact]
    public void MarkFailed_MaxRetriesZero_FailsImmediately()
    {
        var n = new Notification { Status = NotificationStatus.Sending };

        NotificationRetryPolicy.MarkFailed(n, "boom", Now, Options(maxRetries: 0));

        Assert.Equal(NotificationStatus.Failed, n.Status);
        Assert.Equal(0, n.RetryCount);
        Assert.Equal("boom", n.LastError);
    }

    [Theory]
    [InlineData(1999, 1999)]
    [InlineData(2000, 2000)]
    [InlineData(2001, 2000)]
    [InlineData(10_000, 2000)]
    public void MarkFailed_LongError_TruncatedToMaxLength(int length, int expectedLength)
    {
        var n = new Notification();
        var error = new string('x', length);

        NotificationRetryPolicy.MarkFailed(n, error, Now, Options());

        Assert.Equal(expectedLength, n.LastError!.Length);
        Assert.Equal(NotificationRetryPolicy.MaxErrorLength, 2000);
    }

    [Fact]
    public void Claim_SetsSendingAndLease()
    {
        var n = new Notification { Status = NotificationStatus.Pending, NextAttemptAt = Now };

        NotificationRetryPolicy.Claim(n, Now, TimeSpan.FromMinutes(1));

        Assert.Equal(NotificationStatus.Sending, n.Status);
        Assert.Equal(Now.AddMinutes(1), n.NextAttemptAt);
    }

    [Fact]
    public void MarkSent_SetsSentAndSentAt()
    {
        var n = new Notification { Status = NotificationStatus.Sending };

        NotificationRetryPolicy.MarkSent(n, Now);

        Assert.Equal(NotificationStatus.Sent, n.Status);
        Assert.Equal(Now, n.SentAt);
    }

    [Fact]
    public void Release_ReturnsToPendingDueNowWithoutCountingRetry()
    {
        var n = new Notification { Status = NotificationStatus.Sending, NextAttemptAt = Now.AddMinutes(1), RetryCount = 1 };

        NotificationRetryPolicy.Release(n, Now);

        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.Equal(Now, n.NextAttemptAt);
        Assert.Equal(1, n.RetryCount);
    }
}

public class NotificationEmailBuilderTests
{
    private static Attempt CompletedAttempt(int correct = 5, int total = 6, double score = 83.33) => new()
    {
        User = new User { Name = "Ada Lovelace", Email = "ada@example.com" },
        Quiz = new Quiz { Title = "LLM Fundamentals" },
        Status = AttemptStatus.Completed,
        CompletedAt = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc),
        CorrectCount = correct,
        TotalQuestions = total,
        Score = score,
    };

    [Fact]
    public void Build_CompletedAttempt_ContainsAllRequiredFields()
    {
        var msg = NotificationEmailBuilder.Build(CompletedAttempt());

        Assert.Equal("ada@example.com", msg.ToEmail);
        Assert.Equal("Ada Lovelace", msg.ToName);
        Assert.Contains("LLM Fundamentals", msg.Subject);
        Assert.Contains("83.33%", msg.Subject);
        Assert.Contains("Ada Lovelace", msg.Body);
        Assert.Contains("ada@example.com", msg.Body);
        Assert.Contains("LLM Fundamentals", msg.Body);
        Assert.Contains("5/6", msg.Body);
        Assert.Contains("83.33%", msg.Body);
        Assert.Contains(PerformanceMessages.Excellent, msg.Body);
        Assert.Contains("2026-03-04 05:06 UTC", msg.Body);
    }

    [Theory]
    [InlineData(6, 6, 100, "100%", PerformanceMessages.Perfect)]
    [InlineData(0, 6, 0, "0%", PerformanceMessages.Low)]
    [InlineData(1, 2, 50, "50%", PerformanceMessages.Fair)]
    public void Build_VariousScores_FormatsPercentageAndMessage(int correct, int total, double score, string pct, string message)
    {
        var msg = NotificationEmailBuilder.Build(CompletedAttempt(correct, total, score));

        Assert.Contains($"{correct}/{total}", msg.Body);
        Assert.Contains($"({pct})", msg.Body);
        Assert.Contains(message, msg.Body);
    }

    [Fact]
    public void Build_MissingNavigation_Throws()
    {
        var attempt = CompletedAttempt();
        attempt.User = null!;

        Assert.Throws<ArgumentNullException>(() => NotificationEmailBuilder.Build(attempt));
    }
}
