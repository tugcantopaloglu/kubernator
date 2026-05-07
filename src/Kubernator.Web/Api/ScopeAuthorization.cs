using Kubernator.Web.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Kubernator.Web.Api;

public sealed class ScopeRequirement : IAuthorizationRequirement
{
    public ScopeRequirement(ApiKeyScope minimum)
    {
        Minimum = minimum;
    }

    public ApiKeyScope Minimum { get; }
}

public sealed class ScopeRequirementHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        var raw = context.User.FindFirst(ApiKeyScopes.ScopeClaimType)?.Value;
        if (!ApiKeyScopes.TryParse(raw, out var held))
        {
            return Task.CompletedTask;
        }
        if (ApiKeyScopes.Allows(held, requirement.Minimum))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
