using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using OpenKSeF.Portal.Authorization;
using OpenKSeF.Portal.Services;

namespace OpenKSeF.Portal.Tests;

public class TenantAccessHandlerTests
{
    [Fact]
    public async Task HandleAsync_Succeeds_WhenUserOwnsTenantFromRoute()
    {
        var requirement = new TenantAccessRequirement();
        var resolver = new FakeTenantResolver(hasAccess: true);
        var handler = new TenantAccessHandler(resolver);

        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-a")], "test"));
        var context = new DefaultHttpContext { User = user };
        context.Request.RouteValues["tenantId"] = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var authorizationContext = new AuthorizationHandlerContext([requirement], user, context);

        await handler.HandleAsync(authorizationContext);

        Assert.True(authorizationContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_Fails_WhenUserDoesNotOwnTenantFromRoute()
    {
        var requirement = new TenantAccessRequirement();
        var resolver = new FakeTenantResolver(hasAccess: false);
        var handler = new TenantAccessHandler(resolver);

        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-a")], "test"));
        var context = new DefaultHttpContext { User = user };
        context.Request.RouteValues["tenantId"] = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var authorizationContext = new AuthorizationHandlerContext([requirement], user, context);

        await handler.HandleAsync(authorizationContext);

        Assert.False(authorizationContext.HasSucceeded);
    }

    private sealed class FakeTenantResolver(bool hasAccess) : ITenantResolver
    {
        public string? GetCurrentUserId() => "user-a";

        public Task<List<Guid>> GetUserTenantIdsAsync() => Task.FromResult(new List<Guid>());

        public Task<Guid?> GetCurrentTenantIdAsync() => Task.FromResult<Guid?>(null);

        public Task<bool> HasAccessToTenantAsync(Guid tenantId) => Task.FromResult(hasAccess);
    }
}
