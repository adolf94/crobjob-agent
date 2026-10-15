using Api.Database;

namespace Api.Jobs;

public class JobDto
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
    public string AuthMode { get; set; } = "none";
    public string? AuthScope { get; set; }
    public DateTime? LastDispatchedUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? NextRunUtc { get; set; }
    public RunRecord? LastRun { get; set; }
}

public class JobInputDto
{
    public string Name { get; set; } = "";
    public string TargetUrl { get; set; } = "";
    public string CronExpression { get; set; } = "";
    public string Timezone { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int CatchupDays { get; set; } = 1;
    public int MaxRetries { get; set; } = 3;
    public bool AppendDateParam { get; set; }
    public string AuthMode { get; set; } = "none";
    public string? AuthScope { get; set; }
}

public class JobDtos
{
    public static JobDto FromJob(CronJob job, DateTime? nextRunUtc, RunRecord? lastRun)
    {
        return new JobDto
        {
            Id = job.Id,
            Name = job.Name,
            TargetUrl = MaskTargetUrl(job.TargetUrl),
            CronExpression = job.CronExpression,
            Timezone = job.Timezone,
            Enabled = job.Enabled,
            CatchupDays = job.CatchupDays,
            MaxRetries = job.MaxRetries,
            AppendDateParam = job.AppendDateParam,
            AuthMode = job.AuthMode == TargetAuthMode.ArAuth ? "arauth" : "none",
            AuthScope = job.AuthScope,
            LastDispatchedUtc = job.LastDispatchedTickUtc,
            CreatedAtUtc = job.CreatedAtUtc,
            UpdatedAtUtc = job.UpdatedAtUtc,
            NextRunUtc = nextRunUtc,
            LastRun = lastRun
        };
    }

    public static CronJob ToJob(JobInputDto input, DateTime nowUtc)
    {
        return new CronJob
        {
            Name = input.Name.Trim(),
            TargetUrl = input.TargetUrl.Trim(),
            CronExpression = input.CronExpression.Trim(),
            Timezone = input.Timezone.Trim(),
            Enabled = input.Enabled,
            CatchupDays = input.CatchupDays,
            MaxRetries = input.MaxRetries,
            AppendDateParam = input.AppendDateParam,
            AuthMode = ParseAuthMode(input.AuthMode),
            AuthScope = input.AuthMode == "arauth" ? NullIfEmpty(input.AuthScope) : null,
            LastDispatchedTickUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    public static TargetAuthMode ParseAuthMode(string? mode) =>
        string.Equals(mode, "arauth", StringComparison.OrdinalIgnoreCase) ? TargetAuthMode.ArAuth : TargetAuthMode.None;

    public static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string MaskTargetUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        var queryIndex = url.IndexOf('?');
        if (queryIndex < 0)
            return url;

        var query = url[(queryIndex + 1)..];
        var maskedEntries = query.Split('&')
            .Select(entry =>
            {
                var eq = entry.IndexOf('=');
                if (eq < 0)
                    return entry;

                var key = entry[..eq];
                var value = entry[(eq + 1)..];
                if (key.Contains("code", StringComparison.OrdinalIgnoreCase) && !value.Contains('{'))
                    return $"{key}=****";
                return entry;
            });

        return $"{url[..(queryIndex + 1)]}{string.Join('&', maskedEntries)}";
    }
}
