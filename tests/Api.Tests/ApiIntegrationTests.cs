using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Database;
using Api.Jobs;

namespace Api.Tests;

public sealed class ApiIntegrationTests : IClassFixture<TestAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly TestAppFactory _factory;

    public ApiIntegrationTests(TestAppFactory factory)
    {
        _factory = factory.WithServerClient();
    }

    private HttpClient Client => _factory.CreateClient();

    private static JobInputDto ValidJob(string? url = null, string? cron = null) => new()
    {
        Name = $"daily-{Guid.NewGuid():N}",
        TargetUrl = url ?? "http://target.internal/devtest/ok",
        CronExpression = cron ?? "0 0 2 * * *",
        Timezone = "Asia/Kuala_Lumpur",
        Enabled = true,
        CatchupDays = 1,
        MaxRetries = 3,
        AuthMode = "none"
    };

    [Fact]
    public async Task Healthz_is_public_and_reports_db_path()
    {
        var resp = await Client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal("ok", body?["status"].ToString());
    }

    [Fact]
    public async Task Create_job_returns_201_with_masked_fields()
    {
        var input = ValidJob();
        input.AuthMode = "arauth";
        input.AuthScope = "api://app/run:job";

        var resp = await Client.PostAsJsonAsync("/api/jobs", input);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<JobDto>(JsonOpts);
        Assert.NotNull(dto);
        Assert.Equal("arauth", dto!.AuthMode);
        Assert.Equal("api://app/run:job", dto.AuthScope);
    }

    [Fact]
    public async Task Create_job_with_invalid_cron_returns_400()
    {
        var input = ValidJob(cron: "definitely not cron");
        var resp = await Client.PostAsJsonAsync("/api/jobs", input);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_job_with_arauth_and_missing_scope_returns_400()
    {
        var input = ValidJob();
        input.AuthMode = "arauth";
        var resp = await Client.PostAsJsonAsync("/api/jobs", input);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Full_job_lifecycle()
    {
        var input = ValidJob();
        var created = await (await Client.PostAsJsonAsync("/api/jobs", input)).Content.ReadFromJsonAsync<JobDto>(JsonOpts);
        Assert.NotNull(created);

        var list = await (await Client.GetAsync("/api/jobs")).Content.ReadFromJsonAsync<List<JobDto>>(JsonOpts);
        Assert.Contains(list!, j => j.Id == created!.Id);

        input.Name = "renamed-job";
        var updated = await (await Client.PutAsJsonAsync($"/api/jobs/{created!.Id}", input)).Content.ReadFromJsonAsync<JobDto>(JsonOpts);
        Assert.Equal("renamed-job", updated!.Name);
        Assert.Equal("none", updated.AuthMode);

        var fetched = await (await Client.GetAsync($"/api/jobs/{created.Id}")).Content.ReadFromJsonAsync<JobDto>(JsonOpts);
        Assert.Equal("renamed-job", fetched!.Name);

        var del = await Client.DeleteAsync($"/api/jobs/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var missing = await Client.GetAsync($"/api/jobs/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Arauth_job_update_without_scope_clears_auth_fields()
    {
        var input = ValidJob();
        input.AuthMode = "arauth";
        input.AuthScope = "api://app/run:job";

        var created = await (await Client.PostAsJsonAsync("/api/jobs", input)).Content.ReadFromJsonAsync<JobDto>(JsonOpts);
        Assert.Equal("arauth", created!.AuthMode);

        input.AuthScope = null;
        input.AuthMode = "none";
        input.Name = $"arauth-renamed-{Guid.NewGuid():N}";
        var updated = await (await Client.PutAsJsonAsync($"/api/jobs/{created.Id}", input)).Content.ReadFromJsonAsync<JobDto>(JsonOpts);
        Assert.Equal("none", updated!.AuthMode);
        Assert.StartsWith("arauth-renamed-", updated.Name);
        Assert.Null(updated.AuthScope);
    }

    [Fact]
    public async Task Run_now_against_ok_target_succeeds_and_records_history()
    {
        var input = ValidJob(url: "http://target.internal/devtest/ok", cron: "0 0 3 * * *");
        var created = await (await Client.PostAsJsonAsync("/api/jobs", input)).Content.ReadFromJsonAsync<JobDto>(JsonOpts);

        var resp = await Client.PostAsync($"/api/jobs/{created!.Id}/run", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal("True", result!["success"].ToString());
        Assert.Equal(1, _factory.Target.Hits.Where(kv => kv.Key.StartsWith("/devtest/ok")).Sum(kv => kv.Value));

        var runs = await (await Client.GetAsync($"/api/jobs/{created.Id}/runs")).Content.ReadFromJsonAsync<List<RunRecord>>(JsonOpts);
        Assert.Single(runs!);
        Assert.Equal(RunStatus.Succeeded.ToString(), Enum.GetName(typeof(RunStatus), runs![0].Status));
    }

    [Fact]
    public async Task Run_now_against_failing_target_retries_and_records_failures()
    {
        var input = ValidJob(url: "http://target.internal/devtest/fail", cron: "0 0 3 * * *");
        input.MaxRetries = 2;
        var created = await (await Client.PostAsJsonAsync("/api/jobs", input)).Content.ReadFromJsonAsync<JobDto>(JsonOpts);

        var resp = await Client.PostAsync($"/api/jobs/{created!.Id}/run", null);
        var result = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        Assert.Equal("False", result!["success"].ToString());
        Assert.Equal("2", result["attempts"].ToString());
        Assert.True(_factory.Target.Hits.Where(h => h.Key.StartsWith("/devtest/fail")).Sum(h => h.Value) >= 2);
    }

    [Fact]
    public async Task Run_now_with_arauth_mode_uses_bearer_token()
    {
        var input = ValidJob(url: "http://target.internal/devtest/echo", cron: "0 0 3 * * *");
        input.AuthMode = "arauth";
        input.AuthScope = "api://app/run:job";
        var created = await (await Client.PostAsJsonAsync("/api/jobs", input)).Content.ReadFromJsonAsync<JobDto>(JsonOpts);

        var resp = await Client.PostAsync($"/api/jobs/{created!.Id}/run", null);
        var result = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal("True", result!["success"].ToString());
        Assert.Contains(_factory.Target.Hits.Keys, k => k.StartsWith("/devtest/echo") && k.Contains("Bearer token-abc"));
    }

    [Fact]
    public async Task Runs_endpoint_returns_404_for_unknown_job()
    {
        var resp = await Client.GetAsync("/api/jobs/999999/runs");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
