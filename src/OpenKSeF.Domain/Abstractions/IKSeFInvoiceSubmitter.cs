using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Domain.Abstractions;

/// <summary>KSeF login and online session operations for sending issued invoices.</summary>
public interface IKSeFInvoiceSubmitter
{
    /// <summary>Returns the access token. Throws <see cref="KSeFAuthRejectedException"/> when KSeF refuses the token or certificate.</summary>
    Task<string> LoginAsync(string nip, KSeFCredential credential, CancellationToken ct = default);

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
    DateTimeOffset? AcceptedAt,
    string? OriginalKsefNumber = null,
    string? OriginalSessionRef = null)
{
    public const int Processing = 150;
    public const int Accepted = 200;
    public const int Duplicate = 440;
}

/// <summary>KSeF answered the login with a final refusal (invalid, revoked or inactive token or certificate), as opposed to being unavailable.</summary>
public sealed class KSeFAuthRejectedException(int code, string reason) : Exception(reason)
{
    public int Code { get; } = code;
}
