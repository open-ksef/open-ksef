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
    private readonly ILogger<CredentialsController> _logger;

    public CredentialsController(
        ApplicationDbContext db,
        ICurrentUserService currentUser,
        IEncryptionService encryption,
        ITenantSyncService tenantSyncService,
        ILogger<CredentialsController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _encryption = encryption;
        _tenantSyncService = tenantSyncService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
                    return BadRequest(new { error = "Invalid certificate or password." });
                }
                catch (FormatException)
                {
                    return BadRequest(new { error = "CertificateBase64 is not valid base64." });
                }
                break;

            default:
                return BadRequest(new { error = $"Unknown credential type: {request.Type}" });
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
            .Select(c => new { c.Type, c.UpdatedAt })
            .FirstOrDefaultAsync();

        return Ok(new CredentialStatusResponse(
            Exists: credential is not null,
            CredentialType: credential?.Type,
            UpdatedAt: credential?.UpdatedAt));
    }

    [HttpPost("sync")]
    [ProducesResponseType(typeof(TenantManualSyncResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SyncNow(Guid tenantId, CancellationToken cancellationToken)
    {
        var result = await _tenantSyncService.SyncTenantAsync(
            tenantId,
            _currentUser.UserId,
            cancellationToken);

        return result.Outcome switch
        {
            TenantSyncOutcome.TenantNotFound => NotFound(),
            TenantSyncOutcome.MissingCredential => Conflict(new { error = result.ErrorMessage }),
            TenantSyncOutcome.Failed => StatusCode(StatusCodes.Status502BadGateway, new { error = result.ErrorMessage }),
            TenantSyncOutcome.Success => Ok(new TenantManualSyncResponse(
                TenantId: result.TenantId,
                FetchedInvoices: result.FetchedInvoices,
                NewInvoices: result.NewInvoices,
                SyncedAtUtc: result.SyncedAtUtc ?? DateTime.UtcNow)),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { error = "Unknown sync outcome." })
        };
    }

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

        if (hasPem)
            return LoadFromPem(request.CertificatePem!, request.PrivateKeyPem!, request.CertificatePassword);

        return LoadFromPfx(request.CertificateBase64!, request.CertificatePassword);
    }

    private static (X509Certificate2 cert, string base64ForStorage) LoadFromPem(
        string certPem, string keyPem, string? password)
    {
        X509Certificate2 cert;
        bool isEncryptedKey = keyPem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal);

        if (isEncryptedKey)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("CertificatePassword is required for encrypted private keys.");

            cert = X509Certificate2.CreateFromEncryptedPem(certPem, keyPem, password);
        }
        else
        {
            cert = X509Certificate2.CreateFromPem(certPem, keyPem);
        }

        var pfxBytes = cert.Export(X509ContentType.Pfx);
        return (cert, Convert.ToBase64String(pfxBytes));
    }

    private static (X509Certificate2 cert, string base64ForStorage) LoadFromPfx(
        string certificateBase64, string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("CertificatePassword is required for PFX certificates.");

        var pfxBytes = Convert.FromBase64String(certificateBase64);
        var cert = new X509Certificate2(pfxBytes, password);
        return (cert, certificateBase64);
    }

    private static string ComputeSha256Fingerprint(X509Certificate2 cert)
    {
        var hashBytes = SHA256.HashData(cert.RawData);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
