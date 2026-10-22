using System.Net;
using Api;
using Api.Alerts;
using Api.Database;
using Api.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Api.Tests;

public sealed class SchedulerServiceTests : IDisposable
{
    private readonly TargetHandler _target = new();
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"cron-sched-{Guid.NewGuid():N}.db");
    private readonly DatabaseInitializer _db;
    private readonly JobRepository _jobs;
    private readonly RunRepository _runs;
    private readonly FakeTimeProvider _clock = new();
    private readonly SchedulerService _scheduler;

    public SchedulerServiceTests()
    {
        _db = new DatabaseInitializer(_dbPath);
        _db.Initialize();
        _jobs = new JobRepository(_db);
        _runs = new RunRepository(_db);

        var services = new ServiceCollection();
        services.AddHttpClient("job-dispatch")
            .ConfigurePrimaryHttpMessageHandler(() => _target);
        services.AddHttpClient("arauth-token")
            .ConfigurePrimaryHttpMessageHandler(() => _target);
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        var dispatcher = new JobDispatcher(
            factory,
            _runs,
            new ArAuthTokenClient(factory, NullLogger<ArAuthTokenClient>.Instance),
            new AlertSender(factory, NullLogger<AlertSender>.Instance),
            _clock,
            new SchedulerOptions { RetryBaseDelayMs = 0, RetryMaxDelayMs = 0, HttpTimeoutSeconds = 5 },
            new AlertOptions { WebhookUrl = null },
            new AuthOptions { TokenEndpoint = "http://target.internal/devtest/token" },
            NullLogger<JobDispatcher>.Instance);

        _scheduler = new SchedulerService(
            _jobs, _runs, dispatcher, _clock,
            new SchedulerOptions(),
            NullLogger<SchedulerService>.Instance);
    }

    [Fact]
    public async Task First_cycle_syncs_cache_and_initializes_new_jobs_without_dispatching()
    {
        var job = InsertJob();

        _clock.SetUtcNow(new DateTimeOffset(2026, 10, 10, 10, 30, 0, TimeSpan.Zero));
        await _scheduler.ProcessAllAsync(CancellationToken.None);

        // First-run path only advances the cursor; nothing goes to the target yet.
        Assert.Empty(_target.Hits);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, _jobs.GetById(job.Id)!.LastDispatchedTickUtc);
    }

    [Fact]
    public async Task Due_tick_dispatches_once_and_idle_cycles_do_not_redispatch()
    {
        var job = InsertJob();

        _clock.SetUtcNow(new DateTimeOffset(2026, 10, 10, 10, 30, 0, TimeSpan.Zero));
        await _scheduler.ProcessAllAsync(CancellationToken.None); // first-run: cursor -> 10:30, next due 11:00

        _clock.SetUtcNow(new DateTimeOffset(2026, 10, 10, 11, 0, 0, TimeSpan.Zero));
        await _scheduler.ProcessAllAsync(CancellationToken.None); // 11:00 tick expires -> dispatch

        Assert.Single(_target.Hits);

        // Same instant, no job changes: fast path must not touch the target (or the DB).
        await _scheduler.ProcessAllAsync(CancellationToken.None);
        await _scheduler.ProcessAllAsync(CancellationToken.None);

        Assert.Single(_target.Hits);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, _jobs.GetById(job.Id)!.LastDispatchedTickUtc);
    }

    [Fact]
    public async Task Job_created_after_initial_sync_is_picked_up_via_version_bump()
    {
        var jobA = InsertJob();
        _clock.SetUtcNow(new DateTimeOffset(2026, 10, 10, 10, 30, 0, TimeSpan.Zero));
        await _scheduler.ProcessAllAsync(CancellationToken.None); // cache built with jobA only

        var jobB = InsertJob(cron: "0 0 12 * * *");
        await _scheduler.ProcessAllAsync(CancellationToken.None); // version changed -> rebuild + process jobB

        // jobB got its first-run cursor write even though it was created after the initial sync.
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, _jobs.GetById(jobB.Id)!.LastDispatchedTickUtc);

        // jobA (next due 11:00) was not dispatched.
        Assert.Empty(_target.Hits);

        _clock.SetUtcNow(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
        await _scheduler.ProcessAllAsync(CancellationToken.None); // jobA 11:00+12:00 ticks and jobB 12:00 tick all due

        // Both jobs hit the same target URL, so one key accumulates: jobA 2 dispatches + jobB 1.
        Assert.Equal(3, _target.Hits.Values.Sum());
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, _jobs.GetById(jobA.Id)!.LastDispatchedTickUtc);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, _jobs.GetById(jobB.Id)!.LastDispatchedTickUtc);
    }

    [Fact]
    public async Task Deleted_job_is_evicted_when_its_cached_tick_expires()
    {
        InsertJob();

        _clock.SetUtcNow(new DateTimeOffset(2026, 10, 10, 10, 30, 0, TimeSpan.Zero));
        await _scheduler.ProcessAllAsync(CancellationToken.None); // cache: next due 11:00

        // Delete while the 11:00 tick is still cached but not yet dispatched.
        _jobs.Delete(_jobs.GetAll().Single().Id);
        _clock.SetUtcNow(new DateTimeOffset(2026, 10, 10, 11, 0, 0, TimeSpan.Zero));
        await _scheduler.ProcessAllAsync(CancellationToken.None);

        // Existence check catches the deletion: no dispatch, no run rows, no crash.
        Assert.Empty(_target.Hits);
        Assert.Empty(_runs.GetRecentForJob(1));
    }

    private CronJob InsertJob(string cron = "0 0 * * * *")
    {
        var job = new CronJob
        {
            Name = $"job-{Guid.NewGuid():N}",
            TargetUrl = "http://target.internal/devtest/ok",
            CronExpression = cron,
            Timezone = "UTC",
            Enabled = true,
            CatchupDays = 1,
            MaxRetries = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _jobs.Insert(job);
        return job;
    }

    private sealed class TargetHandler : HttpMessageHandler
    {
        public Dictionary<string, int> Hits { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.RequestUri!.PathAndQuery;
            Hits[key] = Hits.GetValueOrDefault(key) + 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
