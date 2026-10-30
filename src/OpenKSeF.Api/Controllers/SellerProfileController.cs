using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api/tenants/{tenantId:guid}/seller-profile")]
[Authorize]
public class SellerProfileController(
    ApplicationDbContext db,
    ICurrentUserService currentUser,
    ISellerProfileService sellerProfiles,
    ISystemConfigService systemConfig) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(SellerProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct)
    {
        if (!await OwnsTenantAsync(tenantId, ct))
            return NotFound();

        var view = await sellerProfiles.GetAsync(tenantId, ct);
        return view is null ? NotFound() : Ok(ToResponse(view));
    }

    [HttpPut]
    [ProducesResponseType(typeof(SellerProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Put(Guid tenantId, SellerProfileRequest request, CancellationToken ct)
    {
        if (!await OwnsTenantAsync(tenantId, ct))
            return NotFound();

        var (view, errors) = await sellerProfiles.SaveAsync(tenantId, new SellerProfileInput(
            request.LegalName, request.AddressLine1, request.AddressLine2, request.BankAccount, request.CashAccounting,
            request.NumberingPattern, request.NextNumber, request.NextNumberPeriod), ct);

        if (errors.Count > 0)
            return BadRequest(new ValidationEnvelope(errors.Select(e => new ValidationIssue(e.Code, e.Field, e.Message)).ToList(), []));

        return view is null ? NotFound() : Ok(ToResponse(view));
    }

    private Task<bool> OwnsTenantAsync(Guid tenantId, CancellationToken ct) =>
        db.Tenants.AnyAsync(t => t.Id == tenantId && t.UserId == currentUser.UserId, ct);

    private SellerProfileResponse ToResponse(SellerProfileView view) => new(
        view.Profile.LegalName,
        view.Profile.AddressLine1,
        view.Profile.AddressLine2,
        view.Profile.BankAccount,
        view.Profile.CashAccounting,
        view.Profile.NumberingPattern,
        view.NextNumber,
        view.NextNumberPeriod,
        view.Nip,
        view.Profile.IsComplete,
        view.NextNumberPreview,
        DependencyInjection.NormalizeKSeFEnvironmentKey(
            systemConfig.GetValue(SystemConfigKeys.KSeFEnvironment) ?? systemConfig.GetValue(SystemConfigKeys.KSeFBaseUrl)));
}
