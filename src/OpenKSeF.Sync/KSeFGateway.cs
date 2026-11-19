using System.Net;
using System.Net.Http.Headers;
using KSeF.Client.Api.Services;
using KSeF.Client.Core.Exceptions;
using KSeF.Client.Core.Interfaces;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.Authorization;
using KSeF.Client.Core.Models.Invoices;
using KSeF.Client.DI;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Services;

using CirfmfKSeFClient = KSeF.Client.Core.Interfaces.Clients.IKSeFClient;

namespace OpenKSeF.Sync;

/// <summary>
/// Implements <see cref="IKSeFGateway"/> using the CIRFMF KSeF.Client library.
/// Handles authentication via <see cref="IAuthCoordinator"/> and delegates
/// invoice operations to <see cref="CirfmfKSeFClient"/>.
/// </summary>
public sealed class KSeFGateway : IKSeFGateway
{
    private const int NameMaxLength = 512;

    private readonly CirfmfKSeFClient _client;
    private readonly IAuthCoordinator _authCoordinator;
    private readonly ICryptographyService _cryptography;
    private readonly IEncryptionService _encryption;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KSeFClientOptions _options;
    private readonly ILogger<KSeFGateway> _logger;

    public KSeFGateway(
        CirfmfKSeFClient client,
        IAuthCoordinator authCoordinator,
        ICryptographyService cryptography,
        IEncryptionService encryption,
        IHttpClientFactory httpClientFactory,
        KSeFClientOptions options,
        ILogger<KSeFGateway> logger)
    {
        _client = client;
        _authCoordinator = authCoordinator;
        _cryptography = cryptography;
        _encryption = encryption;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<KSeFSession> InitSessionAsync(
        string nip, KSeFCredential credential, CancellationToken ct = default)
    {
        var authResult = await _authCoordinator.LoginAsync(nip, credential, _encryption, _cryptography, ct);

        _logger.LogInformation(
            "KSeF {CredentialType} session initiated for NIP {Nip}, valid until {ValidUntil}",
            credential.Type, nip, authResult.AccessToken.ValidUntil);

        return new KSeFSession(
            Token: authResult.AccessToken.Token,
            ExpiresAtUtc: authResult.AccessToken.ValidUntil.UtcDateTime);
    }

    public async Task<KSeFInvoiceQueryResult> QueryInvoicesAsync(
        KSeFSession session, KSeFQueryCriteria criteria, CancellationToken ct = default)
    {
        var issued = criteria.Direction == InvoiceDirection.Issued;
        var filters = new InvoiceQueryFilters
        {
            SubjectType = issued ? InvoiceSubjectType.Subject1 : InvoiceSubjectType.Subject2,
            DateRange = new DateRange
            {
                // KSeF docs: incremental sync must use PermanentStorage; Invoicing can skip
                // invoices that are persisted after the window was already queried.
                DateType = DateType.PermanentStorage,
                From = criteria.DateFrom.HasValue
                    ? new DateTimeOffset(criteria.DateFrom.Value, TimeSpan.Zero)
                    : DateTimeOffset.UtcNow.AddMonths(-3),
                To = criteria.DateTo.HasValue
                    ? new DateTimeOffset(criteria.DateTo.Value, TimeSpan.Zero)
                    : null,
                RestrictToPermanentStorageHwmDate = issued ? true : null
            }
        };

        var result = await _client.QueryInvoiceMetadataAsync(
            filters,
            session.Token,
            pageOffset: criteria.PageOffset,
            pageSize: criteria.PageSize,
            cancellationToken: ct);

        var invoices = result.Invoices?.Select(MapInvoice).ToList()
            ?? new List<KSeFInvoiceHeader>();

        var lastRowAt = result.Invoices?.LastOrDefault()?.PermanentStorageDate.UtcDateTime;
        var continuationAt = result.IsTruncated ? lastRowAt : result.PermanentStorageHwmDate?.UtcDateTime;

        return new KSeFInvoiceQueryResult(invoices, continuationAt, result.IsTruncated, lastRowAt);
    }

    /// <remarks>Raw HTTP instead of <c>GetInvoiceAsync</c>: the SDK reads the body as a string, which drops a BOM and breaks byte equality with the KSeF original.</remarks>
    public async Task<byte[]> DownloadInvoiceAsync(
        KSeFSession session, string ksefNumber, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(_options.BaseUrl), $"/v2/invoices/ksef/{Uri.EscapeDataString(ksefNumber)}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        using var response = await _httpClientFactory.CreateClient().SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw KsefRateLimitException.FromRetryAfterHeader(
                "KSeF HTTP 429 on invoice XML",
                response.Headers.TryGetValues("Retry-After", out var retryAfter) ? retryAfter.FirstOrDefault() : null);
        if (!response.IsSuccessStatusCode)
            throw new KsefApiException($"KSeF HTTP {(int)response.StatusCode} on invoice XML {ksefNumber}", response.StatusCode);

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <remarks>The reference number is the first segment of the token string (`ref|nip-NIP|[exp-DATE|]secret` as seen on KSeF TEST tokens, not in ksef-docs); another shape fails closed. The session is revoked right after the read, never cached.</remarks>
    public async Task<KSeFTokenGrant?> CheckTokenAsync(string nip, string token, CancellationToken ct = default)
    {
        if (token.Split('|') is not [var referenceNumber, _, _, ..] || referenceNumber.Length == 0)
            return NotProven(nip, "noref");

        var auth = new RejectionAwareAuthClient(_client);
        AuthenticationOperationStatusResponse login;
        try
        {
            login = await new AuthCoordinator(auth).AuthKsefTokenAsync(
                AuthenticationTokenContextIdentifierType.Nip, nip, token, _cryptography, EncryptionMethodEnum.Rsa, cancellationToken: ct);
        }
        catch (Exception e) when (e is KSeFAuthRejectedException || IsClientError(e))
        {
            return NotProven(nip, "rejected");
        }

        try
        {
            var meta = await _client.GetKsefTokenAsync(referenceNumber, login.AccessToken.Token, ct);
            if (meta.Status != AuthenticationKsefTokenStatus.Active)
                return NotProven(nip, "inactive");
            if (meta.ContextIdentifier is not { Type: AuthenticationTokenContextIdentifierType.Nip } context || context.Value != nip)
                return NotProven(nip, "context");

            var permissions = (meta.RequestedPermissions ?? []).Select(p => p.ToString()).ToList();
            _logger.LogInformation("KSeF token check for NIP {Nip}: ok, permissions {Permissions}", nip, string.Join(',', permissions));
            return new KSeFTokenGrant(permissions, auth.ReferenceNumber ?? string.Empty);
        }
        catch (KsefApiException e) when (IsClientError(e))
        {
            return NotProven(nip, $"metadata HTTP {(int?)e.StatusCode}");
        }
        finally
        {
            try
            {
                await _client.RevokeCurrentSessionAsync(login.AccessToken.Token, CancellationToken.None);
            }
            catch (Exception)
            {
                _logger.LogWarning("KSeF token check for NIP {Nip}: closing the session failed", nip);
            }
        }
    }

    private KSeFTokenGrant? NotProven(string nip, string reason)
    {
        _logger.LogInformation("KSeF token check for NIP {Nip}: {Reason}", nip, reason);
        return null;
    }

    private static bool IsClientError(Exception e) =>
        e is KsefApiException { StatusCode: >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError } and not KsefRateLimitException;

    private static KSeFInvoiceHeader MapInvoice(InvoiceSummary inv)
    {
        var buyerId = inv.Buyer?.Identifier;
        var buyerIsNip = buyerId?.Type == BuyerIdentifierType.Nip;

        return new(
            KSeFNumber: inv.KsefNumber ?? string.Empty,
            InvoiceNumber: inv.InvoiceNumber,
            VendorNip: inv.Seller?.Nip ?? string.Empty,
            VendorName: CutName(inv.Seller?.Name) ?? string.Empty,
            BuyerNip: buyerIsNip ? buyerId!.Value : null,
            BuyerName: CutName(inv.Buyer?.Name),
            AmountNet: inv.NetAmount,
            AmountVat: inv.VatAmount,
            AmountGross: inv.GrossAmount,
            Currency: inv.Currency ?? "PLN",
            IssueDate: DateTime.SpecifyKind(inv.IssueDate.Date, DateTimeKind.Utc),
            AcquisitionDate: inv.AcquisitionDate.UtcDateTime,
            InvoiceType: inv.InvoiceType.ToString(),
            BuyerIdType: buyerId?.Type.ToString(),
            BuyerForeignId: buyerIsNip ? null : buyerId?.Value);
    }

    private static string? CutName(string? name) =>
        name is { Length: > NameMaxLength } ? name[..NameMaxLength] : name;
}
