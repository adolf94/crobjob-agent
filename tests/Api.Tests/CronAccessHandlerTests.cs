using System.Security.Claims;
using Api;
using Api.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Api.Tests;

public class CronAccessHandlerTests
{
    private static AuthorizationHandlerContext Context(params Claim[] claims)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        return new AuthorizationHandlerContext([new RequireCronAccess()], user, null);
    }

    private static async Task<bool> EvaluateAsync(AuthOptions options, params Claim[] claims)
    {
        var ctx = Context(claims);
        await new CronAccessHandler(options).HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    [Fact]
    public async Task ScopeClaimIsSpaceSeparated_MatchesRequiredScope()
    {
        // IdP issues ONE scope claim with the whole space-separated value —
        // regression test for the HasClaim exact-match 403.
        var options = new AuthOptions { Disabled = false, RequiredScopes = @"api:\\cron-agent\\user" };
        var claims = new[]
        {
            new Claim("scope", @"openid profile email api:\\cron-agent\\user"),
            new Claim("sub", "u1"),
        };
        Assert.True(await EvaluateAsync(options, claims));
    }

    [Fact]
    public async Task MultipleScopeClaims_AreSplitToo()
    {
        var options = new AuthOptions { Disabled = false, RequiredScopes = "b a" };
        var claims = new[] { new Claim("scope", "x y"), new Claim("scope", "b") };
        Assert.True(await EvaluateAsync(options, claims));
    }

    [Fact]
    public async Task RequiredScopeNotGranted_Fails()
    {
        var options = new AuthOptions { Disabled = false, RequiredScopes = @"api:\\other\\x" };
        var claims = new[] { new Claim("scope", @"openid profile email api:\\cron-agent\\user") };
        Assert.False(await EvaluateAsync(options, claims));
    }

    [Fact]
    public async Task EmptyRequiredScopes_Succeeds()
    {
        var options = new AuthOptions { Disabled = false, RequiredScopes = "" };
        var claims = new[] { new Claim("scope", "anything") };
        Assert.True(await EvaluateAsync(options, claims));
    }

    [Fact]
    public async Task Disabled_SucceedsRegardlessOfScope()
    {
        var options = new AuthOptions { Disabled = true, RequiredScopes = "PLACEHOLDER_SCOPE" };
        Assert.True(await EvaluateAsync(options));
    }
}
