namespace OpenKSeF.Domain.Abstractions;

/// <summary>KSeF online session operations for sending issued invoices; the access token comes from <see cref="IKSeFGateway.InitSessionAsync"/>.</summary>
public interface IKSeFInvoiceSubmitter
{
    Task<KSeFOnlineSession> OpenSessionAsync(string accessToken, CancellationToken ct = default);

    Task<string> SendAsync(KSeFOnlineSession session, byte[] invoiceXml, CancellationToken ct = default);

    Task<KSeFSentInvoice?> GetInvoiceAsync(string accessToken, string sessionRef, string elementRef, CancellationToken ct = default);

    Task<IReadOnlyList<KSeFSentInvoice>> ListInvoicesAsync(string accessToken, string sessionRef, CancellationToken ct = default);

    Task<byte[]?> GetUpoAsync(string accessToken, string sessionRef, string elementRef, CancellationToken ct = default);

    Task CloseSessionAsync(KSeFOnlineSession session, CancellationToken ct = default);
}

public sealed record KSeFOnlineSession(string ReferenceNumber, string AccessToken, byte[] CipherKey, byte[] CipherIv);

public sealed record KSeFSentInvoice(
    string ElementRef,
    string? InvoiceHash,
    int Code,
    string? Description,
    IReadOnlyList<string> Details,
    string? KsefNumber,
    DateTimeOffset? AcceptedAt)
{
    public const int Processing = 150;
    public const int Accepted = 200;
    public const int Duplicate = 440;
}
