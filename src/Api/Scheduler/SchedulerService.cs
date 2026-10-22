using Api.Database;

namespace Api.Scheduler;

/// <summary>
/// Polls every PollSeconds, but avoids database access on idle cycles:
///   - Only one value is cached: the earliest next run time across all enabled jobs.
///   - Mutations (create/update/delete) bump an in-memory <see cref="JobRepository.Version"/>,
///     so the cache is invalidated without touching the database.
///   - When the cached time expires, a single GetAll() re-syncs everything and due ticks
///     are dispatched; each dispatched job is re-fetched (GetById) first so deletions
///     and edits made mid-cycle are honoured.
/// </summary>
public class SchedulerService(
    JobRepository jobs,
    RunRepository runs,
    JobDispatcher dispatcher,
    TimeProvider time,
    SchedulerOptions options,
    ILogger<SchedulerService> logger) : BackgroundService
{
    private DateTime _nextRunUtc = DateTime.MinValue; // cached earliest next occurrence (UTC)
    private long _seenVersion = -1;                  // -1 = cache not yet built

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
        var now = time.GetUtcNow().UtcDateTime;

        // Fast path: no mutations since the last sync and the cached earliest
        // run is still in the future -> zero database access.
        var version = jobs.Version;
        if (_seenVersion == version && now < _nextRunUtc)
            return;

        _seenVersion = version;
        var next = DateTime.MaxValue;

        foreach (var job in jobs.GetAll().Where(j => j.Enabled).ToList())
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                var cursor = await ProcessJobAsync(job, now, ct) ?? now;
                var jobNext = ScheduleMath.GetNextRun(job.CronExpression, job.Timezone, cursor) ?? DateTime.MaxValue;
                if (jobNext < next)
                    next = jobNext;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Processing job '{JobName}' (#{JobId}) failed", job.Name, job.Id);
                next = Min(next, now.AddSeconds(options.PollSeconds)); // retry next cycle
            }
        }

        _nextRunUtc = next;
    }

    /// <summary>Dispatches all due ticks for one job. Returns the new last-dispatched cursor (null = nothing dispatched).</summary>
    internal async Task<DateTime?> ProcessJobAsync(CronJob job, DateTime now, CancellationToken ct)
    {
        var last = job.LastDispatchedTickUtc;

        if (last is null)
        {
            var dropped = ScheduleMath.CountDroppedTicks(job.CronExpression, job.Timezone, now.AddDays(-job.CatchupDays), now, job.CatchupDays);
            if (dropped > 0)
                RecordSkipped(job, now, $"Outside catch-up window ({dropped} tick(s) dropped)");
            jobs.SetLastDispatched(job.Id, now);
            return now;
        }

        var ticks = ScheduleMath.GetDueTicks(job.CronExpression, job.Timezone, last.Value, now, job.CatchupDays, options.MaxTicksPerCycle);
        if (ticks.Count == 0)
            return null;

        logger.LogInformation("Job '{JobName}' (#{JobId}) has {Count} due tick(s)", job.Name, job.Id, ticks.Count);

        foreach (var tick in ticks)
        {
            if (ct.IsCancellationRequested)
                break;

            // Narrow the delete-mid-dispatch window: inserting a run for a job that
            // no longer exists would violate the runs -> jobs foreign key.
            if (jobs.GetById(job.Id) is null)
            {
                logger.LogInformation("Job '{JobName}' (#{JobId}) was deleted mid-cycle; skipping {Remaining} remaining tick(s)",
                    job.Name, job.Id, ticks.Count - ticks.IndexOf(tick) - 1);
                return ticks[Math.Max(0, ticks.IndexOf(tick) - 1)];
            }

            var result = await dispatcher.DispatchAsync(job, tick, ct);
            jobs.SetLastDispatched(job.Id, tick);

            if (!result.Success)
                logger.LogWarning("Tick {Tick:O} for job '{JobName}' finished unsuccessfully after {Attempts} attempt(s)",
                    tick, job.Name, result.Attempts);
        }

        return ticks[^1];
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

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
