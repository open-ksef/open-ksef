using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OpenKSeF.Api.Authorization;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Tests.Authorization;

public class TenantAccessAttributeTests
{
    private const string UserId = "user-1";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public void EveryActionWithTenantIdRoute_HasTenantAccess()
    {
        var actions = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(controller => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(action => action.GetCustomAttributes<HttpMethodAttribute>()
                    .Any(http => $"{controller.GetCustomAttribute<RouteAttribute>()?.Template}/{http.Template}".Contains("{tenantId", StringComparison.Ordinal)))
                .Select(action => (Name: $"{controller.Name}.{action.Name}", Access: action.GetCustomAttribute<TenantAccessAttribute>())))
            .ToList();

        Assert.True(actions.Count >= 28, $"Only {actions.Count} {{tenantId}} actions found");
        Assert.Empty(actions.Where(a => a.Access is null).Select(a => a.Name));
        Assert.Empty(actions.Where(a => a.Access is not null && !TenantPermissions.For(TenantRole.Owner).Contains(a.Access.Permission)).Select(a => a.Name));
    }

    [Fact]
    public async Task NonMember_GetsNotFound_AndActionDoesNotRun()
    {
        _db.TenantMembers.Add(new TenantMember { TenantId = _tenantId, UserId = "someone-else", Role = TenantRole.Owner });
        await _db.SaveChangesAsync();

        var (context, ran) = await RunAsync(TenantPermissions.InvoicesRead);

        Assert.IsType<NotFoundResult>(context.Result);
        Assert.False(ran);
    }

    [Fact]
    public async Task MemberWithoutPermission_GetsForbiddenWithPolishError()
    {
        _db.TenantMembers.Add(new TenantMember { TenantId = _tenantId, UserId = UserId, Role = TenantRole.Employee });
        await _db.SaveChangesAsync();

        var (context, ran) = await RunAsync(TenantPermissions.CredentialManage);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal(TenantAccessAttribute.ForbiddenMessage, result.Value!.GetType().GetProperty("error")!.GetValue(result.Value));
        Assert.False(ran);
    }

    [Fact]
    public async Task MemberWithPermission_RunsAction_WithMemberInItems()
    {
        _db.TenantMembers.Add(new TenantMember { TenantId = _tenantId, UserId = UserId, Role = TenantRole.Accountant });
        await _db.SaveChangesAsync();

        var (context, ran) = await RunAsync(TenantPermissions.IssuingSubmit);

        Assert.Null(context.Result);
        Assert.True(ran);
        Assert.Equal(TenantRole.Accountant, TenantAccessAttribute.Member(context.HttpContext).Role);
    }

    private async Task<(ActionExecutingContext Context, bool Ran)> RunAsync(string permission)
    {
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(UserId);
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(_db).AddSingleton(currentUser).BuildServiceProvider()
        };
        var routeData = new RouteData();
        routeData.Values["tenantId"] = _tenantId.ToString();
        var context = new ActionExecutingContext(
            new ActionContext(http, routeData, new ActionDescriptor()), [], new Dictionary<string, object?>(), controller: null!);

        var ran = false;
        await new TenantAccessAttribute(permission).OnActionExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(context, [], controller: null!));
        });
        return (context, ran);
    }
}
