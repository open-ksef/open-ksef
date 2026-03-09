using Microsoft.AspNetCore.Authorization;
using OpenKSeF.Portal.Services;

namespace OpenKSeF.Portal.Authorization;

public sealed class TenantAccessHandler(ITenantResolver tenantResolver)
    : AuthorizationHandler<TenantAccessRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantAccessRequirement requirement)
    {
        var tenantId = TryResolveTenantId(context.Resource);
        if (!tenantId.HasValue)
        {
            return;
        }

        if (await tenantResolver.HasAccessToTenantAsync(tenantId.Value))
        {
            context.Succeed(requirement);
        }
    }

    private static Guid? TryResolveTenantId(object? resource)
    {
        if (resource is not HttpContext httpContext)
        {
            return null;
        }

        if (httpContext.Request.RouteValues.TryGetValue("tenantId", out var rawTenantId) &&
            rawTenantId is not null &&
            Guid.TryParse(rawTenantId.ToString(), out var routeTenantId))
        {
            return routeTenantId;
        }

        return null;
    }
}
