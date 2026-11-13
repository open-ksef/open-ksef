using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using KSeF.Client.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Sync.Issuing;

/// <summary>
/// One send cycle over the IssuedInvoices outbox. Write-ahead: session ref and SendStartedAt are saved before the send,
/// the element ref right after it, so a crash in between is recovered by hash lookup in the session, or by posting the stored bytes again.
/// KSeF calls of one tenant share TenantBudget (2 min) plus CloseCap (10 s) for the session close, so a hung tenant holds others back at most 2 min 10 s;
/// DB writes use the host token so a timed-out tenant still saves its retry.
/// </summary>
public sealed class IssuedInvoiceSendProcessor(
    ApplicationDbContext db,
    IKSeFInvoiceSubmitter submitter,
    TimeProvider time,
    ILogger<IssuedInvoiceSendProcessor> logger)
{
    public const int BatchSize = 50;
    public static readonly TimeSpan TenantBudget = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan CloseCap = TimeSpan.FromSeconds(10);
    public const string UnavailableMessage = "KSeF nie odpowiada, ponowimy wysyłkę";
    public const string RateLimitedMessage = "KSeF ogranicza liczbę żądań, ponowimy wysyłkę";
    public const string NoWritePermissionMessage = "Brak uprawnienia do wystawiania faktur w KSeF. Podmień w ustawieniach firmy token lub certyfikat na taki, który ma uprawnienie do zapisu.";
    public const string EnvironmentChangedMessage = "Środowisko KSeF zmienione";
    public const string InvalidTokenMessage = "KSeF odrzucił token (nieprawidłowy lub unieważniony). Wygeneruj nowy token i podmień go w ustawieniach firmy.";
    public const string InvalidCertificateMessage = "KSeF odrzucił certyfikat (nieprawidłowy, wygasły lub unieważniony). Wgraj nowy certyfikat KSeF w ustawieniach firmy.";
    public const string LoginRefusedMessage = "KSeF odmówił logowania. Przyczynę podaje KSeF w szczegółach.";
    public const string NumberTakenMessage = "Ten numer faktury jest już użyty w KSeF. Sprawdź tę fakturę w KSeF, zanim wystawisz ją pod nowym numerem.";
    public const string DuplicateUnresolvedMessage = "Nie można jeszcze potwierdzić wyniku wysyłki w KSeF. Wcześniejsza próba mogła zostać przyjęta, więc faktura czeka na wyjaśnienie i sprawdzimy ją ponownie.";
    public const string MissingTokenMessage = "Wysyłka faktur wymaga tokenu lub certyfikatu KSeF w ustawieniach firmy";
    public const string MissingXmlMessage = "Brak zapisanego XML faktury";
    public const string UpoPendingMessage = "UPO jeszcze niedostępne, ponowimy pobranie";

    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan UpoRetry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinPoll = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaxPoll = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LoginCap = TimeSpan.FromSeconds(70);

    public async Task ProcessAsync(string currentEnvironment, CancellationToken ct)
    {
        var now = Now();
        var due = await db.IssuedInvoices
            .Where(i => (i.Status == IssuedInvoiceStatus.Queued || i.Status == IssuedInvoiceStatus.Sending
                         || (i.Status == IssuedInvoiceStatus.Accepted && i.UpoXml == null))
                        && i.NextAttemptAt != null && i.NextAttemptAt <= now)
            .OrderBy(i => i.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var batch in due.GroupBy(i => i.TenantId))
        {
            using var budget = new CancellationTokenSource(TenantBudget, time);
            using var ksef = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
            try
            {
                await ProcessTenantAsync(batch.Key, batch.ToList(), DependencyInjection.NormalizeKSeFEnvironmentKey(currentEnvironment), ksef.Token, ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                logger.LogError(e, "Issued invoice send for tenant {TenantId} failed", batch.Key);
                foreach (var invoice in batch)
                    db.Entry(invoice).State = EntityState.Detached;
            }
        }
    }

    private async Task ProcessTenantAsync(Guid tenantId, List<IssuedInvoice> invoices, string environment, CancellationToken ksef, CancellationToken ct)
    {
        foreach (var invoice in invoices.Where(i => DependencyInjection.NormalizeKSeFEnvironmentKey(i.KsefEnvironment) != environment).ToList())
        {
            invoices.Remove(invoice);
            if (MayExistInKSeF(invoice))
                invoice.ScheduleRetry(EnvironmentChangedMessage, Now().AddDays(1), Now());
            else
                invoice.MarkRejected(null, EnvironmentChangedMessage, null, Now());
            await db.SaveChangesAsync(ct);
        }

        if (invoices.Count == 0)
            return;

        var tenant = await db.Tenants.Include(t => t.KSeFCredentials).SingleAsync(t => t.Id == tenantId, ct);
        var credential = tenant.KSeFCredentials.FirstOrDefault(c => c.Type == CredentialType.Certificate ? c.EncryptedCertificateData != null : c.EncryptedToken != null);
        if (credential is null)
        {
            await RejectSendableAsync(invoices, null, MissingTokenMessage, null, ct);
            return;
        }

        string accessToken;
        try
        {
            using var loginTimeout = new CancellationTokenSource(LoginCap, time);
            using var loginCap = CancellationTokenSource.CreateLinkedTokenSource(ksef, loginTimeout.Token);
            accessToken = await submitter.LoginAsync(tenant.Nip, credential, loginCap.Token);
        }
        catch (KSeFAuthRejectedException e)
        {
            logger.LogWarning("KSeF rejected the {CredentialType} of tenant {TenantId}: {Code} {Reason}", credential.Type, tenantId, e.Code, e.Message);
            await RejectSendableAsync(invoices, e.Code, AuthRejectedMessage(credential.Type, e), e.Message, ct);
            return;
        }
        catch (StoredCertificateUnreadableException e)
        {
            logger.LogWarning("Stored certificate of tenant {TenantId} cannot be read: {Error}", tenantId, e.Message);
            await RejectSendableAsync(invoices, null, InvalidCertificateMessage, null, ct);
            return;
        }
        catch (Exception e) when (IsTransient(e, ct))
        {
            await RetryAllAsync(invoices, e, ct);
            return;
        }
        catch (KsefApiException e)
        {
            var (code, description, details) = Describe(e);
            await RejectSendableAsync(invoices, code, description, details, ct);
            return;
        }

        KSeFOnlineSession? session = null;
        Func<Task<KSeFOnlineSession>> openSession = async () => session ??= await submitter.OpenSessionAsync(accessToken, ksef);
        try
        {
            for (var index = 0; index < invoices.Count && !ksef.IsCancellationRequested; index++)
            {
                var invoice = invoices[index];
                try
                {
                    await ProcessInvoiceAsync(invoice, accessToken, openSession, ksef, ct);
                }
                catch (Exception e) when (IsTransient(e, ct))
                {
                    await RetryAllAsync(ksef.IsCancellationRequested ? [invoice] : invoices.Skip(index), e, ct);
                    return;
                }
                catch (KsefApiException e)
                {
                    logger.LogWarning("KSeF status check for issued invoice {InvoiceId} failed: {Error}", invoice.Id, e.Message);
                    invoice.ScheduleRetry(Describe(e).Description, Now().Add(MaxBackoff), Now());
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    logger.LogWarning("Issued invoice {InvoiceId} changed concurrently, skipped this cycle", invoice.Id);
                    db.Entry(invoice).State = EntityState.Detached;
                }
            }
        }
        finally
        {
            if (session is not null)
                await CloseQuietlyAsync(session);
        }
    }

    private async Task ProcessInvoiceAsync(IssuedInvoice invoice, string accessToken, Func<Task<KSeFOnlineSession>> openSession, CancellationToken ksef, CancellationToken ct)
    {
        if (invoice.Status == IssuedInvoiceStatus.Accepted)
        {
            await FetchUpoAsync(invoice, accessToken, ksef, ct);
            return;
        }

        if (invoice.SentXml is null)
        {
            invoice.MarkRejected(null, MissingXmlMessage, null, Now());
            await db.SaveChangesAsync(ct);
            return;
        }

        if (invoice.KsefElementRef is not null)
        {
            await ApplyStatusAsync(invoice, await submitter.GetInvoiceAsync(accessToken, invoice.KsefSessionRef!, invoice.KsefElementRef, ksef), accessToken, ksef, ct);
            return;
        }

        if (invoice.SendStartedAt is not null && invoice.KsefSessionRef is not null)
        {
            var hash = Convert.ToBase64String(SHA256.HashData(invoice.SentXml));
            var match = (await ListOwnSessionAsync(invoice, accessToken, ksef)).FirstOrDefault(s => s.InvoiceHash == hash && s.Code < 400);
            if (match is not null)
            {
                logger.LogInformation("Issued invoice {InvoiceId} recovered from session {SessionRef}", invoice.Id, invoice.KsefSessionRef);
                invoice.RecordElementRef(match.ElementRef, Now());
                await db.SaveChangesAsync(ct);
                await ApplyStatusAsync(invoice, match, accessToken, ksef, ct);
                return;
            }
        }

        var alreadyStarted = invoice.SendStartedAt is not null;
        KSeFOnlineSession session;
        string elementRef;
        try
        {
            session = await openSession();
            invoice.MarkSendStarted(session.ReferenceNumber, Now());
            await db.SaveChangesAsync(ct);
            elementRef = await submitter.SendAsync(session, invoice.SentXml, ksef);
        }
        catch (KsefApiException e) when (!IsTransient(e, ct))
        {
            var (code, description, details) = Describe(e);
            if (alreadyStarted)
                invoice.ScheduleRetry(description, Now().Add(MaxBackoff), Now());
            else
                invoice.MarkRejected(code, description, details, Now());
            await db.SaveChangesAsync(ct);
            return;
        }

        invoice.RecordElementRef(elementRef, Now());
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Issued invoice {InvoiceId} sent in session {SessionRef} as {ElementRef}", invoice.Id, session.ReferenceNumber, elementRef);
    }

    private async Task ApplyStatusAsync(IssuedInvoice invoice, KSeFSentInvoice? status, string accessToken, CancellationToken ksef, CancellationToken ct)
    {
        if (status is { Code: KSeFSentInvoice.Accepted, KsefNumber: not null })
        {
            invoice.MarkAccepted(status.KsefNumber, (status.AcceptedAt ?? time.GetUtcNow()).UtcDateTime, status.Code, status.Description, Now());
            await db.SaveChangesAsync(ct);
            await FetchUpoAsync(invoice, accessToken, ksef, ct);
            return;
        }

        if (status is { Code: KSeFSentInvoice.Duplicate })
        {
            await ResolveDuplicateAsync(invoice, status, accessToken, ksef, ct);
            return;
        }

        if (status is { Code: >= 400 and not (430 or 450) } && invoice.AttemptCount > 1)
        {
            var nextAttemptAt = Now().Add(ResendDelay(invoice));
            logger.LogWarning(
                "Issued invoice {InvoiceId} failed with status {Code} ({Description}) in session {SessionRef}, element {ElementRef}; next post at {NextAttemptAt}",
                invoice.Id, status.Code, status.Description, invoice.KsefSessionRef, invoice.KsefElementRef, nextAttemptAt);
            invoice.ScheduleResend(DuplicateUnresolvedMessage, nextAttemptAt, Now());
            await db.SaveChangesAsync(ct);
            return;
        }

        if (status is { Code: >= 400 })
        {
            invoice.MarkRejected(status.Code, status.Description ?? $"KSeF {status.Code}", Join(status.Details), Now());
            await db.SaveChangesAsync(ct);
            return;
        }

        invoice.ScheduleRetry(null, Now().Add(PollDelay(invoice)), Now());
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A 440 means the number exists in KSeF, either as our own earlier attempt whose response was lost, or as another document.
    /// AttemptCount is saved before each POST, so with one attempt no earlier copy of ours can exist.
    /// </summary>
    private async Task ResolveDuplicateAsync(IssuedInvoice invoice, KSeFSentInvoice duplicate, string accessToken, CancellationToken ksef, CancellationToken ct)
    {
        var listed = duplicate.OriginalSessionRef is null ? [] : await ListOriginalSessionAsync(invoice, accessToken, duplicate.OriginalSessionRef, ksef, ct);
        var hash = Convert.ToBase64String(SHA256.HashData(invoice.SentXml!));
        var original = listed.FirstOrDefault(s => s is { Code: KSeFSentInvoice.Accepted, KsefNumber: not null } && s.InvoiceHash == hash);
        if (original is not null)
        {
            logger.LogInformation("Issued invoice {InvoiceId} is already in KSeF, recovered from session {SessionRef}", invoice.Id, duplicate.OriginalSessionRef);
            invoice.MarkSendStarted(duplicate.OriginalSessionRef!, Now());
            invoice.RecordElementRef(original.ElementRef, Now());
            await db.SaveChangesAsync(ct);
            await ApplyStatusAsync(invoice, original, accessToken, ksef, ct);
            return;
        }

        var otherDocumentListed = duplicate.OriginalKsefNumber is not null && listed.Any(s => s.KsefNumber == duplicate.OriginalKsefNumber);
        if (!otherDocumentListed && invoice.AttemptCount > 1)
        {
            await WaitForUnresolvedAsync(invoice, ct);
            return;
        }

        invoice.MarkRejected(duplicate.Code, NumberTakenMessage, DuplicateDetails(duplicate), Now());
        await db.SaveChangesAsync(ct);
    }

    private async Task WaitForUnresolvedAsync(IssuedInvoice invoice, CancellationToken ct)
    {
        invoice.ScheduleRetry(DuplicateUnresolvedMessage, Now().Add(MaxBackoff), Now());
        await db.SaveChangesAsync(ct);
    }

    private static string? DuplicateDetails(KSeFSentInvoice duplicate)
    {
        var details = Join(duplicate.Details);
        return duplicate.OriginalKsefNumber is not { } number || details?.Contains(number, StringComparison.Ordinal) == true
            ? details
            : Join([.. duplicate.Details, $"Numer KSeF: {number}"]);
    }

    private async Task<IReadOnlyList<KSeFSentInvoice>> ListOwnSessionAsync(IssuedInvoice invoice, string accessToken, CancellationToken ksef)
    {
        try
        {
            return await submitter.ListInvoicesAsync(accessToken, invoice.KsefSessionRef!, ksef);
        }
        catch (KsefApiException e) when (e.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            logger.LogWarning("KSeF session {SessionRef} of issued invoice {InvoiceId} cannot be listed: {Error}", invoice.KsefSessionRef, invoice.Id, e.Message);
            return [];
        }
    }

    private async Task<IReadOnlyList<KSeFSentInvoice>> ListOriginalSessionAsync(IssuedInvoice invoice, string accessToken, string sessionRef, CancellationToken ksef, CancellationToken ct)
    {
        try
        {
            return await submitter.ListInvoicesAsync(accessToken, sessionRef, ksef);
        }
        catch (KsefApiException e) when (e.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound || (invoice.AttemptCount == 1 && !IsTransient(e, ct)))
        {
            logger.LogInformation("Original KSeF session {SessionRef} cannot be listed: {Error}", sessionRef, e.Message);
            return [];
        }
    }

    private TimeSpan PollDelay(IssuedInvoice invoice) =>
        TimeSpan.FromTicks(Math.Clamp((Now() - invoice.SendStartedAt!.Value).Ticks / 4, MinPoll.Ticks, MaxPoll.Ticks));

    private async Task FetchUpoAsync(IssuedInvoice invoice, string accessToken, CancellationToken ksef, CancellationToken ct)
    {
        var upo = await submitter.GetUpoAsync(accessToken, invoice.KsefSessionRef!, invoice.KsefElementRef!, ksef);
        if (upo is null)
            invoice.ScheduleRetry(UpoPendingMessage, Now().Add(UpoRetry), Now());
        else
            invoice.AttachUpo(upo, Now());
        await db.SaveChangesAsync(ct);
    }

    private async Task RejectSendableAsync(IEnumerable<IssuedInvoice> invoices, int? code, string description, string? details, CancellationToken ct)
    {
        foreach (var invoice in invoices)
        {
            if (MayExistInKSeF(invoice))
                invoice.ScheduleRetry(details is null ? description : $"{description} {details}", Now().Add(MaxBackoff), Now());
            else
                invoice.MarkRejected(code, description, details, Now());
            await db.SaveChangesAsync(ct);
        }
    }

    private static readonly string[] TokenFaults = ["Nieprawidłowy token", "Token unieważniony", "Token nieaktywny", "Token nie może być użyty", "nie został znaleziony"];

    private static string AuthRejectedMessage(CredentialType type, KSeFAuthRejectedException e) => (type, e.Code) switch
    {
        (CredentialType.Certificate, 460) => InvalidCertificateMessage,
        (CredentialType.Token, 450) when TokenFaults.Any(e.Message.Contains) => InvalidTokenMessage,
        (_, 415) => NoWritePermissionMessage,
        _ => LoginRefusedMessage,
    };

    private static bool MayExistInKSeF(IssuedInvoice invoice) =>
        invoice.Status == IssuedInvoiceStatus.Accepted || invoice.SendStartedAt is not null;

    private async Task RetryAllAsync(IEnumerable<IssuedInvoice> invoices, Exception error, CancellationToken ct)
    {
        logger.LogWarning("KSeF unavailable ({Error}), send postponed", error.GetType().Name + ": " + error.Message);
        foreach (var invoice in invoices)
        {
            if (error is KsefRateLimitException limit)
                invoice.ScheduleRetry(RateLimitedMessage, Now().Add(limit.RecommendedDelay), Now());
            else
                invoice.ScheduleRetry(UnavailableMessage, Now().Add(Backoff(invoice)), Now());
            await db.SaveChangesAsync(ct);
        }
    }

    private static TimeSpan ResendDelay(IssuedInvoice invoice) =>
        MaxBackoff * Math.Pow(2, Math.Clamp(invoice.AttemptCount - 2, 0, 4));

    internal static TimeSpan Backoff(IssuedInvoice invoice)
    {
        if (invoice.LastError != UnavailableMessage || invoice.NextAttemptAt is null)
            return MinBackoff;
        var previous = invoice.NextAttemptAt.Value - invoice.UpdatedAt;
        var next = previous < MinBackoff ? MinBackoff : previous * 2;
        return next > MaxBackoff ? MaxBackoff : next;
    }

    private static bool IsTransient(Exception e, CancellationToken ct) => e switch
    {
        KsefRateLimitException => true,
        KsefApiException api => (int)api.StatusCode >= 500 || api.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.RequestTimeout or 0,
        HttpRequestException or SocketException or TimeoutException => true,
        OperationCanceledException => !ct.IsCancellationRequested,
        _ => false
    };

    private static (int? Code, string Description, string? Details) Describe(KsefApiException e)
    {
        if (e.StatusCode == HttpStatusCode.Forbidden && e.Message.StartsWith("missing-permissions", StringComparison.Ordinal))
            return ((int)e.StatusCode, NoWritePermissionMessage, e.Message);

        var detail = e.ErrorResponse?.Exception?.ExceptionDetailList?.FirstOrDefault();
        return (detail?.ExceptionCode ?? (int)e.StatusCode, detail?.ExceptionDescription ?? e.Message, Join(detail?.Details));
    }

    private static string? Join(IEnumerable<string>? details) =>
        details is null ? null : string.Join("; ", details) is { Length: > 0 } joined ? joined : null;

    private async Task CloseQuietlyAsync(KSeFOnlineSession session)
    {
        using var closeCap = new CancellationTokenSource(CloseCap, time);
        try
        {
            await submitter.CloseSessionAsync(session, closeCap.Token);
        }
        catch (Exception e) when (e is KsefApiException or HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning("Closing KSeF session {SessionRef} failed: {Error}", session.ReferenceNumber, e.Message);
        }
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
