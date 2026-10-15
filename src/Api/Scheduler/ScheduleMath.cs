using Api.Jobs;
using Cronos;
using TimeZoneConverter;

namespace Api.Scheduler;

public static class ScheduleMath
{
    public static List<DateTime> GetDueTicks(
        string cronExpression,
        string timezone,
        DateTime lastDispatchedUtc,
        DateTime nowUtc,
        int catchupDays,
        int maxTicks)
    {
        var expr = Parse(cronExpression);
        var tz = TZConvert.GetTimeZoneInfo(timezone);

        if (catchupDays < 0)
            return LatestDueTicks(expr, tz, lastDispatchedUtc, nowUtc);

        var windowStart = nowUtc.AddDays(-catchupDays);
        var searchStart = lastDispatchedUtc < windowStart ? windowStart : lastDispatchedUtc;

        var ticks = new List<DateTime>();
        var next = expr.GetNextOccurrence(AsUtc(searchStart), tz, inclusive: false);

        while (next.HasValue && next.Value <= nowUtc && ticks.Count < maxTicks)
        {
            ticks.Add(next.Value);
            next = expr.GetNextOccurrence(next.Value, tz, inclusive: false);
        }

        return ticks;
    }

    private static List<DateTime> LatestDueTicks(
        CronExpression expr,
        TimeZoneInfo tz,
        DateTime lastDispatchedUtc,
        DateTime nowUtc)
    {
        var searchStart = lastDispatchedUtc < nowUtc ? lastDispatchedUtc : nowUtc;
        var next = expr.GetNextOccurrence(AsUtc(searchStart), tz, inclusive: false);

        DateTime? latest = null;
        var guard = 0;
        while (next.HasValue && next.Value <= nowUtc && guard++ < 10_000)
        {
            latest = next.Value;
            next = expr.GetNextOccurrence(next.Value, tz, inclusive: false);
        }

        return latest is null ? new List<DateTime>() : new List<DateTime> { latest.Value };
    }

    public static int CountDroppedTicks(
        string cronExpression,
        string timezone,
        DateTime lastDispatchedUtc,
        DateTime nowUtc,
        int catchupDays,
        int maxCount = 10_000)
    {
        var expr = Parse(cronExpression);
        var tz = TZConvert.GetTimeZoneInfo(timezone);
        var windowStart = nowUtc.AddDays(-catchupDays);
        if (lastDispatchedUtc >= windowStart)
            return 0;

        var count = 0;
        var next = expr.GetNextOccurrence(AsUtc(lastDispatchedUtc), tz, inclusive: false);

        while (next.HasValue && next.Value < windowStart && count < maxCount)
        {
            count++;
            next = expr.GetNextOccurrence(next.Value, tz, inclusive: false);
        }

        return count;
    }

    public static DateTime? GetNextRun(
        string cronExpression,
        string timezone,
        DateTime nowUtc)
    {
        var expr = Parse(cronExpression);
        var tz = TZConvert.GetTimeZoneInfo(timezone);
        return expr.GetNextOccurrence(AsUtc(nowUtc), tz, inclusive: false);
    }

    private static CronExpression Parse(string expression) =>
        CronValidator.TryParse(expression, out var parsed, out _)
            ? parsed!
            : throw new FormatException($"Invalid cron expression: {expression}");

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
