using System.ComponentModel.DataAnnotations;

namespace QuizApp.Api.Features.Notifications;

public sealed class NotificationOptions
{
    public const string Section = "Notifications";

    /// <summary>Turn the dispatcher off (e.g. in integration tests that drive it manually).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How long the worker waits between polls.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Delay before the first retry. Retry n waits BaseDelay * 2^(n-1), capped at MaxDelay.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Retries after the first attempt. Total sends = MaxRetries + 1, then the row becomes Failed.</summary>
    [Range(0, 50)]
    public int MaxRetries { get; set; } = 5;

    [Range(1, 500)]
    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// How long a claimed (Sending) row is reserved. If the process dies mid-send, the row becomes due
    /// again once the lease expires, so it is never stuck in Sending.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>Probability (0–1) that the mock sender throws. 0 = never, 1 = always.</summary>
    [Range(0.0, 1.0)]
    public double FailureRate { get; set; } = 0.2;

    /// <summary>Fake network latency, so a shutdown can actually interrupt a send.</summary>
    public TimeSpan SimulatedLatency { get; set; } = TimeSpan.FromMilliseconds(200);
}
