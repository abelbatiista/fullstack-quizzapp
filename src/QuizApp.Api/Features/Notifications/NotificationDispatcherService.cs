using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuizApp.Api.Data;
using QuizApp.Api.Domain;

namespace QuizApp.Api.Features.Notifications;

/// <summary>
/// Outbox dispatcher: polls due notifications and delivers them through <see cref="IEmailSender"/>.
/// Each notification runs in its own DI scope and its own try/catch, so one bad row never stops the batch.
/// </summary>
public sealed class NotificationDispatcherService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<NotificationOptions> options,
    TimeProvider clock,
    ILogger<NotificationDispatcherService> logger) : BackgroundService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Enabled)
        {
            logger.LogInformation("Notification dispatcher is disabled.");
            return;
        }

        logger.LogInformation("Notification dispatcher started (poll {PollInterval}, max retries {MaxRetries}).",
            options.CurrentValue.PollInterval, options.CurrentValue.MaxRetries);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // e.g. the database is briefly locked: log it and try again on the next poll.
                logger.LogError(ex, "Notification poll failed; will retry on the next interval.");
            }

            try
            {
                await Task.Delay(options.CurrentValue.PollInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Notification dispatcher stopped.");
    }

    /// <summary>Processes one batch of due notifications. Public so tests can drive it without the timer.</summary>
    public async Task<int> DispatchDueAsync(CancellationToken ct)
    {
        var o = options.CurrentValue;
        List<int> dueIds;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            var now = Now;
            // Sending rows are included: an expired lease means the previous send never finished.
            dueIds = await db.Notifications
                .AsNoTracking()
                .Where(n => (n.Status == NotificationStatus.Pending || n.Status == NotificationStatus.Sending)
                            && n.NextAttemptAt <= now)
                .OrderBy(n => n.NextAttemptAt).ThenBy(n => n.Id)
                .Select(n => n.Id)
                .Take(o.BatchSize)
                .ToListAsync(ct);
        }

        foreach (var id in dueIds)
        {
            ct.ThrowIfCancellationRequested();
            await DispatchOneAsync(id, o, ct);
        }

        return dueIds.Count;
    }

    private async Task DispatchOneAsync(int id, NotificationOptions o, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        Notification? n = null;

        try
        {
            n = await db.Notifications
                .Include(x => x.Attempt).ThenInclude(a => a.User)
                .Include(x => x.Attempt).ThenInclude(a => a.Quiz)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            // Re-check: the row may have changed since the batch query.
            if (n is null
                || n.Status is NotificationStatus.Sent or NotificationStatus.Failed
                || n.NextAttemptAt > Now)
                return;

            NotificationRetryPolicy.Claim(n, Now, o.LeaseDuration);
            await db.SaveChangesAsync(ct);

            var message = NotificationEmailBuilder.Build(n.Attempt);
            await sender.SendAsync(message, ct);

            NotificationRetryPolicy.MarkSent(n, Now);
            // The email is already out: don't let a shutdown token cancel recording that.
            await db.SaveChangesAsync(CancellationToken.None);

            logger.LogInformation("Notification {NotificationId} for attempt {AttemptId} sent to {Email} (retries: {RetryCount}).",
                n.Id, n.AttemptId, message.ToEmail, n.RetryCount);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (n is { Status: NotificationStatus.Sending })
            {
                NotificationRetryPolicy.Release(n, Now);
                await TrySaveAsync(db, n.Id, "release cancelled notification");
                logger.LogInformation("Notification {NotificationId} cancelled mid-send; returned to Pending.", n.Id);
            }
            throw;
        }
        catch (Exception ex)
        {
            if (n is null)
            {
                logger.LogError(ex, "Could not load notification {NotificationId}.", id);
                return;
            }

            NotificationRetryPolicy.MarkFailed(n, $"{ex.GetType().Name}: {ex.Message}", Now, o);
            await TrySaveAsync(db, n.Id, "record delivery failure");

            if (n.Status == NotificationStatus.Failed)
                logger.LogError("Notification {NotificationId} for attempt {AttemptId} failed permanently after {RetryCount} retries: {Error}",
                    n.Id, n.AttemptId, n.RetryCount, n.LastError);
            else
                logger.LogWarning("Notification {NotificationId} failed (retry {RetryCount}/{MaxRetries}), next try at {NextAttemptAt:O}: {Error}",
                    n.Id, n.RetryCount, o.MaxRetries, n.NextAttemptAt, n.LastError);
        }
    }

    private async Task TrySaveAsync(QuizDbContext db, int id, string action)
    {
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The lease still expires, so the row will be picked up again later.
            logger.LogError(ex, "Could not {Action} for notification {NotificationId}; it will be retried after its lease expires.", action, id);
        }
    }
}
