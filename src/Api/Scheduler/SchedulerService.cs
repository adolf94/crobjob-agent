using Api.Database;

namespace Api.Scheduler;

public class SchedulerService(
    JobRepository jobs,
    RunRepository runs,
    JobDispatcher dispatcher,
    TimeProvider time,
    SchedulerOptions options,
    ILogger<SchedulerService> logger) : BackgroundService
{
    private readonly HashSet<long> _inFlight = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Scheduler started (poll every {PollSeconds}s)", options.PollSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduler cycle failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Scheduler stopped");
    }

    internal async Task ProcessAllAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var enabledJobs = jobs.GetAll().Where(j => j.Enabled).ToList();

        foreach (var job in enabledJobs)
        {
            if (ct.IsCancellationRequested)
                break;

            lock (_inFlight)
            {
                if (!_inFlight.Add(job.Id))
                    continue;
            }

            try
            {
                await ProcessJobAsync(job, now, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Processing job '{JobName}' (#{JobId}) failed", job.Name, job.Id);
            }
            finally
            {
                lock (_inFlight)
                {
                    _inFlight.Remove(job.Id);
                }
            }
        }
    }

    internal async Task ProcessJobAsync(CronJob job, DateTimeOffset nowOffset, CancellationToken ct)
    {
        var now = nowOffset.UtcDateTime;
        var last = job.LastDispatchedTickUtc;

        if (last is null)
        {
            var dropped = ScheduleMath.CountDroppedTicks(job.CronExpression, job.Timezone, now.AddDays(-job.CatchupDays), now, job.CatchupDays);
            if (dropped > 0)
                RecordSkipped(job, now, $"Outside catch-up window ({dropped} tick(s) dropped)");
            jobs.SetLastDispatched(job.Id, now);
            return;
        }

        var ticks = ScheduleMath.GetDueTicks(job.CronExpression, job.Timezone, last.Value, now, job.CatchupDays, options.MaxTicksPerCycle);
        if (ticks.Count == 0)
            return;

        logger.LogInformation("Job '{JobName}' (#{JobId}) has {Count} due tick(s)", job.Name, job.Id, ticks.Count);

        foreach (var tick in ticks)
        {
            if (ct.IsCancellationRequested)
                break;

            var result = await dispatcher.DispatchAsync(job, tick, ct);
            jobs.SetLastDispatched(job.Id, tick);

            if (!result.Success)
                logger.LogWarning("Tick {Tick:O} for job '{JobName}' finished unsuccessfully after {Attempts} attempt(s)",
                    tick, job.Name, result.Attempts);
        }
    }

    private void RecordSkipped(CronJob job, DateTime scheduledFor, string reason)
    {
        runs.Insert(new RunRecord
        {
            JobId = job.Id,
            ScheduledForUtc = scheduledFor,
            StartedAtUtc = time.GetUtcNow().UtcDateTime,
            FinishedAtUtc = time.GetUtcNow().UtcDateTime,
            Attempt = 0,
            Status = RunStatus.Skipped,
            Error = reason
        });
    }
}
