using System.Net;
using System.Security.Cryptography.X509Certificates;
using KSeF.Client.Api.Services;
using KSeF.Client.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.DTOs;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Sync;

public sealed class TenantSyncService : ITenantSyncService
{
    private const int IssuedFirstRunMonthsBack = 12;
    private const int MaxIssuedQueriesPerRun = 10;
    private const int MaxConsecutiveDownloadFailures = 3;

    private readonly ApplicationDbContext _db;
    private readonly IKSeFGateway _gateway;
    private readonly IEncryptionService _encryption;
    private readonly IInvoiceService _invoiceService;
    private readonly KSeFInvoiceXmlParser _xmlParser;
    private readonly TenantSyncOptions _syncOptions;
    private readonly XmlDownloadBlocks _xmlBlocks;
    private readonly ILogger<TenantSyncService> _logger;

    public TenantSyncService(
        ApplicationDbContext db,
        IKSeFGateway gateway,
        IEncryptionService encryption,
        IInvoiceService invoiceService,
        KSeFInvoiceXmlParser xmlParser,
        IOptions<TenantSyncOptions> syncOptions,
        XmlDownloadBlocks xmlBlocks,
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
        _xmlBlocks = xmlBlocks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TenantSyncResult>> SyncAllTenantsAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await _db.Tenants.AsNoTracking()
            .Include(t => t.KSeFCredentials)
            .Where(t => t.KSeFCredentials.Any())
            .ToListAsync(cancellationToken);

        var runStartedAt = DateTime.UtcNow;
        var sessions = new Dictionary<Guid, KSeFSession?>();
        try
        {
            var results = new List<TenantSyncResult>(tenants.Count);
            foreach (var tenant in tenants)
                results.Add(await SyncMetadataAsync(tenant, fullResyncWindowStart: null, sessions, cancellationToken));

            // Five failed calls in a row open the KSeF.Client breaker for every caller, so XML waits for all metadata.
            var synced = results.Where(r => r.SyncedAtUtc is not null).Select(r => r.TenantId).ToHashSet();
            foreach (var sameNip in tenants.Where(t => synced.Contains(t.Id)).GroupBy(t => t.Nip))
            {
                await DownloadQueuedXmlAsync(
                    sameNip.ToList(), sessions, runStartedAt, _syncOptions.XmlDownloadsPerCycle, _syncOptions.XmlDownloadSpacing, fullResync: false, cancellationToken);
            }

            return results;
        }
        finally
        {
            await TerminateSessionsAsync(sessions, cancellationToken);
        }
    }

    public async Task<TenantSyncResult> SyncTenantAsync(
        Guid tenantId,
        string? userId = null,
        bool forceFullResync = false,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.AsNoTracking()
            .Include(t => t.KSeFCredentials)
            .FirstOrDefaultAsync(t => t.Id == tenantId && (userId == null || t.UserId == userId), cancellationToken);

        if (tenant is null)
        {
            return new TenantSyncResult(
                TenantId: tenantId,
                Nip: string.Empty,
                Outcome: TenantSyncOutcome.TenantNotFound,
                ErrorMessage: "Tenant not found.");
        }

        DateTime? fullResyncWindowStart = null;
        if (forceFullResync)
        {
            var earliestIssueDate = await _db.InvoiceHeaders
                .Where(h => h.TenantId == tenantId && h.Direction == InvoiceDirection.Received)
                .MinAsync(h => (DateTime?)h.IssueDate, cancellationToken);
            fullResyncWindowStart = earliestIssueDate?.AddHours(-1)
                ?? DateTime.UtcNow.AddMonths(-_syncOptions.InitialSyncMonthsBack);
        }

        var runStartedAt = DateTime.UtcNow;
        var sessions = new Dictionary<Guid, KSeFSession?>();
        try
        {
            var result = await SyncMetadataAsync(tenant, fullResyncWindowStart, sessions, cancellationToken);
            if (result.SyncedAtUtc is null)
                return result;

            var (queued, errored) = await DownloadQueuedXmlAsync(
                [tenant], sessions, runStartedAt, _syncOptions.XmlDownloadsPerManualRun, _syncOptions.XmlManualDownloadSpacing, forceFullResync, cancellationToken);
            return result with { QueuedDownloads = queued, ErroredDownloads = errored };
        }
        finally
        {
            await TerminateSessionsAsync(sessions, cancellationToken);
        }
    }

    private async Task<TenantSyncResult> SyncMetadataAsync(
        Tenant tenant, DateTime? fullResyncWindowStart, Dictionary<Guid, KSeFSession?> sessions, CancellationToken cancellationToken)
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

        try
        {
            var storedState = await _db.SyncStates.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, cancellationToken);
            var syncState = storedState ?? new SyncState
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id
            };

            var session = await InitSessionAsync(tenant, credential, cancellationToken);
            sessions[tenant.Id] = session;

            var now = DateTime.UtcNow;
            var windowStart = fullResyncWindowStart
                ?? ReceivedResumePoint(syncState)
                ?? now.AddMonths(-_syncOptions.InitialSyncMonthsBack);
            // Initial sync and forced resync import history: store it, don't push it.
            var notify = fullResyncWindowStart is null && syncState.LastSuccessfulSync is not null;
            var totalFetched = 0;
            var totalNew = 0;

            while (windowStart < now)
            {
                var windowEnd = windowStart.AddMonths(3);
                var isLastWindow = windowEnd >= now;
                if (isLastWindow)
                    windowEnd = now;

                var pageOffset = 0;
                KSeFInvoiceQueryResult result;
                while (true)
                {
                    var criteria = new KSeFQueryCriteria(
                        SubjectNip: tenant.Nip,
                        DateFrom: windowStart,
                        DateTo: windowEnd,
                        PageSize: _syncOptions.BatchSize,
                        PageOffset: pageOffset);

                    result = await _gateway.QueryInvoicesAsync(session, criteria, cancellationToken);

                    totalFetched += result.Invoices.Count;
                    if (result.Invoices.Count == 0)
                        break;

                    var newIds = await _invoiceService.UpsertInvoicesAsync(
                        tenant.Id, result.Invoices.Select(i => ToDto(i, InvoiceDirection.Received)).ToList(), notify);
                    totalNew += newIds.Count;

                    if (result.IsTruncated || result.Invoices.Count < _syncOptions.BatchSize)
                        break;

                    pageOffset++;
                }

                if (result.IsTruncated)
                {
                    if (result.ContinuationAt is not { } next || next <= windowStart)
                        throw new InvalidOperationException($"Truncated KSeF result did not advance past {windowStart:O}.");

                    windowStart = next;
                    continue;
                }

                if (isLastWindow && result.ContinuationAt is not null)
                    syncState.ReceivedContinuationAt = result.ContinuationAt;

                windowStart = windowEnd;
            }

            syncState.LastSyncedAt = now;
            syncState.LastSuccessfulSync = now;

            if (storedState is null)
                _db.SyncStates.Add(syncState);

            await _db.SaveChangesAsync(cancellationToken);

            (int Fetched, int New) issued;
            try
            {
                issued = await SyncIssuedInvoicesAsync(tenant, session, syncState, now, cancellationToken);
            }
            catch (DbUpdateException)
            {
                return new TenantSyncResult(
                    TenantId: tenant.Id,
                    Nip: tenant.Nip,
                    Outcome: TenantSyncOutcome.StoreFailed,
                    FetchedInvoices: totalFetched,
                    NewInvoices: totalNew,
                    SyncedAtUtc: now,
                    ErrorMessage: "Issued invoice page could not be stored.");
            }

            return new TenantSyncResult(
                TenantId: tenant.Id,
                Nip: tenant.Nip,
                Outcome: TenantSyncOutcome.Success,
                FetchedInvoices: totalFetched + issued.Fetched,
                NewInvoices: totalNew + issued.New,
                SyncedAtUtc: now,
                IssuedFetchedInvoices: issued.Fetched,
                IssuedNewInvoices: issued.New);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Sync failed for tenant {TenantId}", tenant.Id);

            var statusCode = (ex as KsefApiException)?.StatusCode;
            return new TenantSyncResult(
                TenantId: tenant.Id,
                Nip: tenant.Nip,
                Outcome: ex is DbUpdateException ? TenantSyncOutcome.StoreFailed : TenantSyncOutcome.Failed,
                ErrorMessage: ex is DbUpdateException ? "Invoice data could not be stored." : ex.Message,
                ErrorStatusCode: statusCode.HasValue ? (int)statusCode.Value : null);
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }
    }

    private static DateTime? ReceivedResumePoint(SyncState syncState)
    {
        var overlap = syncState.LastSuccessfulSync?.AddHours(-1);
        return syncState.ReceivedContinuationAt < overlap ? syncState.ReceivedContinuationAt : overlap;
    }

    /// <returns>Queued: still waiting, not tried or stopped by a 429. Errored: tried in this run and failed.</returns>
    private async Task<(int Queued, int Errored)> DownloadQueuedXmlAsync(
        IReadOnlyList<Tenant> tenants,
        Dictionary<Guid, KSeFSession?> sessions,
        DateTime runStartedAt,
        int budget,
        TimeSpan spacing,
        bool fullResync,
        CancellationToken cancellationToken)
    {
        var nip = tenants[0].Nip;
        var tenantIds = tenants.Select(t => t.Id).ToList();
        var waiting = _db.InvoiceHeaders.AsNoTracking()
            .Where(h => tenantIds.Contains(h.TenantId)
                && ((h.XmlFetchedAt == null
                        && (!h.Lines.Any()
                            || (h.Direction == InvoiceDirection.Received && h.DueDate == null && (!h.IsPaid || fullResync))))
                    || (h.Direction == InvoiceDirection.Received && h.Settlement == null && (!h.IsPaid || fullResync))));
        var waitingCount = await waiting.CountAsync(cancellationToken);
        if (waitingCount == 0)
            return (0, 0);

        var queue = await waiting
            .OrderBy(h => h.Direction == InvoiceDirection.Received && h.FirstSeenAt >= runStartedAt ? 0
                : h.Direction == InvoiceDirection.Received && !h.IsPaid && h.DueDate == null && h.XmlFetchedAt == null ? 1
                : h.Direction == InvoiceDirection.Issued ? 2
                : h.Direction == InvoiceDirection.Received && !h.IsPaid && h.Settlement == null ? 3
                : 4)
            .ThenBy(h => h.XmlAttemptedAt == null ? 0 : 1)
            .ThenBy(h => h.XmlAttemptedAt)
            .ThenByDescending(h => h.IssueDate)
            .ThenBy(h => h.Id)
            .Select(h => new { h.Id, h.TenantId, h.KSeFInvoiceNumber })
            .Take(budget)
            .ToListAsync(cancellationToken);

        if (_xmlBlocks.IsBlocked(nip))
        {
            _logger.LogInformation("Invoice XML downloads for NIP {Nip} wait for KSeF Retry-After", nip);
            return (waitingCount, 0);
        }

        var completed = 0;
        var errored = 0;
        var attempted = 0;
        var failuresInARow = 0;
        foreach (var row in queue)
        {
            if (attempted++ > 0 && spacing > TimeSpan.Zero)
                await Task.Delay(spacing, cancellationToken);

            try
            {
                var session = await CurrentSessionAsync(tenants.First(t => t.Id == row.TenantId), sessions, cancellationToken);
                var xml = await _gateway.DownloadInvoiceAsync(session!, row.KSeFInvoiceNumber, cancellationToken);
                failuresInARow = 0;

                var details = _xmlParser.ExtractInvoiceDetails(xml);
                if (details.Lines is null)
                {
                    await _invoiceService.RecordFailedInvoiceXmlAsync(row.Id, cancellationToken);
                    errored++;
                    continue;
                }

                await _invoiceService.ApplyInvoiceXmlAsync(row.Id, details, cancellationToken);
                completed++;
            }
            catch (KsefApiException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = (ex as KsefRateLimitException)?.RecommendedDelay ?? TimeSpan.Zero;
                _xmlBlocks.Block(nip, retryAfter);
                _logger.LogWarning("KSeF 429 on invoice XML for NIP {Nip}, stopping downloads, Retry-After {RetryAfter}", nip, retryAfter);
                break;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to store invoice XML details: {KSeFNumber}", row.KSeFInvoiceNumber);
                _db.ChangeTracker.Clear();
                await _invoiceService.RecordFailedInvoiceXmlAsync(row.Id, cancellationToken);
                errored++;
                if (++failuresInARow >= MaxConsecutiveDownloadFailures)
                    break;
            }
        }

        _db.ChangeTracker.Clear();
        _logger.LogInformation("Invoice XML queue for NIP {Nip}: {Completed} of {Queued} done", nip, completed, queue.Count);
        return (waitingCount - completed - errored, errored);
    }

    private async Task<KSeFSession?> CurrentSessionAsync(
        Tenant tenant, Dictionary<Guid, KSeFSession?> sessions, CancellationToken cancellationToken)
    {
        if (sessions.TryGetValue(tenant.Id, out var session) && session?.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(1))
            return session;

        return sessions[tenant.Id] = await InitSessionAsync(tenant, tenant.KSeFCredentials.First(), cancellationToken);
    }

    private Task<KSeFSession> InitSessionAsync(Tenant tenant, KSeFCredential credential, CancellationToken cancellationToken) =>
        credential.Type switch
        {
            CredentialType.Certificate => InitCertificateSessionAsync(tenant.Nip, credential, cancellationToken),
            _ => InitTokenSessionAsync(tenant.Nip, credential, cancellationToken),
        };

    private async Task TerminateSessionsAsync(Dictionary<Guid, KSeFSession?> sessions, CancellationToken cancellationToken)
    {
        foreach (var (tenantId, session) in sessions)
        {
            if (session is null)
                continue;

            try
            {
                await _gateway.TerminateSessionAsync(session, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to terminate KSeF session for tenant {TenantId}", tenantId);
            }
        }
    }

    private async Task<(int Fetched, int New)> SyncIssuedInvoicesAsync(
        Tenant tenant, KSeFSession session, SyncState syncState, DateTime now, CancellationToken cancellationToken)
    {
        var fetched = 0;
        var created = 0;
        if (syncState.IssuedFetchIsRecent(now))
        {
            _logger.LogInformation(
                "Issued invoice sync skipped for tenant {TenantId}: continuation {ContinuationAt} is recent",
                tenant.Id, syncState.IssuedContinuationAt);
            return (fetched, created);
        }

        try
        {
            var continuationAtStart = syncState.IssuedContinuationAt;
            var windowStart = continuationAtStart ?? now.AddMonths(-IssuedFirstRunMonthsBack);
            var queries = 0;

            while (windowStart < now)
            {
                var windowEnd = windowStart.AddMonths(3);
                if (windowEnd > now)
                    windowEnd = now;

                var pageOffset = 0;
                KSeFInvoiceQueryResult result;
                while (true)
                {
                    if (queries++ == MaxIssuedQueriesPerRun)
                    {
                        if (syncState.IssuedContinuationAt == continuationAtStart)
                        {
                            _logger.LogWarning(
                                "Issued invoice sync stalled for tenant {TenantId}: {MaxQueries} queries did not move past {ContinuationAt}",
                                tenant.Id, MaxIssuedQueriesPerRun, continuationAtStart);
                        }

                        return (fetched, created);
                    }

                    result = await _gateway.QueryInvoicesAsync(session, new KSeFQueryCriteria(
                        SubjectNip: tenant.Nip,
                        DateFrom: windowStart,
                        DateTo: windowEnd,
                        PageSize: _syncOptions.BatchSize,
                        PageOffset: pageOffset,
                        Direction: InvoiceDirection.Issued), cancellationToken);

                    var sales = result.Invoices
                        .Where(i => !(i.BuyerIdType == nameof(KSeF.Client.Core.Models.Invoices.BuyerIdentifierType.Nip) && i.BuyerNip == tenant.Nip))
                        .Select(i => ToDto(i, InvoiceDirection.Issued))
                        .ToList();
                    fetched += sales.Count;
                    if (sales.Count > 0)
                        created += (await _invoiceService.UpsertInvoicesAsync(tenant.Id, sales, notify: false)).Count;

                    if (result.IsTruncated || result.Invoices.Count < _syncOptions.BatchSize)
                        break;

                    // Rows are stored first and sorted ascending under the HWM: a restart from here re-reads, never skips.
                    if (result.LastRowAt > windowStart)
                    {
                        syncState.IssuedContinuationAt = result.LastRowAt;
                        await _db.SaveChangesAsync(cancellationToken);
                    }

                    pageOffset++;
                }

                if (result.ContinuationAt is not { } next || next <= windowStart)
                {
                    _logger.LogWarning(
                        "Issued invoice continuation did not advance past {WindowStart} for tenant {TenantId}",
                        windowStart, tenant.Id);
                    return (fetched, created);
                }

                if (next > windowEnd)
                    next = windowEnd;

                syncState.IssuedContinuationAt = next;
                await _db.SaveChangesAsync(cancellationToken);

                if (windowEnd == now && !result.IsTruncated)
                    return (fetched, created);

                windowStart = next;
            }
        }
        catch (Exception ex) when (ex is not DbUpdateException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Issued invoice sync failed for tenant {TenantId}", tenant.Id);
            _db.ChangeTracker.Clear();
        }

        return (fetched, created);
    }

    private static InvoiceDto ToDto(KSeFInvoiceHeader i, InvoiceDirection direction) => new(
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
        Direction: direction,
        BuyerIdType: i.BuyerIdType,
        BuyerForeignId: i.BuyerForeignId);

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
        using var cert = X509CertificateLoader.LoadPkcs12(pfxBytes, null);
        var fingerprint = credential.CertificateFingerprint;

        return await _gateway.InitSignedSessionAsync(
            nip,
            challengeXml => Task.FromResult(SignatureService.Sign(challengeXml, cert)),
            fingerprint,
            ct);
    }
}
