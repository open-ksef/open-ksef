using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Domain.Services;

public partial class SellerProfileService(ApplicationDbContext db, TimeProvider timeProvider) : ISellerProfileService
{
    private const int MaxTextLength = 512;
    private const int MaxPatternLength = 100;

    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex MonthPeriodRegex();

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex YearPeriodRegex();

    public async Task<SellerProfileView?> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var nip = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Nip).SingleOrDefaultAsync(ct);
        if (nip is null)
            return null;

        var profile = await db.SellerProfiles.SingleOrDefaultAsync(p => p.TenantId == tenantId, ct) ?? new SellerProfile(tenantId);
        return await ViewAsync(profile, nip, ct);
    }

    public async Task<(SellerProfileView? View, IReadOnlyList<InvoiceCheck> Errors)> SaveAsync(
        Guid tenantId, SellerProfileInput input, CancellationToken ct = default)
    {
        var nip = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Nip).SingleOrDefaultAsync(ct);
        if (nip is null)
            return (null, []);

        var pattern = input.NumberingPattern?.Trim() ?? InvoiceNumbering.DefaultPattern;
        var bankAccount = string.IsNullOrWhiteSpace(input.BankAccount) ? null : Nrb.Normalize(input.BankAccount);
        var period = input.NextNumberPeriod?.Trim() ?? InvoiceNumbering.PeriodKey(pattern, Today());
        var errors = Validate(input, pattern, bankAccount, period);
        if (errors.Count > 0)
            return (null, errors);

        var profile = await db.SellerProfiles.SingleOrDefaultAsync(p => p.TenantId == tenantId, ct);
        if (profile is null)
        {
            profile = new SellerProfile(tenantId);
            db.SellerProfiles.Add(profile);
        }

        profile.Update(input.LegalName, input.AddressLine1, input.AddressLine2, bankAccount, input.CashAccounting, pattern);

        if (input.NextNumber is { } next)
        {
            var counter = await db.InvoiceNumberCounters.SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Period == period, ct);
            if (counter is null)
                db.InvoiceNumberCounters.Add(new InvoiceNumberCounter { TenantId = tenantId, Period = period, Next = next });
            else
                counter.Next = next;
        }

        await db.SaveChangesAsync(ct);
        return (await ViewAsync(profile, nip, ct), []);
    }

    public async Task<int> AllocateNumberAsync(Guid tenantId, string period, CancellationToken ct = default)
    {
        var numbers = await db.Database.SqlQuery<int>($"""
            INSERT INTO "InvoiceNumberCounters" ("TenantId", "Period", "Next") VALUES ({tenantId}, {period}, 2)
            ON CONFLICT ("TenantId", "Period") DO UPDATE SET "Next" = "InvoiceNumberCounters"."Next" + 1
            RETURNING "Next" - 1 AS "Value"
            """).ToListAsync(ct);
        return numbers.Single();
    }

    private static List<InvoiceCheck> Validate(SellerProfileInput input, string pattern, string? bankAccount, string period)
    {
        var errors = new List<InvoiceCheck>();

        if (bankAccount is not null && !Nrb.IsValid(bankAccount))
            errors.Add(new("SP-VAL-001", "bankAccount", "Niepoprawny numer rachunku"));
        if (!InvoiceNumbering.HasNumberToken(pattern))
            errors.Add(new("SP-VAL-002", "numberingPattern", "Wzorzec numeracji musi zawierać {nr}"));
        if (pattern.Length > MaxPatternLength)
            errors.Add(new("SP-VAL-005", "numberingPattern", $"Wzorzec numeracji może mieć najwyżej {MaxPatternLength} znaków"));
        if (input.NextNumber is < 1)
            errors.Add(new("SP-VAL-003", "nextNumber", "Następny numer musi być większy od zera"));
        if (!(pattern.Contains("{MM}", StringComparison.Ordinal) ? MonthPeriodRegex() : YearPeriodRegex()).IsMatch(period))
            errors.Add(new("SP-VAL-004", "nextNumberPeriod", "Okres numeracji musi mieć format RRRR-MM (wzorzec z {MM}) lub RRRR"));

        foreach (var (field, value) in new[] { ("legalName", input.LegalName), ("addressLine1", input.AddressLine1), ("addressLine2", input.AddressLine2) })
        {
            if (value?.Trim().Length > MaxTextLength)
                errors.Add(new("SP-VAL-005", field, $"Pole może mieć najwyżej {MaxTextLength} znaków"));
        }

        return errors;
    }

    private async Task<SellerProfileView> ViewAsync(SellerProfile profile, string nip, CancellationToken ct)
    {
        var today = Today();
        var period = InvoiceNumbering.PeriodKey(profile.NumberingPattern, today);
        var next = await db.InvoiceNumberCounters
            .Where(c => c.TenantId == profile.TenantId && c.Period == period)
            .Select(c => (int?)c.Next)
            .SingleOrDefaultAsync(ct) ?? 1;
        return new SellerProfileView(profile, nip, next, period, InvoiceNumbering.Format(profile.NumberingPattern, next, today));
    }

    private DateOnly Today() => IssuedInvoice.PolandDate(timeProvider.GetUtcNow());
}
