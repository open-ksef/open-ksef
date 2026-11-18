using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;

namespace OpenKSeF.Api.Services;

/// <summary>One active company per NIP per instance (REQ-0054): every insert of a Tenants row takes this lock and check inside its transaction.</summary>
public static class TenantNipGuard
{
    public const string TakenMessage = "Firma z tym NIP-em jest już dodana na tym serwerze OpenKSeF. Poproś osobę, która nią zarządza, o zaproszenie do firmy. Jeśli nie wiesz, kto to jest, napisz do administratora serwera.";
    private const int LockKeyspace = 1263748423;

    public static Task LockAsync(ApplicationDbContext db, string nip, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockKeyspace}, hashtext({nip}))", ct);

    public static Task<bool> ActiveTenantExistsAsync(ApplicationDbContext db, string nip, CancellationToken ct = default) =>
        db.Tenants.AnyAsync(t => t.Nip == nip && !t.UserId.StartsWith("deleted:"), ct);
}
