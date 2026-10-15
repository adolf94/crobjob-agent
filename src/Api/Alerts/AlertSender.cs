using System.Text;
using System.Text.Json;

namespace Api.Alerts;

public class AlertSender(IHttpClientFactory httpClientFactory, ILogger<AlertSender> logger)
{
    public record AlertPayload(string JobName, long JobId, DateTime ScheduledForUtc, int Attempts, int? HttpStatus, string? Error);

    public async Task SendJobFailedAlertAsync(string? webhookUrl, AlertPayload payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return;

        try
        {
            var client = httpClientFactory.CreateClient("alerts");
            client.Timeout = TimeSpan.FromSeconds(10);
            using var req = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        text = $"cron-agent: job '{payload.JobName}' (#{payload.JobId}) FAILED after {payload.Attempts} attempt(s). " +
                               $"status={payload.HttpStatus?.ToString() ?? "no-response"} error={payload.Error} scheduledFor={payload.ScheduledForUtc:O}"
                    }),
                    Encoding.UTF8,
                    "application/json")
            };
            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                logger.LogWarning("Alert webhook returned {Status}", (int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send alert webhook");
        }
    }
}
