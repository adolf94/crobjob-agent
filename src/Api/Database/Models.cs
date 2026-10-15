namespace Api.Database;

public class CronJob
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string TargetUrl { get; set; } = "";
    public string CronExpression { get; set; } = "";
    public string Timezone { get; set; } = "";
    public bool Enabled { get; set; }
    public int CatchupDays { get; set; }
    public int MaxRetries { get; set; }
    public bool AppendDateParam { get; set; }
    public TargetAuthMode AuthMode { get; set; }
    public string? AuthScope { get; set; }
    public string? AuthClientId { get; set; }
    public string? AuthClientSecret { get; set; }
    public DateTime? LastDispatchedTickUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public enum TargetAuthMode
{
    None = 0,
    ArAuth = 1
}

public class RunRecord
{
    public long Id { get; set; }
    public long JobId { get; set; }
    public DateTime ScheduledForUtc { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public int Attempt { get; set; }
    public RunStatus Status { get; set; }
    public int? HttpStatus { get; set; }
    public string? Error { get; set; }
}

public enum RunStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Skipped = 3
}
