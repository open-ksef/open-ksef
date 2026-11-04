using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Domain.Services;

public class IssuedInvoiceService(
    ApplicationDbContext db,
    ISellerProfileService sellerProfiles,
    IFa3InvoiceWriter writer,
    IKSeFIssuingEnvironment environment,
    IExchangeRateSource exchangeRates,
    TimeProvider timeProvider) : IIssuedInvoiceService
{
    private const int MaxLines = 1000;
    private const int MaxTextLength = 512;
    private const int MaxShortTextLength = 256;
    private const decimal MaxQuantity = 1_000_000_000_000m;
    private const decimal MaxUnitNetPrice = 100_000_000_000_000m;
    private const decimal MaxLineNet = 10_000_000_000_000m;
    private const decimal MaxExchangeRate = 1_000_000m;
    private const int MaxForeignIdLength = 50;

    public IQueryable<IssuedInvoice> Query(Guid tenantId, IssuedInvoiceListFilter filter)
    {
        var errors = new List<InvoiceCheck>();
        var query = db.IssuedInvoices.Where(i => i.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            switch (filter.Status)
            {
                case "Draft":
                    query = query.Where(i => i.Status == IssuedInvoiceStatus.Draft);
                    break;
                case "Sending":
                    query = query.Where(i => i.Status == IssuedInvoiceStatus.Queued || i.Status == IssuedInvoiceStatus.Sending);
                    break;
                case "Accepted":
                    query = query.Where(i => i.Status == IssuedInvoiceStatus.Accepted);
                    break;
                case "Rejected":
                    query = query.Where(i => i.Status == IssuedInvoiceStatus.Rejected);
                    break;
                default:
                    errors.Add(new("INV-VAL-080", "status", "Dozwolone statusy: Draft, Sending, Accepted, Rejected"));
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.Month))
        {
            if (DateOnly.TryParseExact(filter.Month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
            {
                var to = from.AddMonths(1);
                query = query.Where(i => i.IssueDate >= from && i.IssueDate < to);
            }
            else
            {
                errors.Add(new("INV-VAL-081", "month", "Miesiąc musi mieć format RRRR-MM"));
            }
        }

        if (errors.Count > 0)
            throw new InvoiceValidationException(errors);

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var term = filter.Query.Trim().ToLower();
            query = query.Where(i =>
                i.BuyerName!.ToLower().Contains(term)
                || i.BuyerNip!.Contains(term)
                || i.BuyerForeignId!.ToLower().Contains(term)
                || i.Number!.ToLower().Contains(term));
        }

        return query;
    }

    public async Task<(IReadOnlyList<IssuedFeedRow> Items, int TotalCount)> FeedAsync(
        Guid tenantId, IssuedInvoiceListFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        var own = Query(tenantId, filter);
        var take = (int)Math.Min((long)page * pageSize, int.MaxValue);

        var ownRows = await own
            .OrderByDescending(i => i.IssueDate)
            .ThenByDescending(i => i.CreatedAt)
            .ThenBy(i => i.Id)
            .Take(take)
            .Select(i => new IssuedFeedRow(
                i.Id,
                false,
                i.Status,
                i.Number,
                i.BuyerName,
                i.BuyerNip,
                i.BuyerIdType,
                i.BuyerForeignId,
                i.BuyerCountryCode,
                i.IssueDate,
                i.DueDate,
                i.TotalNet,
                i.TotalGross,
                i.Currency,
                i.KsefNumber,
                i.EscalatedAt != null,
                i.IssueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                i.CreatedAt))
            .ToListAsync(ct);
        var totalCount = await own.CountAsync(ct);

        List<IssuedFeedRow> externalRows = [];
        if (string.IsNullOrWhiteSpace(filter.Status) || filter.Status == "Accepted")
        {
            var external = ExternalIssued(tenantId, filter);
            externalRows = await external
                .OrderByDescending(i => i.IssueDate)
                .ThenByDescending(i => i.FirstSeenAt)
                .ThenBy(i => i.Id)
                .Take(take)
                .Select(i => new IssuedFeedRow(
                    i.Id,
                    true,
                    IssuedInvoiceStatus.Accepted,
                    i.InvoiceNumber,
                    i.BuyerName,
                    i.BuyerNip,
                    i.BuyerIdType,
                    i.BuyerForeignId,
                    null,
                    IssuedInvoice.PolandDate(i.IssueDate),
                    i.DueDate,
                    i.AmountNet,
                    i.AmountGross,
                    i.Currency,
                    i.KSeFInvoiceNumber,
                    false,
                    i.IssueDate,
                    i.FirstSeenAt))
                .ToListAsync(ct);
            totalCount += await external.CountAsync(ct);
        }

        // Stable sort without Id: it keeps each query's order, and C# and Postgres order a Guid differently.
        var items = ownRows.Concat(externalRows)
            .OrderByDescending(r => r.SortDate)
            .ThenByDescending(r => r.SeenAt)
            .Skip(take - pageSize)
            .Take(pageSize)
            .ToList();

        return (items, totalCount);
    }

    private IQueryable<InvoiceHeader> ExternalIssued(Guid tenantId, IssuedInvoiceListFilter filter)
    {
        var query = db.InvoiceHeaders.Where(i =>
            i.TenantId == tenantId
            && i.Direction == InvoiceDirection.Issued
            && !db.IssuedInvoices.Any(o => o.TenantId == tenantId && o.KsefNumber == i.KSeFInvoiceNumber));

        if (!string.IsNullOrWhiteSpace(filter.Month))
        {
            var from = DateOnly.ParseExact(filter.Month, "yyyy-MM", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var to = from.AddMonths(1);
            query = query.Where(i => i.IssueDate >= from && i.IssueDate < to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var term = filter.Query.Trim().ToLower();
            query = query.Where(i =>
                i.BuyerName!.ToLower().Contains(term)
                || i.BuyerNip!.Contains(term)
                || i.BuyerForeignId!.ToLower().Contains(term)
                || i.InvoiceNumber!.ToLower().Contains(term)
                || i.KSeFInvoiceNumber.ToLower().Contains(term));
        }

        return query;
    }

    public async Task<IssuedInvoiceView?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var invoice = await LoadAsync(tenantId, id, ct);
        return invoice is null ? null : await ViewAsync(invoice, ct);
    }

    public async Task<ExchangeRateQuote?> ProposeExchangeRateAsync(string? currency, DateOnly? issueDate, DateOnly? saleDate, CancellationToken ct = default)
    {
        var code = currency?.Trim().ToUpperInvariant();
        if (!IsLetters(code, 3))
            throw new InvoiceValidationException([new("INV-VAL-090", "currency", "Waluta musi być trzyliterowym kodem ISO 4217")]);
        if (code == IssuedInvoice.DefaultCurrency)
            return null;

        var issued = issueDate ?? Today();
        return await exchangeRates.LatestMidBeforeAsync(code!, Min(issued, saleDate ?? issued), ct);
    }

    public async Task<IssuedInvoiceView> CreateAsync(Guid tenantId, IssuedInvoiceDraftInput input, CancellationToken ct = default)
    {
        var invoice = IssuedInvoice.Draft(Guid.NewGuid(), tenantId, Today(), Now());
        Apply(invoice, input);
        await ProposeRateIfMissingAsync(invoice, ct);
        db.IssuedInvoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return await ViewAsync(invoice, ct);
    }

    public async Task<IssuedInvoiceView?> UpdateAsync(Guid tenantId, Guid id, IssuedInvoiceDraftInput input, CancellationToken ct = default)
    {
        var invoice = await LoadAsync(tenantId, id, ct);
        if (invoice is null)
            return null;
        if (!invoice.IsEditable)
            throw new InvoiceStateException("Faktury w tym statusie nie można zmieniać");

        Apply(invoice, input);
        await ProposeRateIfMissingAsync(invoice, ct);
        invoice.Touch(Now());
        await SaveOrConflictAsync(ct);
        return await ViewAsync(invoice, ct);
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var invoice = await LoadAsync(tenantId, id, ct);
        if (invoice is null)
            return false;
        if (invoice.Status != IssuedInvoiceStatus.Draft)
            throw new InvoiceStateException("Usunąć można tylko szkic");

        db.IssuedInvoices.Remove(invoice);
        await SaveOrConflictAsync(ct);
        return true;
    }

    public async Task<(IssuedInvoiceView View, bool Issued)?> IssueAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var invoice = await LoadAsync(tenantId, id, ct);
        if (invoice is null)
            return null;
        if (!invoice.IsEditable)
            return (await ViewAsync(invoice, ct), false);

        var profile = await db.SellerProfiles.SingleOrDefaultAsync(p => p.TenantId == tenantId, ct) ?? new SellerProfile(tenantId);
        var sellerNip = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Nip).SingleOrDefaultAsync(ct);
        if (sellerNip is null)
            return null;

        var errors = IssueChecks.Errors(invoice, profile, Today());
        if (errors.Count > 0)
            throw new InvoiceValidationException(errors);

        await EnsureProposedRateIsCurrentAsync(invoice, ct);

        var now = Now();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Before the counter row: DeleteTenantAsync locks the tenant first, so the reverse order could deadlock.
        var tenantLocked = await db.Database
            .SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM "Tenants" WHERE "Id" = {tenantId} FOR KEY SHARE""")
            .ToListAsync(ct);
        if (tenantLocked.Count == 0)
            return null;

        var number = invoice.Number ?? InvoiceNumbering.Format(
            profile.NumberingPattern,
            await sellerProfiles.AllocateNumberAsync(tenantId, InvoiceNumbering.PeriodKey(profile.NumberingPattern, invoice.IssueDate), ct),
            invoice.IssueDate);

        invoice.Issue(profile, sellerNip, number, now);
        var written = writer.Write(invoice, now);
        if (!written.IsValid)
            throw new InvoiceValidationException(written.SchemaErrors.Select(e => new InvoiceCheck("INV-XSD", null, $"Faktura nie spełnia schematu FA(3): {e}")).ToList());

        invoice.AttachSentXml(
            written.Xml,
            Convert.ToHexString(SHA256.HashData(written.Xml)),
            environment.Current,
            environment.BuildVerificationUrl(sellerNip, invoice.IssueDate, written.Xml));

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var current = await LoadAsync(tenantId, id, ct);
            if (current is null || current.IsEditable)
                throw new InvoiceStateException("Faktura została równocześnie zmieniona, spróbuj ponownie");
            return (await ViewAsync(current, ct), false);
        }
        catch (DbUpdateException e) when (IsUniqueViolation(e))
        {
            throw new InvoiceStateException($"Numer {number} jest już użyty. Ustaw wyższy następny numer w danych do faktur.");
        }

        return (await ViewAsync(invoice, ct), true);
    }

    public async Task<IssuedInvoiceView?> CopyAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var source = await LoadAsync(tenantId, id, ct);
        if (source is null)
            return null;
        if (source.Status is IssuedInvoiceStatus.Queued or IssuedInvoiceStatus.Sending)
            throw new InvoiceStateException("Tej faktury nie można skopiować w trakcie wysyłki do KSeF. Poczekaj na wynik wysyłki.");

        var copy = IssuedInvoice.Draft(Guid.NewGuid(), tenantId, Today(), Now());
        copy.SetBuyer(source.BuyerKind, source.BuyerNip, source.BuyerName, source.BuyerAddressLine1, source.BuyerAddressLine2);
        copy.SetForeignBuyer(source.BuyerCountryCode, source.BuyerForeignId);
        copy.SetCurrency(source.Currency, null, null);
        copy.SetPayment(source.PaymentMethod, source.SplitPayment);
        copy.SetExemptionBasis(source.ExemptionBasisKind, source.ExemptionBasisText);
        copy.ReplaceLines(source.Lines.OrderBy(l => l.LineNo).Select(l => ((string?)l.Name, (string?)l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate)));
        await ProposeRateIfMissingAsync(copy, ct);
        db.IssuedInvoices.Add(copy);
        await db.SaveChangesAsync(ct);
        return await ViewAsync(copy, ct);
    }

    public async Task<bool> DeleteTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // The tenant lock stops new invoices and IssueAsync; the row lock waits out any other status change, so the checks below read the final status.
        var tenantLocked = await db.Database
            .SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM "Tenants" WHERE "Id" = {tenantId} FOR UPDATE""")
            .ToListAsync(ct);
        if (tenantLocked.Count == 0)
            return false;

        await db.Database.ExecuteSqlAsync($"""SELECT 1 FROM "IssuedInvoices" WHERE "TenantId" = {tenantId} FOR UPDATE""", ct);
        var invoices = db.IssuedInvoices.Where(i => i.TenantId == tenantId);

        if (await invoices.AnyAsync(i => i.Status == IssuedInvoiceStatus.Accepted && (i.KsefEnvironment == null || (i.KsefEnvironment != "test" && i.KsefEnvironment != "demo")), ct))
            throw new InvoiceStateException("Nie można usunąć firmy, która ma faktury przyjęte przez produkcyjny KSeF. Faktury pozostają w KSeF. Aby usunąć dane firmy, skontaktuj się z właścicielem instancji.");
        if (await invoices.AnyAsync(i => i.Status == IssuedInvoiceStatus.Queued || i.Status == IssuedInvoiceStatus.Sending, ct))
            throw new InvoiceStateException("Nie można usunąć firmy w trakcie wysyłki faktur do KSeF. Poczekaj na wynik wysyłki i spróbuj ponownie.");

        await invoices.ExecuteDeleteAsync(ct);
        await db.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    internal static bool IsUniqueViolation(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private void Apply(IssuedInvoice invoice, IssuedInvoiceDraftInput input)
    {
        var errors = new List<InvoiceCheck>();

        var kind = ParseName(input.Buyer?.Kind, BuyerKind.Company, "INV-VAL-070", "buyer.kind", "Rodzaj nabywcy: Company, Person lub Foreign", errors);
        var method = ParseName(input.PaymentMethod, PaymentMethod.Transfer, "INV-VAL-071", "paymentMethod", "Forma płatności: Transfer, Cash lub Card", errors);
        ExemptionBasisKind? exemption = input.ExemptionBasisKind is null
            ? null
            : ParseName(input.ExemptionBasisKind, default(ExemptionBasisKind), "INV-VAL-072", "exemptionBasis.kind", "Podstawa zwolnienia: Act, Directive lub Other", errors);

        if (input.Buyer?.Nip?.Trim().Replace("-", "").Length > 10)
            errors.Add(new("INV-VAL-013", "buyer.nip", "Dla nabywcy firmy wymagany jest poprawny NIP"));
        CheckLength(input.Buyer?.Name, MaxTextLength, "buyer.name", errors);
        CheckLength(input.Buyer?.AddressLine1, MaxTextLength, "buyer.addressLine1", errors);
        CheckLength(input.Buyer?.AddressLine2, MaxTextLength, "buyer.addressLine2", errors);
        CheckLength(input.ExemptionBasisText, MaxShortTextLength, "exemptionBasis.text", errors);
        CheckLength(input.Buyer?.ForeignId, MaxForeignIdLength, "buyer.foreignId", errors);

        var country = input.Buyer?.CountryCode?.Trim();
        if (!string.IsNullOrEmpty(country) && !IsLetters(country, 2))
            errors.Add(new("INV-VAL-030", "buyer.countryCode", "Podaj kraj nabywcy inny niż Polska"));

        var currency = input.Currency?.Trim().ToUpperInvariant();
        if (currency is not null && !IsLetters(currency, 3))
            errors.Add(new("INV-VAL-090", "currency", "Waluta musi być trzyliterowym kodem ISO 4217"));
        if (input.ExchangeRate is { } exchangeRate && (Math.Abs(exchangeRate) >= MaxExchangeRate || Math.Round(exchangeRate, 6) != exchangeRate))
            errors.Add(new("INV-VAL-093", "exchangeRate", "Kurs waluty poza zakresem lub ponad 6 miejsc po przecinku"));

        var lines = input.Lines ?? [];
        if (lines.Count > MaxLines)
            errors.Add(new("INV-VAL-003", "lines", $"Faktura może mieć najwyżej {MaxLines} pozycji"));

        var parsedLines = new List<(string? Name, string? Unit, decimal Quantity, decimal UnitNetPrice, VatRate VatRate)>();
        foreach (var (line, index) in lines.Select((l, i) => (l, i)))
        {
            var field = $"lines[{index}]";
            CheckLength(line.Name, MaxTextLength, $"{field}.name", errors);
            CheckLength(line.Unit, MaxShortTextLength, $"{field}.unit", errors);

            var quantityOk = Math.Abs(line.Quantity) < MaxQuantity && Math.Round(line.Quantity, 6) == line.Quantity;
            var priceOk = Math.Abs(line.UnitNetPrice) < MaxUnitNetPrice && Math.Round(line.UnitNetPrice, 2) == line.UnitNetPrice;
            if (!quantityOk)
                errors.Add(new("INV-VAL-053", $"{field}.quantity", "Ilość poza zakresem lub ponad 6 miejsc po przecinku"));
            if (!priceOk)
                errors.Add(new("INV-VAL-054", $"{field}.unitNetPrice", "Cena netto poza zakresem lub ponad 2 miejsca po przecinku"));
            if (quantityOk && priceOk && Math.Abs(line.Quantity * line.UnitNetPrice) >= MaxLineNet)
                errors.Add(new("INV-VAL-054", $"{field}.unitNetPrice", "Wartość pozycji poza zakresem"));

            if (VatRates.TryParse(line.VatRate, out var rate))
                parsedLines.Add((line.Name, line.Unit, line.Quantity, line.UnitNetPrice, rate));
            else
                errors.Add(new("INV-VAL-060", $"{field}.vatRate", "Dozwolone stawki VAT: 23, 8, 5, 0 KR, zw"));
        }

        if (errors.Count > 0)
            throw new InvoiceValidationException(errors);

        var issueDate = input.IssueDate ?? Today();
        invoice.SetBuyer(kind, input.Buyer?.Nip, input.Buyer?.Name, input.Buyer?.AddressLine1, input.Buyer?.AddressLine2);
        invoice.SetForeignBuyer(input.Buyer?.CountryCode, input.Buyer?.ForeignId);
        invoice.SetCurrency(currency, input.ExchangeRate, input.ExchangeRateDate);
        invoice.SetDates(issueDate, input.SaleDate ?? issueDate, input.DueDate ?? issueDate.AddDays(IssuedInvoice.DefaultPaymentDays));
        invoice.SetPayment(method, input.SplitPayment);
        invoice.SetExemptionBasis(exemption, input.ExemptionBasisText);
        invoice.ReplaceLines(parsedLines);
    }

    private async Task ProposeRateIfMissingAsync(IssuedInvoice invoice, CancellationToken ct)
    {
        if (!invoice.NeedsExchangeRate)
            return;

        var quote = await exchangeRates.LatestMidBeforeAsync(invoice.Currency, Min(invoice.IssueDate, invoice.SaleDate), ct);
        if (quote is not null)
            invoice.SetCurrency(null, quote.Rate, quote.TableDate);
    }

    private async Task EnsureProposedRateIsCurrentAsync(IssuedInvoice invoice, CancellationToken ct)
    {
        if (invoice.Currency == IssuedInvoice.DefaultCurrency || invoice.ExchangeRateDate is not { } tableDate)
            return;

        var cutoff = Min(invoice.IssueDate, invoice.SaleDate);
        var expected = await exchangeRates.LatestMidBeforeAsync(invoice.Currency, cutoff, ct);
        if (tableDate >= cutoff || (expected is not null && expected.TableDate != tableDate))
            throw new InvoiceValidationException([new("INV-VAL-094", "exchangeRate",
                $"Kurs NBP z tabeli z {tableDate:yyyy-MM-dd} nie pasuje do dat faktury. Pobierz kurs ponownie albo wpisz go ręcznie.")]);
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static bool IsLetters(string? value, int length) => value?.Length == length && value.All(char.IsAsciiLetter);

    private static T ParseName<T>(string? value, T fallback, string code, string field, string message, List<InvoiceCheck> errors) where T : struct, Enum
    {
        if (value is null)
            return fallback;
        if (Enum.GetNames<T>().Contains(value))
            return Enum.Parse<T>(value);

        errors.Add(new(code, field, message));
        return fallback;
    }

    private static void CheckLength(string? value, int max, string field, List<InvoiceCheck> errors)
    {
        if (value?.Trim().Length > max)
            errors.Add(new("INV-VAL-073", field, $"Pole może mieć najwyżej {max} znaków"));
    }

    private Task<IssuedInvoice?> LoadAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        db.IssuedInvoices.Include(i => i.Lines).SingleOrDefaultAsync(i => i.TenantId == tenantId && i.Id == id, ct);

    private async Task SaveOrConflictAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvoiceStateException("Faktura została równocześnie zmieniona, odśwież stronę");
        }
    }

    private async Task<IssuedInvoiceView> ViewAsync(IssuedInvoice invoice, CancellationToken ct)
    {
        if (!invoice.IsEditable)
        {
            var snapshot = new IssuedInvoiceSeller(invoice.SellerNip!, invoice.SellerName, invoice.SellerAddressLine1,
                invoice.SellerAddressLine2, invoice.SellerBankAccount, invoice.SellerCashAccounting,
                invoice.SellerIban, invoice.SellerBankSwift, invoice.SellerBankName);
            return new IssuedInvoiceView(invoice, snapshot, invoice.KsefEnvironment ?? environment.Current, IssueChecks.Warnings(invoice));
        }

        var nip = await db.Tenants.Where(t => t.Id == invoice.TenantId).Select(t => t.Nip).SingleAsync(ct);
        var profile = await db.SellerProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == invoice.TenantId, ct);
        var account = profile?.PaymentAccountFor(invoice.BuyerKind);
        var seller = new IssuedInvoiceSeller(nip, profile?.LegalName, profile?.AddressLine1, profile?.AddressLine2, account?.BankAccount, profile?.CashAccounting ?? false,
            account?.Iban, account?.Swift, account?.BankName);
        return new IssuedInvoiceView(invoice, seller, environment.Current, IssueChecks.Warnings(invoice));
    }

    private DateOnly Today() => IssuedInvoice.PolandDate(timeProvider.GetUtcNow());

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
