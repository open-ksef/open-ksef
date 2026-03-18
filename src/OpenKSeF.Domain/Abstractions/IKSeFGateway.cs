namespace OpenKSeF.Domain.Abstractions;

public interface IKSeFGateway
{
    Task<KSeFSession> InitSessionAsync(string nip, string authToken, CancellationToken ct = default);

    Task<KSeFSession> InitSignedSessionAsync(string nip, Func<string, Task<string>> xmlSigner, string fingerprint, CancellationToken ct = default);

    Task<KSeFInvoiceQueryResult> QueryInvoicesAsync(KSeFSession session, KSeFQueryCriteria criteria, CancellationToken ct = default);

    Task<byte[]> DownloadInvoiceAsync(KSeFSession session, string ksefNumber, CancellationToken ct = default);

    Task TerminateSessionAsync(KSeFSession session, CancellationToken ct = default);
}

public record KSeFSession(string Token, string ReferenceNumber, DateTime ExpiresAtUtc);

public record KSeFQueryCriteria(
    string SubjectNip,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    int PageSize = 100,
    int PageOffset = 0);

public record KSeFInvoiceHeader(
    string KSeFNumber,
    string ReferenceNumber,
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
    string? VendorBankAccount = null);

public record KSeFInvoiceQueryResult(
    IReadOnlyList<KSeFInvoiceHeader> Invoices,
    int TotalCount,
    int PageOffset,
    int PageSize);
