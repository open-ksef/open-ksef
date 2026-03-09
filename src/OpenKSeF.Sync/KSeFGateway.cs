using System.Net;
using System.Text;
using KSeF.Client.Core.Exceptions;
using KSeF.Client.Core.Interfaces;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.Authorization;
using KSeF.Client.Core.Models.Invoices;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;

using CirfmfKSeFClient = KSeF.Client.Core.Interfaces.Clients.IKSeFClient;

namespace OpenKSeF.Sync;

/// <summary>
/// Implements <see cref="IKSeFGateway"/> using the CIRFMF KSeF.Client library.
/// Handles authentication via <see cref="IAuthCoordinator"/> and delegates
/// invoice operations to <see cref="CirfmfKSeFClient"/>.
/// </summary>
public sealed class KSeFGateway : IKSeFGateway
{
    private readonly CirfmfKSeFClient _client;
    private readonly IAuthCoordinator _authCoordinator;
    private readonly ICryptographyService _cryptography;
    private readonly ILogger<KSeFGateway> _logger;

    public KSeFGateway(
        CirfmfKSeFClient client,
        IAuthCoordinator authCoordinator,
        ICryptographyService cryptography,
        ILogger<KSeFGateway> logger)
    {
        _client = client;
        _authCoordinator = authCoordinator;
        _cryptography = cryptography;
        _logger = logger;
    }

    public async Task<KSeFSession> InitSessionAsync(
        string nip, string authToken, CancellationToken ct = default)
    {
        var authResult = await _authCoordinator.AuthKsefTokenAsync(
            AuthenticationTokenContextIdentifierType.Nip,
            nip,
            authToken,
            _cryptography,
            EncryptionMethodEnum.Rsa,
            cancellationToken: ct);

        _logger.LogInformation(
            "KSeF token session initiated for NIP {Nip}, valid until {ValidUntil}",
            nip, authResult.AccessToken.ValidUntil);

        return new KSeFSession(
            Token: authResult.AccessToken.Token,
            ReferenceNumber: string.Empty,
            ExpiresAtUtc: authResult.AccessToken.ValidUntil.UtcDateTime);
    }

    public async Task<KSeFSession> InitSignedSessionAsync(
        string nip, Func<string, Task<string>> xmlSigner, string fingerprint, CancellationToken ct = default)
    {
        var authResult = await _authCoordinator.AuthAsync(
            AuthenticationTokenContextIdentifierType.Nip,
            nip,
            AuthenticationTokenSubjectIdentifierTypeEnum.CertificateSubject,
            xmlSigner: xmlSigner,
            cancellationToken: ct);

        _logger.LogInformation(
            "KSeF signed session initiated for NIP {Nip}, valid until {ValidUntil}",
            nip, authResult.AccessToken.ValidUntil);

        return new KSeFSession(
            Token: authResult.AccessToken.Token,
            ReferenceNumber: string.Empty,
            ExpiresAtUtc: authResult.AccessToken.ValidUntil.UtcDateTime);
    }

    public async Task<KSeFInvoiceQueryResult> QueryInvoicesAsync(
        KSeFSession session, KSeFQueryCriteria criteria, CancellationToken ct = default)
    {
        var filters = new InvoiceQueryFilters
        {
            SubjectType = InvoiceSubjectType.Subject2,
            DateRange = new DateRange
            {
                DateType = DateType.Invoicing,
                From = criteria.DateFrom.HasValue
                    ? new DateTimeOffset(criteria.DateFrom.Value, TimeSpan.Zero)
                    : DateTimeOffset.UtcNow.AddMonths(-3),
                To = criteria.DateTo.HasValue
                    ? new DateTimeOffset(criteria.DateTo.Value, TimeSpan.Zero)
                    : null
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

        var totalCount = invoices.Count + (result.HasMore ? criteria.PageSize : 0);

        return new KSeFInvoiceQueryResult(invoices, totalCount, criteria.PageOffset, criteria.PageSize);
    }

    public async Task<byte[]> DownloadInvoiceAsync(
        KSeFSession session, string ksefNumber, CancellationToken ct = default)
    {
        var xml = await _client.GetInvoiceAsync(ksefNumber, session.Token, ct);
        return Encoding.UTF8.GetBytes(xml);
    }

    public Task TerminateSessionAsync(KSeFSession session, CancellationToken ct = default)
    {
        _logger.LogInformation("KSeF session released (token-based auth, no explicit close needed)");
        return Task.CompletedTask;
    }

    private static KSeFInvoiceHeader MapInvoice(InvoiceSummary inv) => new(
        KSeFNumber: inv.KsefNumber ?? string.Empty,
        ReferenceNumber: inv.KsefNumber ?? string.Empty,
        InvoiceNumber: inv.InvoiceNumber,
        VendorNip: inv.Seller?.Nip ?? string.Empty,
        VendorName: inv.Seller?.Name ?? string.Empty,
        BuyerNip: inv.Buyer?.Identifier?.Value,
        BuyerName: inv.Buyer?.Name,
        AmountNet: inv.NetAmount,
        AmountVat: inv.VatAmount,
        AmountGross: inv.GrossAmount,
        Currency: inv.Currency ?? "PLN",
        IssueDate: inv.IssueDate.UtcDateTime,
        AcquisitionDate: inv.AcquisitionDate.UtcDateTime,
        InvoiceType: inv.InvoiceType.ToString());
}
