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
        if (scopes.Length == 0 || scopes.Any(s => context.User.HasClaim("scope", s)))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
