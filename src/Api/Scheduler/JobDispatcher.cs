using Api.Alerts;
using Api.Database;
using Api.Jobs;
using TimeZoneConverter;

namespace Api.Scheduler;

public class DispatchResult
{
    public bool Success { get; init; }
    public int Attempts { get; init; }
    public int? HttpStatus { get; init; }
    public string? Error { get; init; }
}

public class JobDispatcher
{
    public const int MaxDateParamLimit = 200;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RunRepository _runs;
    private readonly ArAuthTokenClient _tokens;
    private readonly AlertSender _alerts;
    private readonly TimeProvider _time;
    private readonly SchedulerOptions _options;
    private readonly AuthOptions _auth;
    private readonly string? _alertWebhookUrl;
    private readonly string _tokenEndpoint;
    private readonly ILogger<JobDispatcher> _logger;

    public JobDispatcher(
        IHttpClientFactory httpClientFactory,
        RunRepository runs,
        ArAuthTokenClient tokens,
        AlertSender alerts,
        TimeProvider time,
        SchedulerOptions options,
        AlertOptions alertOptions,
        AuthOptions authOptions,
        ILogger<JobDispatcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _runs = runs;
        _tokens = tokens;
        _alerts = alerts;
        _time = time;
        _options = options;
        _auth = authOptions;
        _alertWebhookUrl = alertOptions.WebhookUrl;
        _tokenEndpoint = authOptions.TokenEndpoint ?? ArAuthTokenClientDefaults.TokenEndpoint;
        _logger = logger;
    }

    public string BuildTargetUrl(CronJob job, DateTime tickUtc)
    {
        if (!job.AppendDateParam)
            return job.TargetUrl;

        var tz = TZConvert.GetTimeZoneInfo(job.Timezone);
        var localDate = TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(tickUtc, DateTimeKind.Utc), tz);
        var separator = job.TargetUrl.Contains('?') ? '&' : '?';
        return $"{job.TargetUrl}{separator}date={localDate:yyyy-MM-dd}";
    }

    public async Task<DispatchResult> DispatchAsync(CronJob job, DateTime scheduledForUtc, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var url = BuildTargetUrl(job, scheduledForUtc);
        string? bearer = null;

        if (job.AuthMode == TargetAuthMode.ArAuth)
        {
            bearer = await GetBearerTokenAsync(job, ct);
            if (bearer is null)
            {
                return await RecordFinalAsync(job, scheduledForUtc, 0, null,
                    "Failed to obtain Atlas Rig access token (check scope/client credentials)", now, ct);
            }
        }

        var client = _httpClientFactory.CreateClient("job-dispatch");
        client.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);

        for (var attempt = 1; attempt <= job.MaxRetries; attempt++)
        {
            var run = new RunRecord
            {
                JobId = job.Id,
                ScheduledForUtc = scheduledForUtc,
                StartedAtUtc = now.UtcDateTime,
                Attempt = attempt,
                Status = RunStatus.Running
            };
            _runs.Insert(run);

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                if (bearer is not null)
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);

                using var resp = await client.SendAsync(req, ct);
                var finished = _time.GetUtcNow();

                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300)
                {
                    run.FinishedAtUtc = finished.UtcDateTime;
                    run.Status = RunStatus.Succeeded;
                    run.HttpStatus = (int)resp.StatusCode;
                    _runs.Update(run);
                    return new DispatchResult { Success = true, Attempts = attempt, HttpStatus = run.HttpStatus };
                }

                run.FinishedAtUtc = finished.UtcDateTime;
                run.HttpStatus = (int)resp.StatusCode;
                run.Error = $"Target returned {(int)resp.StatusCode}";
                run.Status = RunStatus.Failed;
                _runs.Update(run);

                if (attempt < job.MaxRetries)
                    await DelayBackoffAsync(attempt, ct);
                else
                    return await RecordFinalAsync(job, scheduledForUtc, attempt, run.HttpStatus, run.Error, now, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                var finished = _time.GetUtcNow();
                run.FinishedAtUtc = finished.UtcDateTime;
                run.Error = ex.InnerException?.Message ?? ex.Message;
                run.Status = RunStatus.Failed;
                _runs.Update(run);

                if (attempt < job.MaxRetries)
                    await DelayBackoffAsync(attempt, ct);
                else
                    return await RecordFinalAsync(job, scheduledForUtc, attempt, null, run.Error, now, ct);
            }
        }

        return new DispatchResult { Success = false, Attempts = job.MaxRetries, Error = "unreachable" };
    }

    private async Task<string?> GetBearerTokenAsync(CronJob job, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_auth.ClientId) || string.IsNullOrWhiteSpace(_auth.ClientSecret) ||
            string.IsNullOrWhiteSpace(job.AuthScope))
            return null;

        return await _tokens.GetTokenAsync(
            new ArAuthTokenClient.AuthCredentials(_tokenEndpoint, _auth.ClientId!, _auth.ClientSecret!, job.AuthScope),
            ct);
    }

    private async Task<DispatchResult> RecordFinalAsync(
        CronJob job, DateTime scheduledForUtc, int attempts, int? httpStatus, string? error, DateTimeOffset started, CancellationToken ct)
    {
        if (attempts == 0)
        {
            _runs.Insert(new RunRecord
            {
                JobId = job.Id,
                ScheduledForUtc = scheduledForUtc,
                StartedAtUtc = started.UtcDateTime,
                FinishedAtUtc = _time.GetUtcNow().UtcDateTime,
                Attempt = 0,
                Status = RunStatus.Failed,
                HttpStatus = httpStatus,
                Error = error
            });
        }

        _logger.LogError("Job '{JobName}' (#{JobId}) failed after {Attempts} attempt(s): {Error}",
            job.Name, job.Id, attempts, error);

        await _alerts.SendJobFailedAlertAsync(_alertWebhookUrl,
            new AlertSender.AlertPayload(job.Name, job.Id, scheduledForUtc, attempts, httpStatus, error), ct);

        return new DispatchResult { Success = false, Attempts = attempts, HttpStatus = httpStatus, Error = error };
    }

    private async Task DelayBackoffAsync(int failedAttempt, CancellationToken ct)
    {
        var delayMs = Math.Min(
            _options.RetryBaseDelayMs * Math.Pow(2, failedAttempt - 1),
            _options.RetryMaxDelayMs);
        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct);
    }
}

public static class ArAuthTokenClientDefaults
{
    public const string TokenEndpoint = "https://auth.adolfrey.com/api/token";
}
