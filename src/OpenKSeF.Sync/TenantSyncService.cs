using System.Security.Cryptography.X509Certificates;
using KSeF.Client.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.DTOs;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Sync;

public sealed class TenantSyncService : ITenantSyncService
{
    private readonly ApplicationDbContext _db;
    private readonly IKSeFGateway _gateway;
    private readonly IEncryptionService _encryption;
    private readonly IInvoiceService _invoiceService;
    private readonly KSeFInvoiceXmlParser _xmlParser;
    private readonly TenantSyncOptions _syncOptions;
    private readonly ILogger<TenantSyncService> _logger;

    public TenantSyncService(
        ApplicationDbContext db,
        IKSeFGateway gateway,
        IEncryptionService encryption,
        IInvoiceService invoiceService,
        KSeFInvoiceXmlParser xmlParser,
        IOptions<TenantSyncOptions> syncOptions,
        ILogger<TenantSyncService> logger)
    {
        _db = db;
        _gateway = gateway;
        _encryption = encryption;
        _invoiceService = invoiceService;
        _xmlParser = xmlParser;
        _syncOptions = syncOptions.Value;
        if (_syncOptions.BatchSize <= 0)
        {
            _syncOptions.BatchSize = 100;
        }
        _logger = logger;
    }

    public async Task<IReadOnlyList<TenantSyncResult>> SyncAllTenantsAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await _db.Tenants
            .Include(t => t.KSeFCredentials)
            .Include(t => t.SyncState)
            .Where(t => t.KSeFCredentials.Any())
            .ToListAsync(cancellationToken);

        var results = new List<TenantSyncResult>(tenants.Count);
        foreach (var tenant in tenants)
        {
            var result = await SyncTenantInternalAsync(tenant, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    public async Task<TenantSyncResult> SyncTenantAsync(
        Guid tenantId,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants
            .Include(t => t.KSeFCredentials)
            .Include(t => t.SyncState)
            .FirstOrDefaultAsync(t => t.Id == tenantId && (userId == null || t.UserId == userId), cancellationToken);

        if (tenant is null)
        {
            return new TenantSyncResult(
                TenantId: tenantId,
                Nip: string.Empty,
                Outcome: TenantSyncOutcome.TenantNotFound,
                ErrorMessage: "Tenant not found.");
        }

        return await SyncTenantInternalAsync(tenant, cancellationToken);
    }

    private async Task<TenantSyncResult> SyncTenantInternalAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        var credential = tenant.KSeFCredentials.FirstOrDefault();
        if (credential is null)
        {
            return new TenantSyncResult(
                TenantId: tenant.Id,
                Nip: tenant.Nip,
                Outcome: TenantSyncOutcome.MissingCredential,
                ErrorMessage: "No credential configured for this tenant.");
        }

        KSeFSession? session = null;
        try
        {
            session = credential.Type switch
            {
                CredentialType.Certificate => await InitCertificateSessionAsync(tenant.Nip, credential, cancellationToken),
                _ => await InitTokenSessionAsync(tenant.Nip, credential, cancellationToken),
            };

            var syncState = tenant.SyncState ?? new SyncState
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id
            };

            var now = DateTime.UtcNow;
            var windowStart = syncState.LastSuccessfulSync?.AddHours(-1)
                ?? now.AddMonths(-_syncOptions.InitialSyncMonthsBack);
            var totalFetched = 0;
            var totalNew = 0;

            while (windowStart < now)
            {
                var windowEnd = windowStart.AddMonths(3);
                var isLastWindow = windowEnd >= now;
                if (isLastWindow)
                    windowEnd = now;

                var pageOffset = 0;
                while (true)
                {
                    var criteria = new KSeFQueryCriteria(
                        SubjectNip: tenant.Nip,
                        DateFrom: windowStart,
                        DateTo: windowEnd,
                        PageSize: _syncOptions.BatchSize,
                        PageOffset: pageOffset);

                    var result = await _gateway.QueryInvoicesAsync(session, criteria, cancellationToken);

                    totalFetched += result.Invoices.Count;
                    if (result.Invoices.Count == 0)
                        break;

                    var batchNumbers = result.Invoices.Select(i => i.KSeFNumber).ToList();
                    var invoicesWithBankAccount = await _db.InvoiceHeaders
                        .Where(h => h.TenantId == tenant.Id
                            && batchNumbers.Contains(h.KSeFInvoiceNumber)
                            && h.VendorBankAccount != null)
                        .Select(h => h.KSeFInvoiceNumber)
                        .ToListAsync(cancellationToken);
                    var skipDownloadSet = new HashSet<string>(invoicesWithBankAccount);

                    var invoiceDtos = new List<InvoiceDto>(result.Invoices.Count);
                    foreach (var i in result.Invoices)
                    {
                        string? bankAccount = null;
                        if (!skipDownloadSet.Contains(i.KSeFNumber))
                        {
                            bankAccount = await DownloadBankAccountAsync(
                                session, i.KSeFNumber, cancellationToken);
                        }

                        invoiceDtos.Add(new InvoiceDto(
                            Number: i.KSeFNumber,
                            ReferenceNumber: i.ReferenceNumber,
                            InvoiceNumber: i.InvoiceNumber,
                            VendorName: i.VendorName,
                            VendorNip: i.VendorNip,
                            BuyerName: i.BuyerName,
                            BuyerNip: i.BuyerNip,
                            AmountNet: i.AmountNet,
                            AmountVat: i.AmountVat,
                            AmountGross: i.AmountGross,
                            Currency: i.Currency,
                            IssueDate: i.IssueDate,
                            AcquisitionDate: i.AcquisitionDate,
                            InvoiceType: i.InvoiceType,
                            VendorBankAccount: bankAccount));
                    }

                    var newIds = await _invoiceService.UpsertInvoicesAsync(tenant.Id, invoiceDtos);
                    totalNew += newIds.Count;

                    if (result.Invoices.Count < _syncOptions.BatchSize)
                        break;

                    pageOffset++;
                }

                windowStart = windowEnd;
            }

            syncState.LastSyncedAt = now;
            syncState.LastSuccessfulSync = now;

            if (!await _db.SyncStates.AnyAsync(s => s.TenantId == tenant.Id, cancellationToken))
                _db.SyncStates.Add(syncState);

            await _db.SaveChangesAsync(cancellationToken);

            return new TenantSyncResult(
                TenantId: tenant.Id,
                Nip: tenant.Nip,
                Outcome: TenantSyncOutcome.Success,
                FetchedInvoices: totalFetched,
                NewInvoices: totalNew,
                SyncedAtUtc: now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Sync failed for tenant {TenantId}", tenant.Id);

            var statusCode = (ex as KSeF.Client.Core.Exceptions.KsefApiException)?.StatusCode;
            return new TenantSyncResult(
                TenantId: tenant.Id,
                Nip: tenant.Nip,
                Outcome: TenantSyncOutcome.Failed,
                ErrorMessage: ex.Message,
                ErrorStatusCode: statusCode.HasValue ? (int)statusCode.Value : null);
        }
        finally
        {
            if (session is not null)
            {
                try
                {
                    await _gateway.TerminateSessionAsync(session, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to terminate KSeF session for tenant {TenantId}", tenant.Id);
                }
            }
        }
    }

    private async Task<string?> DownloadBankAccountAsync(
        KSeFSession session, string ksefNumber, CancellationToken ct)
    {
        try
        {
            var xmlBytes = await _gateway.DownloadInvoiceAsync(session, ksefNumber, ct);
            return _xmlParser.ExtractBankAccount(xmlBytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to download invoice XML for bank account extraction: {KSeFNumber}", ksefNumber);
            return null;
        }
    }

    private async Task<KSeFSession> InitTokenSessionAsync(
        string nip, KSeFCredential credential, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(credential.EncryptedToken))
            throw new InvalidOperationException("Token credential is missing encrypted token data.");

        var authToken = _encryption.Decrypt(credential.EncryptedToken);
        return await _gateway.InitSessionAsync(nip, authToken, ct);
    }

    private async Task<KSeFSession> InitCertificateSessionAsync(
        string nip, KSeFCredential credential, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(credential.EncryptedCertificateData) ||
            string.IsNullOrEmpty(credential.CertificateFingerprint))
            throw new InvalidOperationException("Certificate credential is missing certificate data or fingerprint.");

        var certBase64 = _encryption.Decrypt(credential.EncryptedCertificateData);
        var pfxBytes = Convert.FromBase64String(certBase64);
        using var cert = new X509Certificate2(pfxBytes);
        var fingerprint = credential.CertificateFingerprint;

        return await _gateway.InitSignedSessionAsync(
            nip,
            challengeXml => Task.FromResult(SignatureService.Sign(challengeXml, cert)),
            fingerprint,
            ct);
    }
}
