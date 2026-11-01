using System.Collections.Concurrent;
using QuizApp.Api.Features.Notifications;

namespace QuizApp.Tests.Fakes;

/// <summary>A clock that only moves when the test says so.</summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
/// Records every send. Throws for recipients in <see cref="FailFor"/>, or for everyone when
/// <see cref="AlwaysThrow"/> is set.
/// </summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Attempts { get; } = new();
    public List<EmailMessage> Delivered { get; } = [];
    public bool AlwaysThrow { get; set; }
    public HashSet<string> FailFor { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int CallCount => Attempts.Count;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        Attempts.Enqueue(message);

        if (AlwaysThrow || FailFor.Contains(message.ToEmail))
            throw new EmailDeliveryException($"SMTP down for {message.ToEmail}");

        lock (Delivered) Delivered.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>Blocks until cancelled, signalling when the send has started.</summary>
public sealed class BlockingEmailSender : IEmailSender
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => _started.Task;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        _started.TrySetResult();
        await Task.Delay(Timeout.Infinite, ct);
    }
}
