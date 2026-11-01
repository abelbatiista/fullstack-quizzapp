using Microsoft.Extensions.Options;

namespace QuizApp.Api.Features.Notifications;

public record EmailMessage(string ToEmail, string ToName, string Subject, string Body);

public interface IEmailSender
{
    /// <summary>Sends the message or throws. Must honor <paramref name="ct"/>.</summary>
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

public sealed class EmailDeliveryException(string message) : Exception(message);

/// <summary>Logs the email instead of sending it, and fails randomly according to <see cref="EmailOptions.FailureRate"/>.</summary>
public sealed class MockEmailSender(IOptionsMonitor<EmailOptions> options, ILogger<MockEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.CurrentValue;

        if (o.SimulatedLatency > TimeSpan.Zero)
            await Task.Delay(o.SimulatedLatency, ct);

        // NextDouble() is in [0, 1): FailureRate 0 never fails, FailureRate 1 always fails.
        if (Random.Shared.NextDouble() < o.FailureRate)
            throw new EmailDeliveryException(
                $"Simulated SMTP failure sending to {message.ToEmail} (FailureRate={o.FailureRate}).");

        logger.LogInformation(
            "[MockEmail] To: {ToName} <{ToEmail}>\nSubject: {Subject}\n{Body}",
            message.ToName, message.ToEmail, message.Subject, message.Body);
    }
}
