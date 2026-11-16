using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public DashboardController(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<TenantDashboardSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get()
    {
        var now = DateTime.UtcNow;
        var successThreshold = now.AddHours(-24);
        var warningThreshold = now.AddDays(-7);
        var last7DaysThreshold = now.AddDays(-7);
        var last30DaysThreshold = now.AddDays(-30);

        var rows = await _db.Tenants
            .Where(t => _db.TenantMembers.Any(m => m.TenantId == t.Id && m.UserId == _currentUser.UserId))
            .OrderBy(t => t.DisplayName ?? t.Nip)
            .Select(t => new
            {
                Summary = new TenantDashboardSummaryResponse(
                    t.Id,
                    t.Nip,
                    t.DisplayName,
                    t.SyncState != null ? t.SyncState.LastSyncedAt : null,
                    t.SyncState != null ? t.SyncState.LastSuccessfulSync : null,
                    _db.InvoiceHeaders.Count(i => i.TenantId == t.Id && i.Direction == InvoiceDirection.Received),
                    _db.InvoiceHeaders.Count(i => i.TenantId == t.Id && i.Direction == InvoiceDirection.Received && i.IssueDate >= last7DaysThreshold),
                    _db.InvoiceHeaders.Count(i => i.TenantId == t.Id && i.Direction == InvoiceDirection.Received && i.IssueDate >= last30DaysThreshold),
                    t.SyncState == null || !t.SyncState.LastSuccessfulSync.HasValue
                        ? "Error"
                        : t.SyncState.LastSuccessfulSync >= successThreshold
                            ? "Success"
                            : t.SyncState.LastSuccessfulSync >= warningThreshold
                                ? "Warning"
                                : "Error"),
                HasCredential = t.KSeFCredentials.Any(),
                ErrorCategory = t.SyncState != null ? t.SyncState.LastErrorCategory : null,
                ErrorAt = t.SyncState != null ? t.SyncState.LastErrorAt : null,
            })
            .ToListAsync();

        return Ok(rows.Select(r => r.Summary with
        {
            SyncProblem = !r.HasCredential ? "NoCredential"
                : r.ErrorCategory is not null ? "Failed"
                : r.Summary.LastSuccessfulSync is null ? "NeverSynced"
                : null,
            LastSyncErrorCategory = r.ErrorCategory,
            LastSyncErrorMessage = SyncFailure.Message(r.ErrorCategory),
            LastSyncErrorAt = r.ErrorAt,
            LastSyncErrorCredentialCause = SyncFailure.IsCredentialCause(r.ErrorCategory),
        }).ToList());
    }
}
