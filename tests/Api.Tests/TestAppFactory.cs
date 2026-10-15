using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Api.Tests;

public class TargetHandler : HttpMessageHandler
{
    public ConcurrentDictionary<string, int> Hits { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri;
        var path = uri?.AbsolutePath ?? "";
        var key = $"{uri?.PathAndQuery ?? ""}|{request.Headers.Authorization?.ToString() ?? ""}";

        Hits.AddOrUpdate(key, 1, (_, v) => v + 1);

        var status = path switch
        {
            "/devtest/ok" => HttpStatusCode.OK,
            "/devtest/fail" => HttpStatusCode.InternalServerError,
            "/devtest/echo" => HttpStatusCode.OK,
            "/devtest/token" => HttpStatusCode.OK,
            _ => HttpStatusCode.NotFound
        };
        var body = path switch
        {
            "/devtest/ok" => "{\"ok\":true}",
            "/devtest/fail" => "boom",
            "/devtest/echo" => key,
            "/devtest/token" => "{\"access_token\":\"token-abc\",\"expires_in\":300}",
            _ => "no route"
        };

        var resp = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        return Task.FromResult(resp);
    }
}

public class TestAppFactory : WebApplicationFactory<Program>
{
    public TargetHandler Target { get; } = new();
    public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"cron-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Path"] = DbPath,
                ["Auth:Disabled"] = "true",
                ["Auth:TokenEndpoint"] = "http://target.internal/devtest/token",
                ["Auth:ClientId"] = "cron-agent",
                ["Auth:ClientSecret"] = "s3cret",
                ["Scheduler:PollSeconds"] = "3600",
                ["Scheduler:RetryBaseDelayMs"] = "20",
                ["Scheduler:RetryMaxDelayMs"] = "50",
                ["Scheduler:HttpTimeoutSeconds"] = "5"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.Configure<HttpClientFactoryOptions>("job-dispatch", o =>
                o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = Target));
            services.Configure<HttpClientFactoryOptions>("arauth-token", o =>
                o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = Target));
            services.Configure<HttpClientFactoryOptions>("alerts", o =>
                o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = Target));
        });
    }

    public TestAppFactory WithServerClient()
    {
        _ = CreateClient();
        return this;
    }
}
