using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public class CredentialsSummaryController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CredentialsSummaryController(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet("credentials")]
    [ProducesResponseType(typeof(List<TenantCredentialStatusResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCredentials()
    {
        var userId = _currentUser.UserId;
        var tenants = await _db.Tenants
            .Where(t => t.UserId == userId)
            .Select(t => new
            {
                t.Id,
                t.DisplayName,
                Credential = _db.KSeFCredentials
                    .Where(c => c.TenantId == t.Id)
                    .Select(c => new { c.Type, c.UpdatedAt })
                    .FirstOrDefault()
            })
            .ToListAsync();

        var result = tenants.Select(t => new TenantCredentialStatusResponse(
            t.Id,
            t.DisplayName ?? t.Id.ToString(),
            t.Credential != null,
            t.Credential?.Type,
            t.Credential?.UpdatedAt)).ToList();

        return Ok(result);
    }
}

[ApiController]
[Route("api/tenants/{tenantId:guid}/credentials")]
[Authorize]
public class CredentialsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IEncryptionService _encryption;
    private readonly ManualSyncJobs _syncJobs;
    private readonly ILogger<CredentialsController> _logger;

    public const string ManualSyncRateLimitPolicy = "manual-sync";
    public const string MissingPrivateKeyMessage = "Plik nie zawiera klucza prywatnego. Wgraj plik .pfx/.p12 z kluczem albo parę .crt + .key.";
    public const string ExpiredCertificateMessage = "Certyfikat wygasł. Wygeneruj nowy w Aplikacji Podatnika KSeF i wgraj go.";
    public const string UnreadableCertificateMessage = "Nie udało się odczytać certyfikatu. Sprawdź hasło i czy wybrany plik jest właściwy.";
    public const string MissingCertificatePasswordMessage = "Podaj hasło do certyfikatu.";
    public const string MissingCertificateFileMessage = "Wybierz plik certyfikatu.";
    public const string MissingTokenMessage = "Podaj token KSeF.";
    public const string UnknownCredentialTypeMessage = "Nieznany sposób logowania do KSeF.";
    public const string EncryptionKeyUnavailableMessage = "Serwer nie ma prawidłowego klucza szyfrowania, więc nie może zapisać ani odczytać poświadczeń KSeF. Skontaktuj się z administratorem serwera.";
    public const string KeyKdfTooExpensiveMessage = "Nie można odczytać klucza prywatnego (.key): plik jest zaszyfrowany w nieobsługiwany sposób. Wgraj certyfikat jako plik .pfx/.p12.";
    public const int MaxKeyKdfIterations = 600_000;
    public const int MaxRequestBytes = 64 * 1024;
    private const string Pbes2Oid = "1.2.840.113549.1.5.13";

    public CredentialsController(
        ApplicationDbContext db,
        ICurrentUserService currentUser,
        IEncryptionService encryption,
        ManualSyncJobs syncJobs,
        ILogger<CredentialsController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _encryption = encryption;
        _syncJobs = syncJobs;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> AddOrUpdate(Guid tenantId, [FromBody] AddCredentialRequest request)
    {
        if (!await _db.OwnsTenantAsync(tenantId, _currentUser.UserId))
            return Forbid();

        var now = DateTime.UtcNow;

        string? encryptedToken = null;
        string? encryptedCertData = null;
        string? fingerprint = null;

        switch (request.Type)
        {
            case CredentialType.Token:
                if (string.IsNullOrWhiteSpace(request.Token))
                    return BadRequest(new { error = MissingTokenMessage });
                encryptedToken = _encryption.Encrypt(request.Token);
                break;

            case CredentialType.Certificate:
                try
                {
                    var (cert, certBase64ForStorage) = LoadCertificate(request);
                    using (cert)
                    {
                        fingerprint = ComputeSha256Fingerprint(cert);
                        encryptedCertData = _encryption.Encrypt(certBase64ForStorage);
                    }
                }
                catch (ArgumentException ex)
                {
                    return BadRequest(new { error = ex.Message });
                }
                catch (CryptographicException)
                {
                    return BadRequest(new { error = UnreadableCertificateMessage });
                }
                catch (Exception ex) when (ex is FormatException or AsnContentException)
                {
                    return BadRequest(new { error = UnreadableCertificateMessage });
                }
                break;

            default:
                return BadRequest(new { error = UnknownCredentialTypeMessage });
        }

        var existing = await _db.KSeFCredentials
            .FirstOrDefaultAsync(c => c.TenantId == tenantId);

        if (existing is null)
        {
            _db.KSeFCredentials.Add(new KSeFCredential
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Type = request.Type,
                EncryptedToken = encryptedToken,
                EncryptedCertificateData = encryptedCertData,
                CertificateFingerprint = fingerprint,
                CreatedAt = now,
                UpdatedAt = now
            });

            try
            {
                await _db.SaveChangesAsync();
                _logger.LogInformation("KSeF credential created for tenant {TenantId} (type: {Type})", tenantId, request.Type);
                return Ok(new { message = "Credential saved." });
            }
            catch (DbUpdateException e) when (IssuedInvoiceService.IsUniqueViolation(e))
            {
                _db.ChangeTracker.Clear();
                existing = await _db.KSeFCredentials.FirstAsync(c => c.TenantId == tenantId);
            }
        }

        existing.Type = request.Type;
        existing.EncryptedToken = encryptedToken;
        existing.EncryptedCertificateData = encryptedCertData;
        existing.CertificateFingerprint = fingerprint;
        existing.UpdatedAt = now;
        await _db.SaveChangesAsync();
        _logger.LogInformation("KSeF credential updated for tenant {TenantId} (type: {Type})", tenantId, request.Type);
        return Ok(new { message = "Credential saved." });
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid tenantId)
    {
        if (!await _db.OwnsTenantAsync(tenantId, _currentUser.UserId))
            return Forbid();

        var existing = await _db.KSeFCredentials
            .FirstOrDefaultAsync(c => c.TenantId == tenantId);

        if (existing is null)
            return NotFound();

        _db.KSeFCredentials.Remove(existing);
        await _db.SaveChangesAsync();

        _logger.LogInformation("KSeF credential deleted for tenant {TenantId}", tenantId);
        return NoContent();
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(CredentialStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Status(Guid tenantId)
    {
        if (!await _db.OwnsTenantAsync(tenantId, _currentUser.UserId))
            return Forbid();

        var credential = await _db.KSeFCredentials
            .Where(c => c.TenantId == tenantId)
            .Select(c => new { c.Type, c.UpdatedAt, c.EncryptedCertificateData })
            .FirstOrDefaultAsync();

        return Ok(new CredentialStatusResponse(
            Exists: credential is not null,
            CredentialType: credential?.Type,
            UpdatedAt: credential?.UpdatedAt,
            CertificateValidTo: CertificateValidTo(credential?.Type == CredentialType.Certificate ? credential.EncryptedCertificateData : null)));
    }

    private DateTime? CertificateValidTo(string? encryptedCertificateData)
    {
        if (encryptedCertificateData is null)
            return null;

        var pkcs12 = Convert.FromBase64String(_encryption.Decrypt(encryptedCertificateData));
        try
        {
            using var cert = X509CertificateLoader.LoadPkcs12(pkcs12, null, X509KeyStorageFlags.EphemeralKeySet, new Pkcs12LoaderLimits { IgnorePrivateKeys = true });
            return cert.NotAfter.ToUniversalTime();
        }
        catch (CryptographicException)
        {
            // Older PFX uploads were stored password-protected and cannot be read.
            return null;
        }
    }

    [HttpPost("sync")]
    [EnableRateLimiting(ManualSyncRateLimitPolicy)]
    [ProducesResponseType(typeof(TenantManualSyncResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TenantSyncStatusResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> SyncNow(Guid tenantId, CancellationToken cancellationToken) =>
        StartSync(tenantId, fullResync: false, cancellationToken);

    [HttpPost("full-resync")]
    [EnableRateLimiting(ManualSyncRateLimitPolicy)]
    [ProducesResponseType(typeof(TenantManualSyncResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TenantSyncStatusResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> FullResync(Guid tenantId, CancellationToken cancellationToken) =>
        StartSync(tenantId, fullResync: true, cancellationToken);

    [HttpGet("sync")]
    [ProducesResponseType(typeof(TenantSyncStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SyncStatus(Guid tenantId)
    {
        if (!await _db.OwnsTenantAsync(tenantId, _currentUser.UserId))
            return Forbid();

        return Ok(await StatusAsync(tenantId, _syncJobs.Find(tenantId)));
    }

    private async Task<IActionResult> StartSync(Guid tenantId, bool fullResync, CancellationToken cancellationToken)
    {
        if (!await _db.OwnsTenantAsync(tenantId, _currentUser.UserId))
            return Forbid();

        if (!await _db.KSeFCredentials.AnyAsync(c => c.TenantId == tenantId, cancellationToken))
            return Conflict(new { error = TenantSyncService.MissingCredentialMessage });

        var job = _syncJobs.Start(tenantId, _currentUser.UserId, fullResync);
        var statusUrl = $"/api/tenants/{tenantId}/credentials/sync";
        if (Request.Headers["Prefer"].Any(p => p?.Contains("respond-async", StringComparison.OrdinalIgnoreCase) == true))
            return Accepted(statusUrl, await StatusAsync(tenantId, job));

        // Mobile v1.0.7 sends no Prefer: an empty 202 shows its existing "try later" text while the job finishes.
        var result = await _syncJobs.WaitAsync(job, cancellationToken);
        return result is null ? Accepted(statusUrl) : MapSyncResult(result);
    }

    private async Task<TenantSyncStatusResponse> StatusAsync(Guid tenantId, ManualSyncJobs.Job? job)
    {
        var lastSuccessfulSync = await _db.SyncStates
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.LastSuccessfulSync)
            .FirstOrDefaultAsync();

        if (job is null)
            return new TenantSyncStatusResponse(tenantId, "idle", false, null, null, null, null, null, lastSuccessfulSync);

        if (!job.Completion.IsCompleted)
            return new TenantSyncStatusResponse(tenantId, job.Running ? "running" : "waiting", job.FullResync, job.StartedAtUtc, null, null, null, null, lastSuccessfulSync);

        var result = job.Completion.Result;
        return result.Outcome == TenantSyncOutcome.Success
            ? new TenantSyncStatusResponse(tenantId, "succeeded", job.FullResync, job.StartedAtUtc, job.FinishedAtUtc, ToResponse(result), null, null, lastSuccessfulSync)
            : new TenantSyncStatusResponse(tenantId, "failed", job.FullResync, job.StartedAtUtc, job.FinishedAtUtc, null, ErrorStatus(result.Outcome), result.ErrorMessage, lastSuccessfulSync);
    }

    private static int ErrorStatus(TenantSyncOutcome outcome) => outcome switch
    {
        TenantSyncOutcome.TenantNotFound => StatusCodes.Status404NotFound,
        TenantSyncOutcome.MissingCredential => StatusCodes.Status409Conflict,
        TenantSyncOutcome.Failed => StatusCodes.Status502BadGateway,
        TenantSyncOutcome.Busy => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    private IActionResult MapSyncResult(TenantSyncResult result) => result.Outcome switch
    {
        TenantSyncOutcome.Success => Ok(ToResponse(result)),
        TenantSyncOutcome.TenantNotFound => NotFound(),
        TenantSyncOutcome.StoreFailed or TenantSyncOutcome.MissingCredential or TenantSyncOutcome.Failed or TenantSyncOutcome.Busy =>
            StatusCode(ErrorStatus(result.Outcome), new { error = result.ErrorMessage }),
        _ => StatusCode(StatusCodes.Status500InternalServerError, new { error = "Unknown sync outcome." })
    };

    private static TenantManualSyncResponse ToResponse(TenantSyncResult result) => new(
        TenantId: result.TenantId,
        FetchedInvoices: result.FetchedInvoices,
        NewInvoices: result.NewInvoices,
        SyncedAtUtc: result.SyncedAtUtc ?? DateTime.UtcNow,
        FailedDownloads: result.FailedDownloads,
        QueuedDownloads: result.QueuedDownloads,
        ErroredDownloads: result.ErroredDownloads,
        IssuedFetchedInvoices: result.IssuedFetchedInvoices,
        IssuedNewInvoices: result.IssuedNewInvoices);

    private static (X509Certificate2 cert, string base64ForStorage) LoadCertificate(AddCredentialRequest request)
    {
        bool hasCertPem = !string.IsNullOrWhiteSpace(request.CertificatePem);
        bool hasPem = hasCertPem && !string.IsNullOrWhiteSpace(request.PrivateKeyPem);
        bool hasPfx = !string.IsNullOrWhiteSpace(request.CertificateBase64);

        if (!hasPem && !hasPfx)
            throw new ArgumentException(hasCertPem ? MissingPrivateKeyMessage : MissingCertificateFileMessage);

        var cert = hasPem
            ? LoadFromPem(request.CertificatePem!, request.PrivateKeyPem!, request.CertificatePassword)
            : LoadFromPfx(request.CertificateBase64!, request.CertificatePassword);
        try
        {
            if (!cert.HasPrivateKey)
                throw new ArgumentException(MissingPrivateKeyMessage);
            if (cert.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
                throw new ArgumentException(ExpiredCertificateMessage);

            return (cert, Convert.ToBase64String(cert.Export(X509ContentType.Pfx)));
        }
        catch
        {
            cert.Dispose();
            throw;
        }
    }

    private static X509Certificate2 LoadFromPem(string certPem, string keyPem, string? password)
    {
        if (!keyPem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal))
            return X509Certificate2.CreateFromPem(certPem, keyPem);

        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException(MissingCertificatePasswordMessage);

        EnsureKdfIterationsWithinCap(keyPem);
        return X509Certificate2.CreateFromEncryptedPem(certPem, keyPem, password);
    }

    private static void EnsureKdfIterationsWithinCap(string keyPem)
    {
        if (!PemEncoding.TryFind(keyPem, out var pem))
            throw new CryptographicException();

        var algorithm = new AsnReader(Convert.FromBase64String(keyPem[pem.Base64Data]), AsnEncodingRules.BER).ReadSequence().ReadSequence();
        AsnReader kdfParameters;
        if (algorithm.ReadObjectIdentifier() == Pbes2Oid)
        {
            var kdf = algorithm.ReadSequence().ReadSequence();
            kdf.ReadObjectIdentifier();
            kdfParameters = kdf.ReadSequence();
        }
        else
            kdfParameters = algorithm.ReadSequence();

        kdfParameters.ReadOctetString();
        if (!kdfParameters.TryReadInt32(out var iterations) || iterations > MaxKeyKdfIterations)
            throw new ArgumentException(KeyKdfTooExpensiveMessage);
    }

    private static X509Certificate2 LoadFromPfx(string certificateBase64, string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException(MissingCertificatePasswordMessage);

        return X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(certificateBase64), password, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
    }

    private static string ComputeSha256Fingerprint(X509Certificate2 cert)
    {
        var hashBytes = SHA256.HashData(cert.RawData);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
