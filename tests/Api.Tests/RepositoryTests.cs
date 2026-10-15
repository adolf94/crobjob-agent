using Api.Database;

namespace Api.Tests;

public sealed class RepositoryTests : IDisposable
{
    private readonly DatabaseInitializer _db;
    private readonly JobRepository _jobs;
    private readonly RunRepository _runs;

    public RepositoryTests()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cron-repo-{Guid.NewGuid():N}.db");
        _db = new DatabaseInitializer(path);
        _db.Initialize();
        _jobs = new JobRepository(_db);
        _runs = new RunRepository(_db);
    }

    [Fact]
    public void Job_crud_roundtrip()
    {
        var job = new CronJob
        {
            Name = "interest-accrual",
            TargetUrl = "https://loans.azurewebsites.net/api/interest_accrual?code=key",
            CronExpression = "0 0 16 * * *",
            Timezone = "Asia/Kuala_Lumpur",
            Enabled = true,
            CatchupDays = 2,
            MaxRetries = 3,
            AppendDateParam = true,
            AuthMode = TargetAuthMode.ArAuth,
            AuthScope = "api://ar-loans-app/run:job",
            AuthClientId = "cron-agent",
            AuthClientSecret = "s3cret",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _jobs.Insert(job);
        Assert.True(job.Id > 0);

        var loaded = _jobs.GetById(job.Id)!;
        Assert.Equal("interest-accrual", loaded.Name);
        Assert.Equal(TargetAuthMode.ArAuth, loaded.AuthMode);
        Assert.Equal("s3cret", loaded.AuthClientSecret);
        Assert.Null(loaded.LastDispatchedTickUtc);

        loaded.Name = "renamed";
        loaded.Enabled = false;
        loaded.AuthMode = TargetAuthMode.None;
        _jobs.Update(loaded);

        var reloaded = _jobs.GetById(job.Id)!;
        Assert.Equal("renamed", reloaded.Name);
        Assert.False(reloaded.Enabled);
        Assert.Equal(TargetAuthMode.None, reloaded.AuthMode);

        _jobs.Delete(job.Id);
        Assert.Null(_jobs.GetById(job.Id));
    }

    [Fact]
    public void LastDispatched_is_persisted_and_updates()
    {
        var job = ValidJob();
        _jobs.Insert(job);

        var tick = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
        _jobs.SetLastDispatched(job.Id, tick);

        var loaded = _jobs.GetById(job.Id)!;
        Assert.Equal(tick, loaded.LastDispatchedTickUtc);
    }

    [Fact]
    public void Run_records_roundtrip()
    {
        var job = ValidJob();
        _jobs.Insert(job);

        var scheduled = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
        var run = new RunRecord
        {
            JobId = job.Id,
            ScheduledForUtc = scheduled,
            StartedAtUtc = DateTime.UtcNow,
            Attempt = 1,
            Status = RunStatus.Running
        };
        _runs.Insert(run);

        run.Status = RunStatus.Succeeded;
        run.HttpStatus = 200;
        run.FinishedAtUtc = DateTime.UtcNow;
        _runs.Update(run);

        var recent = _runs.GetRecentForJob(job.Id);
        Assert.Single(recent);
        Assert.Equal(RunStatus.Succeeded, recent[0].Status);
        Assert.Equal(200, recent[0].HttpStatus);

        var latest = _runs.GetLatestForJob(job.Id)!;
        Assert.Equal(run.Id, latest.Id);
    }

    [Fact]
    public void Run_history_is_ordered_most_recent_first()
    {
        var job = ValidJob();
        _jobs.Insert(job);

        for (var i = 1; i <= 3; i++)
        {
            _runs.Insert(new RunRecord
            {
                JobId = job.Id,
                ScheduledForUtc = new DateTime(2026, 10, 9 + i, 0, 0, 0, DateTimeKind.Utc),
                StartedAtUtc = DateTime.UtcNow,
                Attempt = 1,
                Status = RunStatus.Succeeded
            });
        }

        var recent = _runs.GetRecentForJob(job.Id);
        Assert.True(recent[0].ScheduledForUtc > recent[^1].ScheduledForUtc);
    }

    [Fact]
    public void Deleting_job_cascades_runs()
    {
        var job = ValidJob();
        _jobs.Insert(job);
        _runs.Insert(new RunRecord
        {
            JobId = job.Id,
            ScheduledForUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow,
            Attempt = 1,
            Status = RunStatus.Failed
        });

        _jobs.Delete(job.Id);
        Assert.Empty(_runs.GetRecentForJob(job.Id));
    }

    private static CronJob ValidJob() => new()
    {
        Name = $"job-{Guid.NewGuid():N}",
        TargetUrl = "https://target.example.com/api/job",
        CronExpression = "0 0 0 * * *",
        Timezone = "UTC",
        Enabled = true,
        CatchupDays = 1,
        MaxRetries = 3,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    public void Dispose()
    {
        if (File.Exists(_db.DbPath))
            File.Delete(_db.DbPath);
    }
}
