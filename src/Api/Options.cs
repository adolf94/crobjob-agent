namespace Api;

public class DatabaseOptions
{
    public string Path { get; set; } = "data/cron.db";
}

public class SchedulerOptions
{
    public int PollSeconds { get; set; } = 15;
    public int HttpTimeoutSeconds { get; set; } = 60;
    public int RetryBaseDelayMs { get; set; } = 5000;
    public int RetryMaxDelayMs { get; set; } = 60_000;
    public int MaxTicksPerCycle { get; set; } = 500;
}

public class AuthOptions
{
    public bool Disabled { get; set; }
    public string Authority { get; set; } = "https://auth.adolfrey.com/api";
    public string RequiredScopes { get; set; } = "";
    /// <summary>Space-separated list of accepted token audiences. Empty = skip audience validation.</summary>
    public string Audience { get; set; } = "";
    public string? TokenEndpoint { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

public class AlertOptions
{
    public string? WebhookUrl { get; set; }
}
