using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;

namespace OpenKSeF.Api.Services;

/// <summary>One active company per NIP per instance (REQ-0054): every insert of a Tenants row takes this lock and check inside its transaction.</summary>
public static class TenantNipGuard
{
    public const string TakenMessage = "Firma z tym NIP-em jest już dodana na tym serwerze OpenKSeF. Poproś osobę, która nią zarządza, o zaproszenie do firmy. Jeśli nie wiesz, kto to jest, napisz do administratora serwera. Jeśli masz token KSeF z uprawnieniem Zarządzanie uprawnieniami dla tego NIP-u, możesz przejąć firmę w portalu OpenKSeF (Przejmij firmę).";
    public const string InvalidNipMessage = "Niepoprawny NIP. Wpisz 10 cyfr z poprawną sumą kontrolną.";
    private const int LockKeyspace = 1263748423;

    public static string AlreadyMemberMessage(string companyName) => $"Masz już dostęp do firmy z tym NIP-em: {companyName}.";

    public static Task LockAsync(ApplicationDbContext db, string nip, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockKeyspace}, hashtext({nip}))", ct);

    public static Task<bool> ActiveTenantExistsAsync(ApplicationDbContext db, string nip, CancellationToken ct = default) =>
        db.Tenants.AnyAsync(t => t.Nip == nip && !t.UserId.StartsWith("deleted:"), ct);

    public static async Task<string?> MemberCompanyNameAsync(ApplicationDbContext db, string nip, string userId)
    {
        var names = await db.Tenants
            .Where(t => t.Nip == nip && db.TenantMembers.Any(m => m.TenantId == t.Id && m.UserId == userId))
            .OrderBy(t => t.DisplayName).ThenBy(t => t.Id)
            .Select(t => t.DisplayName)
            .ToListAsync();
        return names.Count == 0 ? null : string.IsNullOrWhiteSpace(names[0]) ? $"NIP {nip}" : names[0];
    }
}
