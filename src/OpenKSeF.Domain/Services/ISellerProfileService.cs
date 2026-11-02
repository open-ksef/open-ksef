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
    string? NextNumberPeriod);

public record SellerProfileView(SellerProfile Profile, string Nip, int NextNumber, string NextNumberPeriod, string NextNumberPreview);

public interface ISellerProfileService
{
    Task<SellerProfileView?> GetAsync(Guid tenantId, CancellationToken ct = default);

    Task<(SellerProfileView? View, IReadOnlyList<InvoiceCheck> Errors)> SaveAsync(Guid tenantId, SellerProfileInput input, CancellationToken ct = default);

    Task<int> AllocateNumberAsync(Guid tenantId, string period, CancellationToken ct = default);
}
