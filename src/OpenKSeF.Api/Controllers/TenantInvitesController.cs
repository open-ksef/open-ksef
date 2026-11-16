using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenKSeF.Api.Authorization;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class TenantInvitesController(TenantInviteService invites, ICurrentUserService currentUser, ISystemConfigService systemConfig) : ControllerBase
{
    [HttpPost("tenants/{tenantId:guid}/invites")]
    [TenantAccess(TenantPermissions.MembersManage)]
    [ProducesResponseType(typeof(CreateInviteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create(Guid tenantId, [FromBody] CreateInviteRequest request)
    {
        var (outcome, body) = await invites.CreateAsync(tenantId, currentUser.UserId, request.Email!, Enum.Parse<TenantRole>(request.Role!), BaseUrl);
        return Result(outcome, body);
    }

    [HttpGet("tenants/{tenantId:guid}/invites")]
    [TenantAccess(TenantPermissions.MembersManage)]
    [ProducesResponseType(typeof(List<TenantInviteResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid tenantId) => Ok(await invites.ListAsync(tenantId));

    [HttpPost("tenants/{tenantId:guid}/invites/{id:guid}/resend")]
    [TenantAccess(TenantPermissions.MembersManage)]
    [ProducesResponseType(typeof(InviteLinkResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Resend(Guid tenantId, Guid id)
    {
        var (outcome, body) = await invites.ResendAsync(tenantId, currentUser.UserId, id, BaseUrl);
        return Result(outcome, body);
    }

    [HttpDelete("tenants/{tenantId:guid}/invites/{id:guid}")]
    [TenantAccess(TenantPermissions.MembersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid tenantId, Guid id) =>
        Result(await invites.RevokeAsync(tenantId, currentUser.UserId, id), null);

    [HttpPost("invites/accept")]
    [ProducesResponseType(typeof(AcceptInviteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Accept([FromBody] AcceptInviteRequest request)
    {
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;
        var (outcome, body) = await invites.AcceptAsync(currentUser.UserId, email, request.Token!);
        return outcome == TenantInviteService.Outcome.NotFound
            ? NotFound(new { error = TenantInviteService.InvalidMessage })
            : Result(outcome, body);
    }

    private string? BaseUrl => systemConfig.GetValue(SystemConfigKeys.ExternalBaseUrl)?.TrimEnd('/') is { Length: > 0 } url ? url : null;

    private IActionResult Result(TenantInviteService.Outcome outcome, object? body) => outcome switch
    {
        TenantInviteService.Outcome.Done => body is null ? NoContent() : Ok(body),
        TenantInviteService.Outcome.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = TenantAccessAttribute.ForbiddenMessage }),
        TenantInviteService.Outcome.WrongEmail => StatusCode(StatusCodes.Status403Forbidden, new { error = TenantInviteService.WrongEmailMessage }),
        TenantInviteService.Outcome.AlreadyMember => Conflict(new { error = TenantInviteService.AlreadyMemberMessage }),
        TenantInviteService.Outcome.Limited => StatusCode(StatusCodes.Status429TooManyRequests, new { error = TenantInviteService.LimitMessage }),
        TenantInviteService.Outcome.PersonLimited => StatusCode(StatusCodes.Status429TooManyRequests, new { error = TenantInviteService.PersonLimitMessage }),
        TenantInviteService.Outcome.NotConfigured => Problem(statusCode: StatusCodes.Status503ServiceUnavailable, detail: $"{SystemConfigKeys.ExternalBaseUrl} (APP_EXTERNAL_BASE_URL) is not configured; invite links cannot be built."),
        _ => NotFound(),
    };
}
