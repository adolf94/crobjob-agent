using System.Text.Json.Serialization;
using Api;
using Api.Alerts;
using Api.Auth;
using Api.Database;
using Api.Jobs;
using Api.Scheduler;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var wwwrootCandidates = new[]
{
    Path.Combine(builder.Environment.ContentRootPath, "wwwroot"),
    Path.Combine(AppContext.BaseDirectory, "wwwroot"),
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot")
};
var wwwrootDir = wwwrootCandidates.FirstOrDefault(Directory.Exists);
if (wwwrootDir is not null)
{
    var wwwrootFull = Path.GetFullPath(wwwrootDir);
    builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        WebRootPath = wwwrootFull
    });
}

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection("Database"));
builder.Services.AddOptions<SchedulerOptions>()
    .Bind(builder.Configuration.GetSection("Scheduler"));
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection("Auth"));
builder.Services.AddOptions<AlertOptions>()
    .Bind(builder.Configuration.GetSection("Alerts"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<AuthOptions>>().Value);
builder.Services.AddSingleton<IAuthorizationHandler, CronAccessHandler>();

var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
var authAudiences = authOptions.Audience.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = authOptions.Authority;
        o.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            // Atlas Rig issues tokens with an audience the API doesn't control;
            // set Auth__Audience (space-separated) to enforce one, otherwise
            // accept any aud — issuer and signature are still validated.
            ValidateAudience = authAudiences.Length > 0,
            ValidAudiences = authAudiences,
        };
    });

builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder()
        .AddRequirements(new RequireCronAccess())
        .Build();
});

builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;
    var db = new DatabaseInitializer(opts.Path);
    db.Initialize();
    return db;
});
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DatabaseOptions>>().Value);
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<SchedulerOptions>>().Value);
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<AlertOptions>>().Value);
builder.Services.AddSingleton<JobRepository>();
builder.Services.AddSingleton<RunRepository>();
builder.Services.AddSingleton<ArAuthTokenClient>();
builder.Services.AddSingleton<AlertSender>();

builder.Services.AddHttpClient("job-dispatch");
builder.Services.AddHttpClient("arauth-token");
builder.Services.AddHttpClient("alerts");

builder.Services.AddSingleton<JobDispatcher>();
builder.Services.AddHostedService<SchedulerService>();
builder.Services.AddOpenApi();
builder.Services.TryAddSingleton(TimeProvider.System);

var dbPathRaw = builder.Configuration["Database:Path"] ?? "data/cron.db";
var dbPath = Path.IsPathRooted(dbPathRaw) ? dbPathRaw : Path.Combine(builder.Environment.ContentRootPath, dbPathRaw);
builder.Services.Configure<DatabaseOptions>(o => o.Path = dbPath);

var app = builder.Build();

// Static assets (SPA shell + js/css) are public: serve them before
// authentication/authorization. Endpoint routing would otherwise select the
// fallback endpoint for asset paths and the fallback policy would 401 them
// before the static-file middleware ever runs.
ServeWeb(app);

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", (DatabaseInitializer db) =>
    Results.Ok(new { status = "ok", dbPath = db.DbPath }))
   .AllowAnonymous();

MapJobRoutes(app);
app.MapOpenApi();

if (app.Environment.IsEnvironment("Testing"))
{
    app.MapPost("/devtest/ok", () => Results.Ok(new { ok = true }));
    app.MapPost("/devtest/fail", (HttpResponse res) => { res.StatusCode = 500; return Results.Text("boom"); });
    app.MapPost("/devtest/echo", async (HttpRequest req, HttpResponse res) =>
    {
        res.StatusCode = 200;
        await res.WriteAsync(req.Headers.Authorization.ToString());
    });
}

app.Run();

static void MapJobRoutes(IEndpointRouteBuilder app)
{
    app.MapJobEndpoints();
}

static void ServeWeb(WebApplication app)
{
    if (!Directory.Exists(app.Environment.WebRootPath))
        return;

    app.UseDefaultFiles();
    app.UseStaticFiles();
    // The SPA shell must be reachable unauthenticated — the OIDC login flow
    // starts from it. The fallback policy still guards the API endpoints.
    app.MapFallbackToFile("index.html").AllowAnonymous();
}

public partial class Program { }
