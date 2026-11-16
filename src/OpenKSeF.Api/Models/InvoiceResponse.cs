using System.Linq.Expressions;
using System.Text.Json.Serialization;
using KSeF.Client.Core.Interfaces.Services;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Api.Models;

public record InvoiceResponse(
    Guid Id,
    [property: JsonPropertyName("ksefInvoiceNumber")] string KSeFInvoiceNumber,
    [property: JsonPropertyName("ksefReferenceNumber")] string KSeFReferenceNumber,
    string? InvoiceNumber,
    string VendorName,
    string VendorNip,
    string? BuyerName,
    string? BuyerNip,
    decimal AmountNet,
    decimal AmountVat,
    decimal AmountGross,
    string Currency,
    DateTime IssueDate,
    DateTime? AcquisitionDate,
    string? InvoiceType,
    DateTime FirstSeenAt,
    bool IsPaid,
    DateTime? PaidAt,
    string? VendorBankAccount = null,
    IReadOnlyList<InvoiceLineResponse>? Lines = null,
    string? PaidByName = null,
    DateOnly? DueDate = null,
    string? Direction = null,
    string? BuyerIdType = null,
    string? BuyerForeignId = null,
    bool HasXml = false,
    string? VerificationUrl = null)
{
    public static Expression<Func<InvoiceHeader, InvoiceResponse>> Projection(IQueryable<UserProfile> profiles) => i => new InvoiceResponse(
        i.Id,
        i.KSeFInvoiceNumber,
        i.KSeFReferenceNumber,
        i.InvoiceNumber,
        i.VendorName,
        i.VendorNip,
        i.BuyerName,
        i.BuyerNip,
        i.AmountNet,
        i.AmountVat,
        i.AmountGross,
        i.Currency,
        i.IssueDate,
        i.AcquisitionDate,
        i.InvoiceType,
        i.FirstSeenAt,
        i.IsPaid,
        i.PaidAt,
        i.VendorBankAccount,
        null,
        profiles.Where(p => p.UserId == i.PaidByUserId).Select(p => p.DisplayName ?? p.Email).FirstOrDefault(),
        i.DueDate,
        i.Direction.ToString(),
        i.BuyerIdType,
        i.BuyerForeignId,
        i.Xml != null);

    private static readonly Func<InvoiceHeader, InvoiceResponse> CompiledProjection = Projection(Enumerable.Empty<UserProfile>().AsQueryable()).Compile();

    public static async Task<InvoiceResponse> FromEntityAsync(ApplicationDbContext db, InvoiceHeader i, IVerificationLinkService verificationLinks)
    {
        var xmlSha256 = await db.InvoiceXmls.Where(x => x.InvoiceHeaderId == i.Id).Select(x => x.Sha256).FirstOrDefaultAsync();
        var paidByName = i.PaidByUserId is null
            ? null
            : await db.UserProfiles.Where(p => p.UserId == i.PaidByUserId).Select(p => p.DisplayName ?? p.Email).FirstOrDefaultAsync();
        return CompiledProjection(i) with
        {
            PaidByName = paidByName,
            HasXml = xmlSha256 is not null,
            VerificationUrl = xmlSha256 is null || i.VendorNip.Length == 0
                ? null
                : verificationLinks.BuildInvoiceVerificationUrl(i.VendorNip, i.IssueDate, xmlSha256),
            Lines = i.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new InvoiceLineResponse(
                    l.LineNumber, l.Name, l.Unit, l.Quantity,
                    l.UnitPriceNet, l.UnitPriceGross,
                    l.AmountNet, l.AmountGross, l.AmountVat, l.VatRate))
                .ToList()
        };
    }

    public bool IsOverdue =>
        Direction != nameof(InvoiceDirection.Issued) && InvoiceHeader.IsOverdue(IsPaid, DueDate, DateTimeOffset.UtcNow);
}
