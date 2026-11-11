using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Domain.Abstractions;

public interface IKSeFGateway
{
    Task<KSeFSession> InitSessionAsync(string nip, KSeFCredential credential, CancellationToken ct = default);

    Task<KSeFInvoiceQueryResult> QueryInvoicesAsync(KSeFSession session, KSeFQueryCriteria criteria, CancellationToken ct = default);

    Task<byte[]> DownloadInvoiceAsync(KSeFSession session, string ksefNumber, CancellationToken ct = default);
}

public record KSeFSession(string Token, DateTime ExpiresAtUtc);

public record KSeFQueryCriteria(
    string SubjectNip,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    int PageSize = 100,
    int PageOffset = 0,
    InvoiceDirection Direction = InvoiceDirection.Received);

public record KSeFInvoiceHeader(
    string KSeFNumber,
    string? InvoiceNumber,
    string VendorNip,
    string VendorName,
    string? BuyerNip,
    string? BuyerName,
    decimal AmountNet,
    decimal AmountVat,
    decimal AmountGross,
    string Currency,
    DateTime IssueDate,
    DateTime? AcquisitionDate,
    string? InvoiceType,
    string? VendorBankAccount = null,
    string? BuyerIdType = null,
    string? BuyerForeignId = null);

public record KSeFInvoiceQueryResult(
    IReadOnlyList<KSeFInvoiceHeader> Invoices,
    DateTime? ContinuationAt = null,
    bool IsTruncated = false,
    DateTime? LastRowAt = null);
