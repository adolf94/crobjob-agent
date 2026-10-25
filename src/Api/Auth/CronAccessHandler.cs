using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Api.Auth;

public class RequireCronAccess : IAuthorizationRequirement { }

public class CronAccessHandler(AuthOptions options) : AuthorizationHandler<RequireCronAccess>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RequireCronAccess requirement)
    {
        if (options.Disabled)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (context.User.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        var scopes = options.RequiredScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // The IdP issues scope as a single space-separated claim value, so
        // each granted scope must be split out before comparing.
        var granted = context.User.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (scopes.Length == 0 || scopes.Any(s => granted.Contains(s, StringComparer.Ordinal)))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
