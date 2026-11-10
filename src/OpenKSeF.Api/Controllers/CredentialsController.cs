using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Models;
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
    private readonly ITenantSyncService _tenantSyncService;
    private readonly ISystemConfigService _systemConfig;
    private readonly ILogger<CredentialsController> _logger;
    private readonly bool _onRowKey;

    public const string MissingPrivateKeyMessage = "Plik nie zawiera klucza prywatnego. Wgraj plik .pfx/.p12 z kluczem albo parę .crt + .key.";
    public const string ExpiredCertificateMessage = "Certyfikat wygasł. Wygeneruj nowy w Aplikacji Podatnika KSeF i wgraj go.";
    public const string UnreadableCertificateMessage = "Nie udało się odczytać certyfikatu. Sprawdź hasło i czy wybrany plik jest właściwy.";
    public const string MissingCertificatePasswordMessage = "Podaj hasło do certyfikatu.";

    public CredentialsController(
        ApplicationDbContext db,
        ICurrentUserService currentUser,
        IEncryptionService encryption,
        ITenantSyncService tenantSyncService,
        ISystemConfigService systemConfig,
        ILogger<CredentialsController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _encryption = encryption;
        _tenantSyncService = tenantSyncService;
        _systemConfig = systemConfig;
        _logger = logger;
        // Read here, not in the action: binding awaits the request body after _encryption is resolved.
        _onRowKey = systemConfig.GetValue(SystemConfigKeys.EncryptionKey) is not null;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> AddOrUpdate(Guid tenantId, [FromBody] AddCredentialRequest request)
    {
        if (!await VerifyTenantOwnership(tenantId))
            return Forbid();

        var now = DateTime.UtcNow;

        string? encryptedToken = null;
        string? encryptedCertData = null;
        string? fingerprint = null;

        switch (request.Type)
        {
            case CredentialType.Token:
                if (string.IsNullOrWhiteSpace(request.Token))
                    return BadRequest(new { error = "Token is required for token-based credentials." });
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
                catch (FormatException)
                {
                    return BadRequest(new { error = UnreadableCertificateMessage });
                }
                break;

            default:
                return BadRequest(new { error = $"Unknown credential type: {request.Type}" });
        }

        await using var transaction = _onRowKey && _db.Database.IsNpgsql() ? await _db.Database.BeginTransactionAsync() : null;
        if (_onRowKey && await EncryptionKeyMigration.ReadKeyRowLockedAsync(_db, forUpdate: false) is null)
        {
            await _systemConfig.RefreshCacheAsync();
            _logger.LogCritical("This host still held the encryption key from the database after it moved out, credential not saved for tenant {TenantId}", tenantId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Encryption key changed. Try again." });
        }

        var existing = await _db.KSeFCredentials
            .FirstOrDefaultAsync(c => c.TenantId == tenantId);

        if (existing is not null)
        {
            existing.Type = request.Type;
            existing.EncryptedToken = encryptedToken;
            existing.EncryptedCertificateData = encryptedCertData;
            existing.CertificateFingerprint = fingerprint;
            existing.UpdatedAt = now;
            _logger.LogInformation("KSeF credential updated for tenant {TenantId} (type: {Type})", tenantId, request.Type);
        }
        else
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
            _logger.LogInformation("KSeF credential created for tenant {TenantId} (type: {Type})", tenantId, request.Type);
        }

        await _db.SaveChangesAsync();
        if (transaction is not null)
            await transaction.CommitAsync();
        return Ok(new { message = "Credential saved." });
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid tenantId)
    {
        if (!await VerifyTenantOwnership(tenantId))
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
    public async Task<IActionResult> Status(Guid tenantId)
    {
        if (!await VerifyTenantOwnership(tenantId))
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

        try
        {
            using var cert = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(_encryption.Decrypt(encryptedCertificateData)), null);
            return cert.NotAfter.ToUniversalTime();
        }
        catch (CryptographicException)
        {
            // Older PFX uploads were stored password-protected and cannot be read.
            return null;
        }
    }

    [HttpPost("sync")]
    [ProducesResponseType(typeof(TenantManualSyncResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SyncNow(Guid tenantId, CancellationToken cancellationToken)
    {
        var result = await _tenantSyncService.SyncTenantAsync(
            tenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return MapSyncResult(result);
    }

    [HttpPost("full-resync")]
    [ProducesResponseType(typeof(TenantManualSyncResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> FullResync(Guid tenantId, CancellationToken cancellationToken)
    {
        var result = await _tenantSyncService.SyncTenantAsync(
            tenantId,
            _currentUser.UserId,
            forceFullResync: true,
            cancellationToken: cancellationToken);

        return MapSyncResult(result);
    }

    private IActionResult MapSyncResult(TenantSyncResult result) => result.Outcome switch
    {
        TenantSyncOutcome.TenantNotFound => NotFound(),
        TenantSyncOutcome.MissingCredential => Conflict(new { error = result.ErrorMessage }),
        TenantSyncOutcome.Failed => StatusCode(StatusCodes.Status502BadGateway, new { error = result.ErrorMessage }),
        TenantSyncOutcome.StoreFailed => StatusCode(StatusCodes.Status500InternalServerError, new { error = result.ErrorMessage }),
        TenantSyncOutcome.Success => Ok(new TenantManualSyncResponse(
            TenantId: result.TenantId,
            FetchedInvoices: result.FetchedInvoices,
            NewInvoices: result.NewInvoices,
            SyncedAtUtc: result.SyncedAtUtc ?? DateTime.UtcNow,
            FailedDownloads: result.FailedDownloads,
            QueuedDownloads: result.QueuedDownloads,
            ErroredDownloads: result.ErroredDownloads,
            IssuedFetchedInvoices: result.IssuedFetchedInvoices,
            IssuedNewInvoices: result.IssuedNewInvoices)),
        _ => StatusCode(StatusCodes.Status500InternalServerError, new { error = "Unknown sync outcome." })
    };

    private async Task<bool> VerifyTenantOwnership(Guid tenantId)
    {
        return await _db.Tenants
            .AnyAsync(t => t.Id == tenantId && t.UserId == _currentUser.UserId);
    }

    private static (X509Certificate2 cert, string base64ForStorage) LoadCertificate(AddCredentialRequest request)
    {
        bool hasPem = !string.IsNullOrWhiteSpace(request.CertificatePem) &&
                      !string.IsNullOrWhiteSpace(request.PrivateKeyPem);
        bool hasPfx = !string.IsNullOrWhiteSpace(request.CertificateBase64);

        if (!hasPem && !hasPfx)
            throw new ArgumentException("Provide CertificatePem + PrivateKeyPem, or CertificateBase64.");

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

        return X509Certificate2.CreateFromEncryptedPem(certPem, keyPem, password);
    }

    private static X509Certificate2 LoadFromPfx(string certificateBase64, string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException(MissingCertificatePasswordMessage);

        return X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(certificateBase64), password, X509KeyStorageFlags.Exportable);
    }

    private static string ComputeSha256Fingerprint(X509Certificate2 cert)
    {
        var hashBytes = SHA256.HashData(cert.RawData);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
