using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Api.Scheduler;

public class ArAuthTokenClient(IHttpClientFactory httpClientFactory, ILogger<ArAuthTokenClient> logger)
{
    private class CachedToken
    {
        public string AccessToken { get; init; } = "";
        public DateTimeOffset ExpiresAt { get; init; }
    }

    private readonly ConcurrentDictionary<string, CachedToken> _cache = new();

    public record AuthCredentials(string TokenEndpoint, string ClientId, string ClientSecret, string Scope);

    public async Task<string?> GetTokenAsync(AuthCredentials credentials, CancellationToken ct)
    {
        var cacheKey = $"{credentials.ClientId}|{credentials.Scope}";
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
            return cached.AccessToken;

        var client = httpClientFactory.CreateClient("arauth-token");
        client.Timeout = TimeSpan.FromSeconds(15);

        using var req = new HttpRequestMessage(HttpMethod.Post, credentials.TokenEndpoint);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = credentials.ClientId,
            ["client_secret"] = credentials.ClientSecret,
            ["scope"] = credentials.Scope
        });

        try
        {
            using var resp = await client.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Atlas Rig token request for {ClientId} failed: {Status} {Body}",
                    credentials.ClientId, (int)resp.StatusCode, body);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var accessToken = doc.RootElement.TryGetProperty("access_token", out var tokenEl)
                ? tokenEl.GetString()
                : null;
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(
                doc.RootElement.TryGetProperty("expires_in", out var expEl) && expEl.TryGetInt32(out var exp)
                    ? exp
                    : 300);

            if (accessToken is null)
                return null;

            _cache[cacheKey] = new CachedToken { AccessToken = accessToken, ExpiresAt = expiresAt };
            return accessToken;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Atlas Rig token request for {ClientId} failed", credentials.ClientId);
            return null;
        }
    }
}
