using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api/tenants/{tenantId:guid}/seller-profile")]
[Authorize]
public class SellerProfileController(
    ApplicationDbContext db,
    ICurrentUserService currentUser,
    ISellerProfileService sellerProfiles,
    IKSeFIssuingEnvironment ksefEnvironment) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(SellerProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct)
    {
        if (!await db.OwnsTenantAsync(tenantId, currentUser.UserId, ct))
            return NotFound();

        var view = await sellerProfiles.GetAsync(tenantId, ct);
        return view is null ? NotFound() : Ok(ToResponse(view));
    }

    [HttpPut]
    [ProducesResponseType(typeof(SellerProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Put(Guid tenantId, SellerProfileRequest request, CancellationToken ct)
    {
        if (!await db.OwnsTenantAsync(tenantId, currentUser.UserId, ct))
            return NotFound();

        try
        {
            var (view, errors) = await sellerProfiles.SaveAsync(tenantId, new SellerProfileInput(
                request.LegalName, request.AddressLine1, request.AddressLine2, request.BankAccount, request.CashAccounting,
                request.NumberingPattern, request.NextNumber, request.NextNumberPeriod,
                request.BankSwift, request.BankName, request.ForeignBankAccount, request.ForeignBankSwift, request.ForeignBankName), ct);

            if (errors.Count > 0)
                return BadRequest(ToEnvelope(errors));

            return view is null ? NotFound() : Ok(ToResponse(view));
        }
        catch (InvoiceValidationException e)
        {
            return Conflict(ToEnvelope(e.Errors));
        }
        catch (InvoiceStateException e)
        {
            return Conflict(new { message = e.Message });
        }
    }

    private static ValidationEnvelope ToEnvelope(IReadOnlyList<InvoiceCheck> errors) =>
        new(errors.Select(e => new ValidationIssue(e.Code, e.Field, e.Message)).ToList(), []);

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
        ksefEnvironment.Current,
        view.Profile.BankSwift,
        view.Profile.BankName,
        view.Profile.ForeignBankAccount,
        view.Profile.ForeignBankSwift,
        view.Profile.ForeignBankName);
}
