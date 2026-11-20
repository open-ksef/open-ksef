using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Domain.Services;

public record SellerProfileInput(
    string? LegalName,
    string? AddressLine1,
    string? AddressLine2,
    string? BankAccount,
    bool CashAccounting,
    string? NumberingPattern,
    int? NextNumber,
    string? NextNumberPeriod,
    string? BankSwift = null,
    string? BankName = null,
    string? ForeignBankAccount = null,
    string? ForeignBankSwift = null,
    string? ForeignBankName = null,
    string? CorrectionNumberingPattern = null,
    int? CorrectionNextNumber = null,
    string? CorrectionNextNumberPeriod = null);

public record SellerProfileView(
    SellerProfile Profile,
    string Nip,
    int NextNumber,
    string NextNumberPeriod,
    string NextNumberPreview,
    int CorrectionNextNumber,
    string CorrectionNextNumberPeriod,
    string CorrectionNextNumberPreview);

public interface ISellerProfileService
{
    Task<SellerProfileView?> GetAsync(Guid tenantId, CancellationToken ct = default);

    Task<(SellerProfileView? View, IReadOnlyList<InvoiceCheck> Errors)> SaveAsync(Guid tenantId, SellerProfileInput input, CancellationToken ct = default);

    Task<int> AllocateNumberAsync(Guid tenantId, string period, string series = IssuedInvoiceTypes.FvSeries, CancellationToken ct = default);
}
