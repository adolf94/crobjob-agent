using Api.Scheduler;

namespace Api.Tests;

public class ScheduleMathTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Due_tick_between_last_and_now_is_returned()
    {
        // every minute, last dispatched 10:50, now 10:00? -- use coherent window
        var last = Now.AddMinutes(-5);
        var ticks = ScheduleMath.GetDueTicks("0 * * * * *", "UTC", last, Now, catchupDays: 1, maxTicks: 500);

        Assert.Equal(5, ticks.Count);
        Assert.All(ticks, t => Assert.True(t > last && t <= Now));
    }

    [Fact]
    public void No_due_tick_when_last_is_now()
    {
        var ticks = ScheduleMath.GetDueTicks("0 * * * * *", "UTC", Now, Now, catchupDays: 1, maxTicks: 500);
        Assert.Empty(ticks);
    }

    [Fact]
    public void Ticks_older_than_catchup_window_are_clamped()
    {
        // 3 days ago, catchup window 1 day -> only last day's hours fire
        var last = Now.AddDays(-3);
        var ticks = ScheduleMath.GetDueTicks("0 0 10 * * *", "UTC", last, Now, catchupDays: 1, maxTicks: 500);

        var windowStart = Now.AddDays(-1);
        Assert.All(ticks, t => Assert.True(t > windowStart));
        Assert.Single(ticks);
    }

    [Fact]
    public void Timezone_aware_daily_job_resolves_expected_utc_tick()
    {
        // daily 02:00 Asia/Kuala_Lumpur => 18:00 UTC previous day
        var last = new DateTime(2026, 10, 8, 23, 59, 0, DateTimeKind.Utc);
        var ticks = ScheduleMath.GetDueTicks("0 0 2 * * *", "Asia/Kuala_Lumpur", last, Now, catchupDays: 30, maxTicks: 500);

        Assert.Single(ticks);
        Assert.Equal(DateTime.Parse("2026-10-09T18:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind), ticks[0]);
    }

    [Fact]
    public void Max_ticks_per_cycle_is_respected()
    {
        var last = Now.AddMinutes(-100);
        var ticks = ScheduleMath.GetDueTicks("0 * * * * *", "UTC", last, Now, catchupDays: 1, maxTicks: 10);
        Assert.Equal(10, ticks.Count);
    }

    [Fact]
    public void Run_once_catchup_returns_only_latest_missed_tick()
    {
        var last = Now.AddMinutes(-100);
        var ticks = ScheduleMath.GetDueTicks("0 * * * * *", "UTC", last, Now, catchupDays: -1, maxTicks: 500);

        Assert.Single(ticks);
        Assert.Equal(Now, ticks[0]);
    }

    [Fact]
    public void Run_once_catchup_returns_empty_when_no_missed_tick()
    {
        var last = Now;
        var ticks = ScheduleMath.GetDueTicks("0 * * * * *", "UTC", last, Now, catchupDays: -1, maxTicks: 500);
        Assert.Empty(ticks);
    }

    [Fact]
    public void CountDroppedTicks_counts_occurrences_outside_window()
    {
        // hourly ticks across 2 hours beyond a 1-day window
        var last = Now.AddDays(-3);
        var dropped = ScheduleMath.CountDroppedTicks("0 0 * * * *", "UTC", last, Now, catchupDays: 1);
        Assert.True(dropped >= 40);
    }

    [Fact]
    public void CountDroppedTicks_returns_zero_when_inside_window()
    {
        Assert.Equal(0, ScheduleMath.CountDroppedTicks("0 * * * * *", "UTC", Now.AddMinutes(-5), Now, catchupDays: 1));
    }

    [Fact]
    public void GetNextRun_returns_future_occurrence_in_utc()
    {
        var next = ScheduleMath.GetNextRun("0 0 2 * * *", "Asia/Kuala_Lumpur", Now);
        Assert.NotNull(next);
        Assert.True(next > Now);
    }

    [Fact]
    public void Form_preset_schedules_fire_on_expected_manila_ticks()
    {
        // form presets time-checked in Asia/Manila (UTC+08:00)
        var at = DateTime.Parse("2026-10-10T09:31:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        static DateTime Utc(string value) =>
            DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

        Assert.Equal(Utc("2026-10-10T10:00:00Z"), ScheduleMath.GetNextRun("0 */30 * * * *", "Asia/Manila", at));
        Assert.Equal(Utc("2026-10-10T10:00:00Z"), ScheduleMath.GetNextRun("0 0 * * * *", "Asia/Manila", at));
        // hour fields resolve in local (Manila) time: 09:31Z = 17:31 PHT -> next */3 hour is 18:00 PHT
        Assert.Equal(Utc("2026-10-10T10:00:00Z"), ScheduleMath.GetNextRun("0 0 */3 * * *", "Asia/Manila", at));
        Assert.Equal(Utc("2026-10-10T18:00:00Z"), ScheduleMath.GetNextRun("0 0 2 * * *", "Asia/Manila", at));

        // At 09:31Z it is Saturday in Manila; weekly preset fires Monday 02:00 PHT (2026-10-11 18:00Z + 8h)
        Assert.Equal(Utc("2026-10-11T18:00:00Z"), ScheduleMath.GetNextRun("0 0 2 * * MON", "Asia/Manila", at));
    }

    [Fact]
    public void Invalid_cron_throws()
    {
        Assert.Throws<FormatException>(() =>
            ScheduleMath.GetDueTicks("garbage", "UTC", Now.AddMinutes(-1), Now, 1, 10));
    }
}
