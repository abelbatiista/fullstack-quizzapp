using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuizApp.Api.Data;
using QuizApp.Api.Domain;
using QuizApp.Api.Features.Notifications;
using QuizApp.Tests.Fakes;

namespace QuizApp.Tests.Features;

public sealed class NotificationDispatcherServiceTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(T0));
    private readonly NotificationOptions _options = new()
    {
        Enabled = true,
        MaxRetries = 2,
        BaseDelay = TimeSpan.FromSeconds(2),
        MaxDelay = TimeSpan.FromMinutes(1),
        LeaseDuration = TimeSpan.FromMinutes(1),
        BatchSize = 20,
    };

    private ServiceProvider? _provider;

    public NotificationDispatcherServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose()
    {
        _provider?.Dispose();
        _connection.Dispose();
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private NotificationDispatcherService CreateDispatcher(IEmailSender sender)
    {
        var services = new ServiceCollection();
        services.AddDbContext<QuizDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton(sender);
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            db.Database.EnsureCreated();
            DbSeeder.Seed(db);
        }

        return new NotificationDispatcherService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<NotificationOptions>(_options),
            _clock,
            NullLogger<NotificationDispatcherService>.Instance);
    }

    private QuizDbContext NewDb() =>
        _provider!.CreateScope().ServiceProvider.GetRequiredService<QuizDbContext>();

    /// <summary>Creates a user + completed attempt (5/6, 83.33%) + notification. Returns the notification id.</summary>
    private int AddNotification(
        string name = "Ada Lovelace",
        string email = "ada@example.com",
        NotificationStatus status = NotificationStatus.Pending,
        DateTime? nextAttemptAt = null,
        int retryCount = 0)
    {
        using var db = NewDb();
        var quiz = db.Quizzes.OrderBy(q => q.Id).First();
        var user = new User { Name = name, Email = email, CreatedAt = Now };
        var attempt = new Attempt
        {
            User = user,
            QuizId = quiz.Id,
            Status = AttemptStatus.Completed,
            StartedAt = Now.AddMinutes(-5),
            CompletedAt = Now,
            CorrectCount = 5,
            TotalQuestions = 6,
            Score = 83.33,
            Notification = new Notification
            {
                Status = status,
                CreatedAt = Now,
                NextAttemptAt = nextAttemptAt ?? Now,
                RetryCount = retryCount,
            },
        };
        db.Attempts.Add(attempt);
        db.SaveChanges();
        return attempt.Notification.Id;
    }

    private Notification Load(int id)
    {
        using var db = NewDb();
        return db.Notifications.AsNoTracking().Single(n => n.Id == id);
    }

    [Fact]
    public async Task DispatchDueAsync_SenderSucceeds_MarksSentWithFullEmail()
    {
        var sender = new RecordingEmailSender();
        var dispatcher = CreateDispatcher(sender);
        var id = AddNotification();

        var processed = await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        var n = Load(id);
        Assert.Equal(NotificationStatus.Sent, n.Status);
        Assert.Equal(T0, n.SentAt);
        Assert.Equal(DateTimeKind.Utc, n.SentAt!.Value.Kind);
        Assert.Equal(0, n.RetryCount);
        Assert.Null(n.LastError);

        var msg = Assert.Single(sender.Delivered);
        Assert.Equal("ada@example.com", msg.ToEmail);
        Assert.Equal("Ada Lovelace", msg.ToName);
        Assert.Contains("Ada Lovelace", msg.Body);
        Assert.Contains("ada@example.com", msg.Body);
        Assert.Contains("LLM Fundamentals for Developers", msg.Body);
        Assert.Contains("5/6", msg.Body);
        Assert.Contains("83.33%", msg.Body);
        Assert.Contains(PerformanceMessages.Excellent, msg.Body);
    }

    [Fact]
    public async Task DispatchDueAsync_SentRow_IsNotSentAgain()
    {
        var sender = new RecordingEmailSender();
        var dispatcher = CreateDispatcher(sender);
        AddNotification();

        await dispatcher.DispatchDueAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromHours(1));
        var second = await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(0, second);
        Assert.Equal(1, sender.CallCount);
    }

    [Fact]
    public async Task DispatchDueAsync_SenderAlwaysThrows_RetriesWithBackoffThenFails()
    {
        var sender = new RecordingEmailSender { AlwaysThrow = true };
        var dispatcher = CreateDispatcher(sender);
        var id = AddNotification();

        // Send 1 fails → retry 1 scheduled at +2s.
        await dispatcher.DispatchDueAsync(CancellationToken.None);
        var n = Load(id);
        Assert.Equal(1, sender.CallCount);
        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.Equal(1, n.RetryCount);
        Assert.Equal(T0.AddSeconds(2), n.NextAttemptAt);
        Assert.Equal("EmailDeliveryException: SMTP down for ada@example.com", n.LastError);
        Assert.Null(n.SentAt);

        // Not due yet: nothing happens.
        _clock.Advance(TimeSpan.FromSeconds(1.9));
        Assert.Equal(0, await dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Equal(1, sender.CallCount);

        // Send 2 fails → retry 2 scheduled at +4s.
        _clock.Advance(TimeSpan.FromSeconds(0.1));
        var t2 = Now;
        await dispatcher.DispatchDueAsync(CancellationToken.None);
        n = Load(id);
        Assert.Equal(2, sender.CallCount);
        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.Equal(2, n.RetryCount);
        Assert.Equal(t2.AddSeconds(4), n.NextAttemptAt);
        Assert.NotNull(n.LastError);

        // Send 3 fails → retries exhausted → Failed.
        _clock.Advance(TimeSpan.FromSeconds(4));
        await dispatcher.DispatchDueAsync(CancellationToken.None);
        n = Load(id);
        Assert.Equal(3, sender.CallCount);
        Assert.Equal(NotificationStatus.Failed, n.Status);
        Assert.Equal(2, n.RetryCount);
        Assert.Contains("EmailDeliveryException", n.LastError);
        Assert.Null(n.SentAt);

        // Terminal: further polls send nothing.
        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, await dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Equal(0, await dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Equal(3, sender.CallCount);
        Assert.Equal(NotificationStatus.Failed, Load(id).Status);
    }

    [Fact]
    public async Task DispatchDueAsync_NotYetDue_IsSkipped()
    {
        var sender = new RecordingEmailSender();
        var dispatcher = CreateDispatcher(sender);
        var id = AddNotification(nextAttemptAt: T0.AddSeconds(10));

        var processed = await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(0, processed);
        Assert.Equal(0, sender.CallCount);
        var n = Load(id);
        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.Equal(T0.AddSeconds(10), n.NextAttemptAt);
    }

    [Fact]
    public async Task DispatchDueAsync_DueExactlyNow_IsSent()
    {
        var sender = new RecordingEmailSender();
        var dispatcher = CreateDispatcher(sender);
        var id = AddNotification(nextAttemptAt: T0.AddSeconds(10));

        _clock.Advance(TimeSpan.FromSeconds(10));
        await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(NotificationStatus.Sent, Load(id).Status);
    }

    [Fact]
    public async Task DispatchDueAsync_SentAndFailedRows_AreSkipped()
    {
        var sender = new RecordingEmailSender();
        var dispatcher = CreateDispatcher(sender);
        AddNotification(email: "sent@example.com", status: NotificationStatus.Sent, nextAttemptAt: T0.AddMinutes(-1));
        AddNotification(email: "failed@example.com", status: NotificationStatus.Failed, nextAttemptAt: T0.AddMinutes(-1));

        Assert.Equal(0, await dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Equal(0, sender.CallCount);
    }

    [Fact]
    public async Task DispatchDueAsync_OneRecipientThrows_OthersInBatchStillSent()
    {
        var sender = new RecordingEmailSender();
        sender.FailFor.Add("bad@example.com");
        var dispatcher = CreateDispatcher(sender);
        // The failing row is oldest, so it is processed first.
        var bad = AddNotification("Bad", "bad@example.com", nextAttemptAt: T0.AddSeconds(-3));
        var good1 = AddNotification("Good One", "good1@example.com", nextAttemptAt: T0.AddSeconds(-2));
        var good2 = AddNotification("Good Two", "good2@example.com", nextAttemptAt: T0.AddSeconds(-1));

        var processed = await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(3, processed);
        Assert.Equal(3, sender.CallCount);
        Assert.Equal("bad@example.com", sender.Attempts.First().ToEmail);

        var b = Load(bad);
        Assert.Equal(NotificationStatus.Pending, b.Status);
        Assert.Equal(1, b.RetryCount);
        Assert.Contains("bad@example.com", b.LastError);

        Assert.Equal(NotificationStatus.Sent, Load(good1).Status);
        Assert.Equal(NotificationStatus.Sent, Load(good2).Status);
        Assert.Equal(["good1@example.com", "good2@example.com"], sender.Delivered.Select(m => m.ToEmail));
    }

    [Fact]
    public async Task DispatchDueAsync_BatchSizeLimit_ProcessesOldestFirst()
    {
        _options.BatchSize = 1;
        var sender = new RecordingEmailSender();
        var dispatcher = CreateDispatcher(sender);
        var newer = AddNotification("Newer", "newer@example.com", nextAttemptAt: T0.AddSeconds(-1));
        var older = AddNotification("Older", "older@example.com", nextAttemptAt: T0.AddSeconds(-5));

        Assert.Equal(1, await dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Equal(NotificationStatus.Sent, Load(older).Status);
        Assert.Equal(NotificationStatus.Pending, Load(newer).Status);

        Assert.Equal(1, await dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Equal(NotificationStatus.Sent, Load(newer).Status);
    }

    [Fact]
    public async Task DispatchDueAsync_CancelledMidSend_ReleasesToPendingAndRethrows()
    {
        var sender = new BlockingEmailSender();
        var dispatcher = CreateDispatcher(sender);
        var id = AddNotification();
        using var cts = new CancellationTokenSource();

        var dispatch = dispatcher.DispatchDueAsync(cts.Token);
        await sender.Started.WaitAsync(TimeSpan.FromSeconds(10));

        // While the send is in flight, the row is claimed with a lease.
        var claimed = Load(id);
        Assert.Equal(NotificationStatus.Sending, claimed.Status);
        Assert.Equal(T0 + _options.LeaseDuration, claimed.NextAttemptAt);

        _clock.Advance(TimeSpan.FromSeconds(3));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatch);

        var n = Load(id);
        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.Equal(0, n.RetryCount);
        Assert.Null(n.LastError);
        Assert.Equal(Now, n.NextAttemptAt); // due again right away
    }

    [Fact]
    public async Task DispatchDueAsync_CancelledBeforeStart_DoesNotTouchRows()
    {
        var sender = new RecordingEmailSender();
        var dispatcher = CreateDispatcher(sender);
        var id = AddNotification();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.DispatchDueAsync(cts.Token));

        Assert.Equal(0, sender.CallCount);
        Assert.Equal(NotificationStatus.Pending, Load(id).Status);
    }

    [Fact]
    public async Task DispatchDueAsync_ExpiredSendingLease_IsPickedUpAgain()
    {
        var sender = new RecordingEmailSender();
        var dispatcher = CreateDispatcher(sender);
        var id = AddNotification(status: NotificationStatus.Sending, nextAttemptAt: T0.AddSeconds(-1));

        var processed = await dispatcher.DispatchDueAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Equal(1, sender.CallCount);
        Assert.Equal(NotificationStatus.Sent, Load(id).Status);
    }

    [Fact]
    public async Task DispatchDueAsync_ActiveSendingLease_IsSkipped()
    {
        var sender = new RecordingEmailSender();
        var dispatcher = CreateDispatcher(sender);
        var id = AddNotification(status: NotificationStatus.Sending, nextAttemptAt: T0.AddSeconds(30));

        Assert.Equal(0, await dispatcher.DispatchDueAsync(CancellationToken.None));
        Assert.Equal(0, sender.CallCount);
        Assert.Equal(NotificationStatus.Sending, Load(id).Status);
    }

    [Fact]
    public async Task DispatchDueAsync_SuccessAfterEarlierFailure_KeepsRetryCountAndSends()
    {
        var sender = new RecordingEmailSender { AlwaysThrow = true };
        var dispatcher = CreateDispatcher(sender);
        var id = AddNotification();

        await dispatcher.DispatchDueAsync(CancellationToken.None);
        sender.AlwaysThrow = false;
        _clock.Advance(TimeSpan.FromSeconds(2));
        await dispatcher.DispatchDueAsync(CancellationToken.None);

        var n = Load(id);
        Assert.Equal(NotificationStatus.Sent, n.Status);
        Assert.Equal(1, n.RetryCount);
        Assert.Equal(T0.AddSeconds(2), n.SentAt);
        Assert.Equal(2, sender.CallCount);
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
