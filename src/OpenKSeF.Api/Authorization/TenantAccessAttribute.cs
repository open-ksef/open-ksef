using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Authorization;

/// <summary>ADR-006: gates a {tenantId} action on the caller's membership; non-member 404, missing permission 403.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TenantAccessAttribute(string permission) : Attribute, IAsyncActionFilter, IOrderedFilter
{
    public const string ForbiddenMessage = "Brak uprawnień do tej operacji w tej firmie.";

    public string Permission { get; } = permission;

    // Before ModelStateInvalidFilter, so a non-member never learns more than 404 from a malformed body.
    public int Order => int.MinValue;

    public static TenantMember Member(HttpContext http) => (TenantMember)http.Items[typeof(TenantMember)]!;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!Guid.TryParse(context.RouteData.Values["tenantId"]?.ToString(), out var tenantId))
        {
            context.Result = new NotFoundResult();
            return;
        }

        var userId = http.RequestServices.GetRequiredService<ICurrentUserService>().UserId;
        var member = await http.RequestServices.GetRequiredService<ApplicationDbContext>().TenantMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId, http.RequestAborted);

        if (member is null)
        {
            context.Result = new NotFoundResult();
            return;
        }

        if (!TenantPermissions.For(member.Role).Contains(Permission))
        {
            context.Result = new ObjectResult(new { error = ForbiddenMessage }) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        http.Items[typeof(TenantMember)] = member;
        await next();
    }
}
