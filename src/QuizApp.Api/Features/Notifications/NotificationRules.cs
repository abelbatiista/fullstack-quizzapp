using System.Globalization;
using QuizApp.Api.Domain;

namespace QuizApp.Api.Features.Notifications;

/// <summary>Pure retry and state-transition rules for the outbox. No EF, no clock.</summary>
public static class NotificationRetryPolicy
{
    public const int MaxErrorLength = 2000;

    /// <summary>Delay before retry number <paramref name="retryNumber"/> (1-based): base * 2^(n-1), capped.</summary>
    public static TimeSpan ComputeBackoff(int retryNumber, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retryNumber, 1);

        // Compare in double so large retry numbers cap instead of overflowing.
        var ticks = baseDelay.Ticks * Math.Pow(2, retryNumber - 1);
        return ticks >= maxDelay.Ticks ? maxDelay : TimeSpan.FromTicks((long)ticks);
    }

    /// <summary>Claim a due row: mark Sending and reserve it for the lease duration.</summary>
    public static void Claim(Notification n, DateTime now, TimeSpan leaseDuration)
    {
        n.Status = NotificationStatus.Sending;
        n.NextAttemptAt = now + leaseDuration;
    }

    public static void MarkSent(Notification n, DateTime now)
    {
        n.Status = NotificationStatus.Sent;
        n.SentAt = now;
    }

    /// <summary>Records the error, then either schedules a retry with backoff or marks the row Failed.</summary>
    public static void MarkFailed(Notification n, string error, DateTime now, NotificationOptions options)
    {
        n.LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;

        if (n.RetryCount >= options.MaxRetries)
        {
            n.Status = NotificationStatus.Failed;
            return;
        }

        n.RetryCount++;
        n.Status = NotificationStatus.Pending;
        n.NextAttemptAt = now + ComputeBackoff(n.RetryCount, options.BaseDelay, options.MaxDelay);
    }

    /// <summary>Cancelled mid-send: release the claim so the row is due again right away.</summary>
    public static void Release(Notification n, DateTime now)
    {
        n.Status = NotificationStatus.Pending;
        n.NextAttemptAt = now;
    }
}

/// <summary>Pure builder for the result email.</summary>
public static class NotificationEmailBuilder
{
    public static EmailMessage Build(Attempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt.User);
        ArgumentNullException.ThrowIfNull(attempt.Quiz);

        var inv = CultureInfo.InvariantCulture;
        var completedAt = attempt.CompletedAt is { } c
            ? c.ToString("yyyy-MM-dd HH:mm 'UTC'", inv)
            : "unknown";

        var body = string.Join('\n',
            $"Hi {attempt.User.Name},",
            "",
            $"You completed \"{attempt.Quiz.Title}\" on {completedAt}.",
            $"Result: {attempt.CorrectCount}/{attempt.TotalQuestions} correct ({attempt.Score.ToString("0.##", inv)}%).",
            PerformanceMessages.For(attempt.Score),
            "",
            $"Sent to {attempt.User.Email}. QuizApp");

        return new EmailMessage(
            attempt.User.Email,
            attempt.User.Name,
            $"Your result for \"{attempt.Quiz.Title}\": {attempt.Score.ToString("0.##", inv)}%",
            body);
    }
}
