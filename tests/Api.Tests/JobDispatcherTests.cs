using Api;
using Api.Alerts;
using Api.Database;
using Api.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Api.Tests;

public sealed class JobDispatcherTests : IDisposable
{
    private readonly TargetHandler _target = new();
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"cron-disp-{Guid.NewGuid():N}.db");
    private readonly DatabaseInitializer _db;
    private readonly JobRepository _jobs;
    private readonly RunRepository _runs;

    public JobDispatcherTests()
    {
        _db = new DatabaseInitializer(_dbPath);
        _db.Initialize();
        _jobs = new JobRepository(_db);
        _runs = new RunRepository(_db);
    }

    private JobDispatcher BuildDispatcher()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("job-dispatch")
            .ConfigurePrimaryHttpMessageHandler(() => _target);
        services.AddHttpClient("arauth-token")
            .ConfigurePrimaryHttpMessageHandler(() => _target);

        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        return new JobDispatcher(
            factory,
            _runs,
            new ArAuthTokenClient(factory, TokenLogger),
            new AlertSender(factory, AlertLogger),
            new FakeTimeProvider(),
            new SchedulerOptions { RetryBaseDelayMs = 0, RetryMaxDelayMs = 0, HttpTimeoutSeconds = 5 },
            new AlertOptions { WebhookUrl = null },
            new AuthOptions
            {
                TokenEndpoint = "http://target.internal/devtest/token",
                ClientId = "cron-agent",
                ClientSecret = "secret"
            },
            NullLogger<JobDispatcher>.Instance);
    }

    private CronJob CreateJob(CronJob job)
    {
        _jobs.Insert(job);
        return job;
    }

    [Fact]
    public async Task Success_first_attempt_records_single_succeeded_run()
    {
        var dispatcher = BuildDispatcher();
        var job = CreateJob(NewJob(url: "http://target.internal/devtest/ok"));
        var tick = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

        var result = await dispatcher.DispatchAsync(job, tick, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.Attempts);
        Assert.Equal(200, result.HttpStatus);
        Assert.Single(_target.Hits);
    }

    [Fact]
    public async Task Failing_target_retries_then_fails_with_run_rows()
    {
        var dispatcher = BuildDispatcher();
        var job = CreateJob(NewJob(url: "http://target.internal/devtest/fail", maxRetries: 3));
        var tick = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

        var result = await dispatcher.DispatchAsync(job, tick, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(500, result.HttpStatus);
        Assert.Equal(3, _target.Hits.Values.Sum());

        var runs = _runs.GetRecentForJob(job.Id);
        Assert.Equal(3, runs.Count);
        Assert.All(runs, r => Assert.Equal(RunStatus.Failed, r.Status));
    }

    [Fact]
    public async Task Date_param_is_appended_when_flag_set()
    {
        var dispatcher = BuildDispatcher();
        var job = CreateJob(new CronJob
        {
            Name = "dated",
            TargetUrl = "http://target.internal/devtest/echo?code=k",
            Timezone = "Asia/Kuala_Lumpur",
            AppendDateParam = true,
            Enabled = true,
            MaxRetries = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        var tick = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

        var result = await dispatcher.DispatchAsync(job, tick, CancellationToken.None);

        Assert.True(result.Success);
        var host = _target.Hits.Keys.Single(k => k.StartsWith("/devtest/echo"));
        Assert.Contains("code=k", host);
        Assert.Contains("date=2026-10-10", host);
    }

    [Fact]
    public async Task ArAuth_job_obtains_token_and_sends_bearer()
    {
        var dispatcher = BuildDispatcher();
        var job = CreateJob(new CronJob
        {
            Name = "arauth",
            TargetUrl = "http://target.internal/devtest/echo",
            Enabled = true,
            MaxRetries = 1,
            AuthMode = TargetAuthMode.ArAuth,
            AuthScope = "api://some-app/run:job",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        var result = await dispatcher.DispatchAsync(job, DateTime.UtcNow, CancellationToken.None);

        Assert.True(result.Success, result.Error ?? "no error detail");
        Assert.Contains("Bearer token-abc", _target.Hits.Keys.First(k => k.StartsWith("/devtest/echo")));
    }

    [Fact]
    public async Task ArAuth_job_without_configured_client_fails_fast()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("job-dispatch")
            .ConfigurePrimaryHttpMessageHandler(() => _target);
        services.AddHttpClient("arauth-token")
            .ConfigurePrimaryHttpMessageHandler(() => _target);
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        var dispatcher = new JobDispatcher(
            factory,
            _runs,
            new ArAuthTokenClient(factory, TokenLogger),
            new AlertSender(factory, AlertLogger),
            new FakeTimeProvider(),
            new SchedulerOptions { RetryBaseDelayMs = 0, RetryMaxDelayMs = 0, HttpTimeoutSeconds = 5 },
            new AlertOptions { WebhookUrl = null },
            new AuthOptions { TokenEndpoint = "http://target.internal/devtest/token" },
            NullLogger<JobDispatcher>.Instance);

        var job = CreateJob(new CronJob
        {
            Name = "arauth-bad",
            TargetUrl = "http://target.internal/devtest/echo",
            Enabled = true,
            MaxRetries = 3,
            AuthMode = TargetAuthMode.ArAuth,
            AuthScope = "api://some-app/run:job",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        var result = await dispatcher.DispatchAsync(job, DateTime.UtcNow, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Atlas Rig", result.Error);
        Assert.DoesNotContain(_target.Hits.Keys, k => k.StartsWith("/devtest/echo", StringComparison.OrdinalIgnoreCase));
        Assert.Single(_runs.GetRecentForJob(job.Id));
    }

    [Fact]
    public void BuildTargetUrl_without_date_flag_is_unchanged()
    {
        var dispatcher = BuildDispatcher();
        var job = new CronJob { TargetUrl = "https://x/api/y?code=1", Timezone = "UTC" };
        Assert.Equal(job.TargetUrl, dispatcher.BuildTargetUrl(job, DateTime.UtcNow));
    }

    private static CronJob NewJob(string url, int maxRetries = 1) => new()
    {
        Name = $"job-{Guid.NewGuid():N}",
        TargetUrl = url,
        Timezone = "UTC",
        Enabled = true,
        MaxRetries = maxRetries,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static ILogger<ArAuthTokenClient> TokenLogger => NullLogger<ArAuthTokenClient>.Instance;
    private static ILogger<AlertSender> AlertLogger => NullLogger<AlertSender>.Instance;

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
