using System.Globalization;
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
    private const string ConcurrentChangeMessage = "Faktura została równocześnie zmieniona, odśwież stronę";
    public const string SellerChangedCode = "INV-SELLER-CHANGED";
    public const string SellerChangedMessage = "Dane sprzedawcy zostały zmienione po otwarciu potwierdzenia. Sprawdź je i potwierdź wystawienie ponownie.";
    public const string KindMismatchCode = "INV-KIND-MISMATCH";
    public const string KindMismatchMessage = "Ten dokument to faktura korygująca lub zaliczkowa. Edytuj go w portalu.";
    public const string CorrectionOpenCode = "INV-KOR-OPEN";
    public const string CorrectionSourceCode = "INV-KOR-SOURCE";
    public const string ReissueCode = "INV-KOR-REISSUE";
    public const string ChainOpenCode = "INV-CHAIN-OPEN";
    public const string ChainClosedCode = "INV-CHAIN-CLOSED";
    public const string ChainSourceCode = "INV-CHAIN-SOURCE";
    public const string FinalExistsCode = "INV-ROZ-EXISTS";
    public const string UseFinalCorrectionCode = "INV-KOR-USE-ROZ";
    public const string TemplateKindCode = "INV-TEMPLATE-KIND";
    private const int MaxSuggestions = 20;

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
                || i.Number!.ToLower().Contains(term)
                || i.KsefNumber!.ToLower().Contains(term));
        }

        return query;
    }

    public async Task<(IReadOnlyList<IssuedFeedRow> Items, int TotalCount)> FeedAsync(
        Guid tenantId, IssuedInvoiceListFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        var own = Query(tenantId, filter);
        var take = page * pageSize;

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
                i.InvoiceType == IssuedInvoiceType.Zal || i.InvoiceType == IssuedInvoiceType.KorZal
                    || (i.InvoiceType != IssuedInvoiceType.Vat && i.TotalGross <= 0) ? null : i.DueDate,
                i.TotalNet,
                i.TotalGross,
                i.Currency,
                i.KsefNumber,
                i.EscalatedAt != null,
                i.IssueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                i.CreatedAt,
                i.InvoiceType.Code()))
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
                    i.FirstSeenAt,
                    i.InvoiceType ?? "VAT"))
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

    public async Task<IssuedInvoiceView?> UpdateAsync(Guid tenantId, Guid id, IssuedInvoiceDraftInput input, uint? expectedVersion = null, CancellationToken ct = default)
    {
        var invoice = await LoadAsync(tenantId, id, ct);
        if (invoice is null)
            return null;
        EnsureKind(invoice, null);
        if (!invoice.IsEditable)
            throw new InvoiceStateException("Faktury w tym statusie nie można zmieniać");
        EnsureVersion(invoice, expectedVersion);

        Apply(invoice, input);
        await ProposeRateIfMissingAsync(invoice, ct);
        invoice.Touch(Now());
        await SaveOrConflictAsync(ct);
        return await ViewAsync(invoice, ct);
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid id, uint? expectedVersion = null, CancellationToken ct = default)
    {
        var invoice = await LoadAsync(tenantId, id, ct);
        if (invoice is null)
            return false;
        if (invoice.Status != IssuedInvoiceStatus.Draft)
            throw new InvoiceStateException("Usunąć można tylko szkic");
        EnsureVersion(invoice, expectedVersion);

        db.IssuedInvoices.Remove(invoice);
        await SaveOrConflictAsync(ct);
        return true;
    }

    public async Task<(IssuedInvoiceView View, bool Issued)?> IssueAsync(Guid tenantId, Guid id, uint? expectedVersion = null, bool confirmPastIssueDate = false, uint? expectedSellerVersion = null, string? invoiceType = null, CancellationToken ct = default)
    {
        var invoice = await LoadAsync(tenantId, id, ct);
        if (invoice is null)
            return null;
        EnsureKind(invoice, invoiceType);
        if (!invoice.IsEditable)
            return (await ViewAsync(invoice, ct), false);
        EnsureVersion(invoice, expectedVersion);

        var profile = await db.SellerProfiles.SingleOrDefaultAsync(p => p.TenantId == tenantId, ct) ?? new SellerProfile(tenantId);
        if (expectedSellerVersion is { } sellerVersion && sellerVersion != SellerVersionOf(profile))
            throw new InvoiceStateException(SellerChangedMessage, SellerChangedCode);
        var sellerNip = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Nip).SingleOrDefaultAsync(ct);
        if (sellerNip is null)
            return null;

        if (!confirmPastIssueDate && IssueChecks.PastIssueDate(invoice, Today()) is { } pastIssueDate)
            throw new InvoiceValidationException([pastIssueDate]);

        if (!invoice.InvoiceType.IsCorrection())
            await EnsureProposedRateIsCurrentAsync(invoice, ct);

        var now = Now();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Before the counter row: DeleteTenantAsync locks the tenant first, so the reverse order could deadlock.
        var tenantLocked = await db.Database
            .SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM "Tenants" WHERE "Id" = {tenantId} FOR KEY SHARE""")
            .ToListAsync(ct);
        if (tenantLocked.Count == 0)
            return null;

        IssuedInvoice? original = null;
        IssuedInvoice? state = null;
        Fa3Refs? refs = null;
        var advanceCount = 0;
        IReadOnlyList<IssuedInvoice> chain = [];
        if (invoice.OrderChainId is { } chainId)
        {
            chain = await LockChainAsync(tenantId, chainId, ct);
            if (!invoice.IsNumberTakenInKsef && chain.FirstOrDefault(c => c.IsOpen && c.Id != invoice.Id) is { } openInChain)
                throw ChainOpen(openInChain);
        }

        if (invoice.InvoiceType.IsCorrection())
        {
            (original, var corrections) = await LockOriginalAsync(tenantId, invoice.CorrectedInvoiceId!.Value, ct);
            if (!invoice.IsNumberTakenInKsef && corrections.FirstOrDefault(c => c.IsOpen && c.Id != invoice.Id) is { } open)
                throw OpenCorrection(open);
            if (invoice.InvoiceType == IssuedInvoiceType.KorZal && HasFinal(chain))
                throw UseFinalCorrection();
            state = Corrections.CurrentState(original, corrections);
            refs = new Fa3Refs(original, state, AmountBefore(invoice, original, corrections));
            if (invoice.InvoiceType == IssuedInvoiceType.KorZal && IssueChecks.ChainAmountError(invoice, OtherAdvances(chain, original.Id)) is { } amountError)
                throw new InvoiceValidationException([amountError]);
        }
        else if (invoice.OrderChainId is not null)
        {
            if (chain.Any(c => c.InvoiceType == IssuedInvoiceType.Roz && c.Status == IssuedInvoiceStatus.Accepted))
                throw new InvoiceStateException(invoice.InvoiceType == IssuedInvoiceType.Roz ? FinalExistsMessage : ChainClosedMessage,
                    invoice.InvoiceType == IssuedInvoiceType.Roz ? FinalExistsCode : ChainClosedCode);
            if (invoice.InvoiceType == IssuedInvoiceType.Zal && IssueChecks.ChainAmountError(invoice, OtherAdvances(chain, invoice.Id)) is { } amountError)
                throw new InvoiceValidationException([amountError]);
            (refs, var listed) = ChainRefs(invoice, chain);
            advanceCount = listed.Count;
        }

        var pattern = profile.Pattern(invoice.InvoiceType);
        var number = invoice.Number ?? InvoiceNumbering.Format(
            pattern,
            await sellerProfiles.AllocateNumberAsync(tenantId, InvoiceNumbering.PeriodKey(pattern, invoice.IssueDate), invoice.InvoiceType.Series(), ct),
            invoice.IssueDate);

        invoice.Issue(profile, sellerNip, number, now, original, state, refs?.Deducted, advanceCount);
        var written = writer.Write(invoice, now, refs);
        if (!written.IsValid)
            throw new InvoiceValidationException(written.SchemaErrors.Select(e => new InvoiceCheck("INV-XSD", null, $"Faktura nie spełnia schematu FA(3): {e}")).ToList());

        invoice.AttachSentXml(
            written.Xml,
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
            throw new InvoiceStateException(invoice.InvoiceType.Series() switch
            {
                IssuedInvoiceTypes.KorSeries => $"Numer {number} jest już użyty. Ustaw wyższy następny numer korekt w danych do faktur.",
                IssuedInvoiceTypes.ZalSeries => $"Numer {number} jest już użyty. Ustaw wyższy następny numer faktur zaliczkowych w danych do faktur.",
                _ => $"Numer {number} jest już użyty. Ustaw wyższy następny numer w danych do faktur."
            });
        }

        return (await ViewAsync(invoice, ct), true);
    }

    public async Task<IssuedInvoiceView?> CopyAsync(Guid tenantId, Guid id, string? invoiceType = null, CancellationToken ct = default)
    {
        var source = await LoadAsync(tenantId, id, ct);
        if (source is null)
            return null;
        EnsureKind(source, invoiceType);
        if (source.Status is IssuedInvoiceStatus.Queued or IssuedInvoiceStatus.Sending)
            throw new InvoiceStateException("Tej faktury nie można skopiować w trakcie wysyłki do KSeF. Poczekaj na wynik wysyłki.");

        var (original, state) = await CopySourceAsync(tenantId, source, ct);
        return await AddCopyAsync(tenantId, InvoiceTemplate.Capture(tenantId, original, state, Now()), ct);
    }

    private async Task<(IssuedInvoice Original, IssuedInvoice State)> CopySourceAsync(Guid tenantId, IssuedInvoice source, CancellationToken ct)
    {
        var original = source.CorrectedInvoiceId is { } originalId ? await LoadAsync(tenantId, originalId, ct) ?? source : source;
        var state = original.Status == IssuedInvoiceStatus.Accepted
            ? Corrections.CurrentState(original, await CorrectionsOfAsync(tenantId, original.Id, ct))
            : original;
        return (original, state);
    }

    public async Task<IReadOnlyList<RecentBuyer>> RecentBuyersAsync(Guid tenantId, string? query, CancellationToken ct = default)
    {
        // shortcut: groups all accepted invoices of the tenant in memory; move to SQL DISTINCT ON past ~10k accepted invoices.
        var buyers = await AcceptedSales(tenantId)
            .OrderByDescending(i => i.IssueDate)
            .ThenByDescending(i => i.CreatedAt)
            .Select(i => new RecentBuyer(i.BuyerKind, i.BuyerNip, i.BuyerName, i.BuyerAddressLine1, i.BuyerAddressLine2,
                i.BuyerCountryCode, i.BuyerIdType, i.BuyerForeignId, i.IssueDate))
            .ToListAsync(ct);
        var term = query?.Trim();
        return buyers
            .DistinctBy(BuyerKey)
            .Where(b => string.IsNullOrEmpty(term)
                || (b.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (b.Nip?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (b.ForeignId?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(MaxSuggestions)
            .ToList();
    }

    public async Task<IReadOnlyList<LineSuggestion>> LineSuggestionsAsync(Guid tenantId, string? query, string? buyerKind, CancellationToken ct = default)
    {
        var errors = new List<InvoiceCheck>();
        var kind = ParseName(buyerKind, BuyerKind.Company, "INV-VAL-070", "buyerKind", "Rodzaj nabywcy: Company, Person lub Foreign", errors);
        if (errors.Count > 0)
            throw new InvoiceValidationException(errors);
        var term = query?.Trim().ToLower();
        if (term is null || term.Length < 2)
            return [];

        var rates = (kind == BuyerKind.Foreign ? VatRates.IssuableForeign : VatRates.Issuable).ToList();
        var lines = await AcceptedSales(tenantId)
            .SelectMany(i => i.Lines.Select(l => new { i.IssueDate, i.CreatedAt, l.LineNo, l.Name, l.Unit, l.UnitNetPrice, l.VatRate }))
            .Where(l => l.Name.ToLower().Contains(term) && rates.Contains(l.VatRate))
            .OrderByDescending(l => l.IssueDate)
            .ThenByDescending(l => l.CreatedAt)
            .ThenBy(l => l.LineNo)
            .ToListAsync(ct);
        return lines
            .DistinctBy(l => l.Name.ToUpperInvariant())
            .Take(MaxSuggestions)
            .Select(l => new LineSuggestion(l.Name, l.Unit, l.UnitNetPrice, l.VatRate))
            .ToList();
    }

    public async Task<IReadOnlyList<InvoiceTemplate>> ListTemplatesAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.InvoiceTemplates.AsNoTracking().Include(t => t.Lines)
            .Where(t => t.TenantId == tenantId)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public async Task<InvoiceTemplate?> CreateTemplateAsync(Guid tenantId, Guid sourceId, string? name, CancellationToken ct = default)
    {
        var checkedName = TemplateName(name);
        var source = await LoadAsync(tenantId, sourceId, ct);
        if (source is null)
            return null;
        if (source.InvoiceType != IssuedInvoiceType.Vat)
            throw new InvoiceStateException("Szablon można zapisać tylko z faktury VAT.", TemplateKindCode);

        var (original, state) = await CopySourceAsync(tenantId, source, ct);
        var template = InvoiceTemplate.Capture(tenantId, original, state, Now(), checkedName);
        db.InvoiceTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return template;
    }

    public async Task<InvoiceTemplate?> RenameTemplateAsync(Guid tenantId, Guid id, string? name, CancellationToken ct = default)
    {
        var checkedName = TemplateName(name);
        var template = await LoadTemplateAsync(tenantId, id, ct);
        if (template is null)
            return null;
        template.Rename(checkedName, Now());
        await db.SaveChangesAsync(ct);
        return template;
    }

    public async Task<bool> DeleteTemplateAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        await db.InvoiceTemplates.Where(t => t.TenantId == tenantId && t.Id == id).ExecuteDeleteAsync(ct) > 0;

    public async Task<IssuedInvoiceView?> DraftFromTemplateAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        await LoadTemplateAsync(tenantId, id, ct) is { } template ? await AddCopyAsync(tenantId, template, ct) : null;

    private IQueryable<IssuedInvoice> AcceptedSales(Guid tenantId) =>
        db.IssuedInvoices.AsNoTracking().Where(i => i.TenantId == tenantId && i.Status == IssuedInvoiceStatus.Accepted
            && (i.InvoiceType == IssuedInvoiceType.Vat || i.InvoiceType == IssuedInvoiceType.Zal || i.InvoiceType == IssuedInvoiceType.Roz));

    private static string BuyerKey(RecentBuyer buyer) => buyer.Kind switch
    {
        BuyerKind.Company => $"C|{buyer.Nip}",
        BuyerKind.Foreign when buyer.IdType == IssuedInvoice.BuyerIdNone => $"F|{buyer.CountryCode}|{buyer.Name?.ToUpperInvariant()}",
        BuyerKind.Foreign => $"F|{buyer.CountryCode}|{buyer.IdType}|{buyer.ForeignId}",
        _ => $"P|{buyer.Name?.ToUpperInvariant()}|{buyer.AddressLine1?.ToUpperInvariant()}"
    };

    private static string TemplateName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > InvoiceTemplate.MaxNameLength)
            throw new InvoiceValidationException([new("INV-VAL-099", "name", $"Nazwa szablonu musi mieć od 1 do {InvoiceTemplate.MaxNameLength} znaków")]);
        return trimmed;
    }

    private Task<InvoiceTemplate?> LoadTemplateAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        db.InvoiceTemplates.Include(t => t.Lines).SingleOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);

    public async Task<IssuedInvoiceView?> CreateCorrectionAsync(Guid tenantId, Guid originalId, string? mode, CancellationToken ct = default)
    {
        var errors = new List<InvoiceCheck>();
        var parsedMode = ParseName(mode, CorrectionMode.Edit, "INV-VAL-074", "mode", "Tryb korekty: Edit, ToZero lub WrongBuyerId", errors);
        if (errors.Count > 0)
            throw new InvoiceValidationException(errors);
        if (!await db.IssuedInvoices.AnyAsync(i => i.TenantId == tenantId && i.Id == originalId, ct))
            return null;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var tenantLocked = await db.Database
            .SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM "Tenants" WHERE "Id" = {tenantId} FOR KEY SHARE""")
            .ToListAsync(ct);
        if (tenantLocked.Count == 0)
            return null;

        var chainId = await db.IssuedInvoices.Where(i => i.TenantId == tenantId && i.Id == originalId).Select(i => i.OrderChainId).SingleAsync(ct);
        var chain = chainId is { } id ? await LockChainAsync(tenantId, id, ct) : [];
        var (original, corrections) = await LockOriginalAsync(tenantId, originalId, ct);
        if (original.InvoiceType is not (IssuedInvoiceType.Vat or IssuedInvoiceType.Zal or IssuedInvoiceType.Roz) || original.Status != IssuedInvoiceStatus.Accepted || original.KsefNumber is null)
            throw new InvoiceStateException("Korygować można tylko fakturę VAT, zaliczkową lub rozliczającą przyjętą w KSeF. Korektę wystawiasz zawsze z faktury pierwotnej.", CorrectionSourceCode);
        if (original.InvoiceType == IssuedInvoiceType.Zal && HasFinal(chain))
            throw UseFinalCorrection();
        if (corrections.FirstOrDefault(c => c.IsOpen) is { } open)
            throw OpenCorrection(open);
        if (chain.FirstOrDefault(c => c.IsOpen) is { } openInChain)
            throw ChainOpen(openInChain);

        var correction = IssuedInvoice.Correction(Guid.NewGuid(), original, Corrections.CurrentState(original, corrections), parsedMode, Today(), Now());
        db.IssuedInvoices.Add(correction);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await ViewAsync(correction, ct);
    }

    public async Task<IssuedInvoiceView?> UpdateCorrectionAsync(Guid tenantId, Guid id, IssuedInvoiceCorrectionInput input, uint? expectedVersion = null, CancellationToken ct = default)
    {
        var correction = await LoadAsync(tenantId, id, ct);
        if (correction is null)
            return null;
        if (!correction.InvoiceType.IsCorrection())
            throw new InvoiceStateException("Ten dokument nie jest fakturą korygującą.", KindMismatchCode);
        if (!correction.IsEditable)
            throw new InvoiceStateException("Faktury w tym statusie nie można zmieniać");
        EnsureVersion(correction, expectedVersion);

        var original = await LoadAsync(tenantId, correction.CorrectedInvoiceId!.Value, ct)
            ?? throw new InvoiceStateException("Nie znaleziono faktury korygowanej.");
        var state = Corrections.CurrentState(original, await CorrectionsOfAsync(tenantId, original.Id, ct));

        var errors = new List<InvoiceCheck>();
        var method = input.PaymentMethod is null
            ? correction.PaymentMethod
            : ParseName(input.PaymentMethod, PaymentMethod.Transfer, "INV-VAL-071", "paymentMethod", "Forma płatności: Transfer, Cash lub Card", errors);
        CheckLength(input.CorrectionReason, MaxShortTextLength, "correctionReason", errors);
        if (input.CorrectionType is not (null or 0 or 1 or 2 or 3))
            errors.Add(new("INV-VAL-087", "correctionType", "Typ korekty: 1, 2 lub 3; 0 usuwa typ"));
        if (input.Buyer is { } buyer)
        {
            CheckLength(buyer.Name, MaxTextLength, "buyer.name", errors);
            CheckLength(buyer.AddressLine1, MaxTextLength, "buyer.addressLine1", errors);
            CheckLength(buyer.AddressLine2, MaxTextLength, "buyer.addressLine2", errors);
            errors.AddRange(BuyerIdChanges(correction, buyer, input.BuyerIdType));
        }

        var lines = input.Lines is null ? null : ParseLines(input.Lines, errors);
        if (input.AdvanceAmount is not null && correction.InvoiceType != IssuedInvoiceType.KorZal)
            errors.Add(new(IssueChecks.AdvanceAmountCode, "advanceAmount", "Kwotę zaliczki koryguje się tylko w korekcie faktury zaliczkowej"));
        CheckAmount(input.AdvanceAmount, errors);
        if (errors.Count > 0)
            throw new InvoiceValidationException(errors);

        var issueDate = input.IssueDate ?? correction.IssueDate;
        if (input.AdvanceAmount is { } amount)
            correction.SetAdvanceAmount(amount);
        correction.SetCorrection(input.CorrectionReason ?? correction.CorrectionReason, input.CorrectionType switch
        {
            null => correction.CorrectionType,
            0 => null,
            var type => type
        });
        if (input.Buyer is { } data)
            correction.SetCorrectionBuyer(data.Name, data.AddressLine1, data.AddressLine2, data.CountryCode);
        correction.SetDates(issueDate, issueDate, input.DueDate ?? correction.DueDate);
        correction.SetPayment(method, correction.SplitPayment);
        if (lines is not null)
            correction.ReplaceCorrectionLines(lines, state);
        else
            correction.RecalculateTotals(state);

        var draftErrors = IssueChecks.CorrectionErrors(correction, original, state, Today())
            .Where(e => e.Code is IssueChecks.CorrectionIssueDateCode or IssueChecks.BaseLineCode or "INV-VAL-060" or "INV-VAL-062" or "INV-VAL-063" or "INV-VAL-064")
            .ToList();
        if (correction.InvoiceType == IssuedInvoiceType.KorZal
            && IssueChecks.ChainAmountError(correction, OtherAdvances(await ChainOfAsync(tenantId, correction.OrderChainId!.Value, ct), original.Id)) is { } amountError)
            draftErrors.Add(amountError);
        if (draftErrors.Count > 0)
            throw new InvoiceValidationException(draftErrors);

        correction.Touch(Now());
        await SaveOrConflictAsync(ct);
        return await ViewAsync(correction, ct);
    }

    public async Task<IssuedInvoiceView?> ReissueAsync(Guid tenantId, Guid originalId, CancellationToken ct = default)
    {
        var original = await LoadAsync(tenantId, originalId, ct);
        if (original is null)
            return null;

        var chain = original.InvoiceType == IssuedInvoiceType.Vat && original.Status == IssuedInvoiceStatus.Accepted
            ? Corrections.AcceptedChain(original, await CorrectionsOfAsync(tenantId, original.Id, ct))
            : [original];
        var current = chain[^1];
        if (chain.Count < 2 || current.Lines.Count > 0)
            throw new InvoiceStateException("Poprawną fakturę wystawisz po przyjęciu w KSeF korekty do zera.", ReissueCode);

        var lastSale = chain.Last(d => d.Lines.Count > 0);
        return await AddCopyAsync(tenantId, InvoiceTemplate.Capture(tenantId, original, lastSale, Now(), clearBuyerId: current.CorrectionReason == Corrections.WrongBuyerIdReason), ct);
    }

    public async Task<IssuedInvoiceView?> CreateAdvanceAsync(Guid tenantId, IssuedInvoiceAdvanceInput? input, CancellationToken ct = default)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct))
            return null;

        var advance = IssuedInvoice.Advance(Guid.NewGuid(), tenantId, Today(), Now());
        if (input is not null)
            ApplyAdvance(advance, input, []);
        await ProposeRateIfMissingAsync(advance, ct);
        db.IssuedInvoices.Add(advance);
        await db.SaveChangesAsync(ct);
        return await ViewAsync(advance, ct);
    }

    public Task<IssuedInvoiceView?> NextAdvanceAsync(Guid tenantId, Guid advanceId, CancellationToken ct = default) =>
        AddChainDocumentAsync(tenantId, advanceId, IssuedInvoiceType.Zal, ct);

    public Task<IssuedInvoiceView?> CreateFinalAsync(Guid tenantId, Guid advanceId, CancellationToken ct = default) =>
        AddChainDocumentAsync(tenantId, advanceId, IssuedInvoiceType.Roz, ct);

    public async Task<IssuedInvoiceView?> UpdateAdvanceAsync(Guid tenantId, Guid id, IssuedInvoiceAdvanceInput input, uint? expectedVersion = null, CancellationToken ct = default)
    {
        var advance = await LoadAsync(tenantId, id, ct);
        if (advance is null)
            return null;
        if (advance.InvoiceType != IssuedInvoiceType.Zal)
            throw new InvoiceStateException("Ten dokument nie jest fakturą zaliczkową.", KindMismatchCode);
        if (!advance.IsEditable)
            throw new InvoiceStateException("Faktury w tym statusie nie można zmieniać");
        EnsureVersion(advance, expectedVersion);

        ApplyAdvance(advance, input, await ChainOfAsync(tenantId, advance.OrderChainId!.Value, ct));
        await ProposeRateIfMissingAsync(advance, ct);
        advance.Touch(Now());
        await SaveOrConflictAsync(ct);
        return await ViewAsync(advance, ct);
    }

    public async Task<IssuedInvoiceView?> UpdateFinalAsync(Guid tenantId, Guid id, IssuedInvoiceFinalInput input, uint? expectedVersion = null, CancellationToken ct = default)
    {
        var final = await LoadAsync(tenantId, id, ct);
        if (final is null)
            return null;
        if (final.InvoiceType != IssuedInvoiceType.Roz)
            throw new InvoiceStateException("Ten dokument nie jest fakturą rozliczającą.", KindMismatchCode);
        if (!final.IsEditable)
            throw new InvoiceStateException("Faktury w tym statusie nie można zmieniać");
        EnsureVersion(final, expectedVersion);

        var errors = new List<InvoiceCheck>();
        var method = input.PaymentMethod is null
            ? final.PaymentMethod
            : ParseName(input.PaymentMethod, PaymentMethod.Transfer, "INV-VAL-071", "paymentMethod", "Forma płatności: Transfer, Cash lub Card", errors);
        ExemptionBasisKind? exemption = input.ExemptionBasisKind is null
            ? null
            : ParseName(input.ExemptionBasisKind, default(ExemptionBasisKind), "INV-VAL-072", "exemptionBasis.kind", "Podstawa zwolnienia: Act, Directive lub Other", errors);
        CheckLength(input.ExemptionBasisText, MaxShortTextLength, "exemptionBasis.text", errors);
        CheckRate(input.ExchangeRate, errors);
        errors.AddRange(ChainLockErrors(final, input.Buyer, input.Currency));
        var lines = input.Lines is null ? null : ParseLines(input.Lines, errors);
        if (errors.Count > 0)
            throw new InvoiceValidationException(errors);

        var issueDate = input.IssueDate ?? final.IssueDate;
        final.SetDates(issueDate, input.SaleDate ?? final.SaleDate, input.DueDate ?? final.DueDate);
        final.SetPayment(method, input.SplitPayment ?? final.SplitPayment);
        final.SetCurrency(null, input.ExchangeRate, input.ExchangeRateDate);
        final.SetExemptionBasis(exemption, input.ExemptionBasisText);
        if (lines is not null)
            final.ReplaceLines(lines.Select(l => (l.Name, l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate)));
        var chain = await ChainOfAsync(tenantId, final.OrderChainId!.Value, ct);
        var (finalRefs, deductedAdvances) = ChainRefs(final, chain);
        var deducted = finalRefs?.Deducted ?? [];
        final.RecalculateTotals(null, deducted);
        if (IssueChecks.ChainErrors(final, deducted, deductedAdvances.Count).ToList() is { Count: > 0 } chainErrors)
            throw new InvoiceValidationException(chainErrors);

        await ProposeRateIfMissingAsync(final, ct);
        final.Touch(Now());
        await SaveOrConflictAsync(ct);
        return await ViewAsync(final, ct);
    }

    private async Task<IssuedInvoiceView?> AddChainDocumentAsync(Guid tenantId, Guid advanceId, IssuedInvoiceType type, CancellationToken ct)
    {
        var chainId = await db.IssuedInvoices.Where(i => i.TenantId == tenantId && i.Id == advanceId).Select(i => new { i.OrderChainId }).SingleOrDefaultAsync(ct);
        if (chainId is null)
            return null;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var tenantLocked = await db.Database
            .SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM "Tenants" WHERE "Id" = {tenantId} FOR KEY SHARE""")
            .ToListAsync(ct);
        if (tenantLocked.Count == 0)
            return null;

        var chain = chainId.OrderChainId is { } id ? await LockChainAsync(tenantId, id, ct) : [];
        var source = chain.SingleOrDefault(c => c.Id == advanceId);
        if (source is not { InvoiceType: IssuedInvoiceType.Zal, Status: IssuedInvoiceStatus.Accepted })
            throw new InvoiceStateException("Kolejną zaliczkę i fakturę rozliczającą wystawisz z faktury zaliczkowej przyjętej w KSeF.", ChainSourceCode);
        if (chain.Any(c => c.InvoiceType == IssuedInvoiceType.Roz && c.Status == IssuedInvoiceStatus.Accepted))
            throw type == IssuedInvoiceType.Roz
                ? new InvoiceStateException(FinalExistsMessage, FinalExistsCode)
                : new InvoiceStateException(ChainClosedMessage, ChainClosedCode);
        if (chain.FirstOrDefault(c => c.IsOpen) is { } open)
            throw type == IssuedInvoiceType.Roz && open.InvoiceType == IssuedInvoiceType.Roz
                ? new InvoiceStateException(FinalExistsMessage, FinalExistsCode)
                : ChainOpen(open);

        var buyerState = chain.Where(c => c.Status == IssuedInvoiceStatus.Accepted && c.InvoiceType.IsAdvance())
            .OrderBy(c => c.IssuedAt)
            .ThenBy(c => c.AcceptedAt)
            .Last();
        var document = IssuedInvoice.ChainDocument(Guid.NewGuid(), type, buyerState, LatestOrder(chain)!, Today(), Now());
        if (type == IssuedInvoiceType.Roz)
            document.RecalculateTotals(null, ChainRefs(document, chain).Refs?.Deducted);
        await ProposeRateIfMissingAsync(document, ct);
        db.IssuedInvoices.Add(document);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await ViewAsync(document, ct);
    }

    private void ApplyAdvance(IssuedInvoice advance, IssuedInvoiceAdvanceInput input, IReadOnlyList<IssuedInvoice> chain)
    {
        var errors = new List<InvoiceCheck>();
        var isFirst = advance.Id == advance.OrderChainId;
        var method = input.PaymentMethod is null
            ? advance.PaymentMethod
            : ParseName(input.PaymentMethod, PaymentMethod.Transfer, "INV-VAL-071", "paymentMethod", "Forma płatności: Transfer, Cash lub Card", errors);
        ExemptionBasisKind? exemption = input.ExemptionBasisKind is null
            ? null
            : ParseName(input.ExemptionBasisKind, default(ExemptionBasisKind), "INV-VAL-072", "exemptionBasis.kind", "Podstawa zwolnienia: Act, Directive lub Other", errors);
        CheckLength(input.ExemptionBasisText, MaxShortTextLength, "exemptionBasis.text", errors);
        CheckRate(input.ExchangeRate, errors);
        CheckAmount(input.AdvanceAmount, errors);
        var currency = input.Currency?.Trim().ToUpperInvariant();
        if (isFirst)
        {
            var kind = ParseName(input.Buyer?.Kind, advance.BuyerKind, "INV-VAL-070", "buyer.kind", "Rodzaj nabywcy: Company, Person lub Foreign", errors);
            CheckBuyer(input.Buyer, errors);
            if (currency is not null && !IsLetters(currency, 3))
                errors.Add(new("INV-VAL-090", "currency", "Waluta musi być trzyliterowym kodem ISO 4217"));
            if (errors.Count == 0 && input.Buyer is { } buyer)
            {
                advance.SetBuyer(kind, buyer.Nip, buyer.Name, buyer.AddressLine1, buyer.AddressLine2);
                advance.SetForeignBuyer(buyer.CountryCode, buyer.ForeignId);
            }
        }
        else
        {
            errors.AddRange(ChainLockErrors(advance, input.Buyer, currency));
        }

        var lines = input.Lines is null ? null : ParseLines(input.Lines, errors);
        if (errors.Count > 0)
            throw new InvoiceValidationException(errors);

        var issueDate = input.IssueDate ?? advance.IssueDate;
        advance.SetCurrency(isFirst ? currency : null, input.ExchangeRate, input.ExchangeRateDate);
        advance.SetDates(issueDate, input.PaymentDate ?? advance.SaleDate, issueDate);
        advance.SetPayment(method, false);
        advance.SetExemptionBasis(exemption, input.ExemptionBasisText);
        if (input.AdvanceAmount is { } amount)
            advance.SetAdvanceAmount(amount);
        if (lines is not null)
            advance.ReplaceLines(lines.Select(l => (l.Name, l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate)));
        else
            advance.RecalculateTotals(null);

        var draftErrors = IssueChecks.ChainErrors(advance, null).Where(e => e.Code == IssueChecks.PaymentDateCode).ToList();
        if (IssueChecks.ChainAmountError(advance, OtherAdvances(chain, advance.Id)) is { } amountError)
            draftErrors.Add(amountError);
        if (draftErrors.Count > 0)
            throw new InvoiceValidationException(draftErrors);
    }

    /// <summary>Buyer and currency of a chain are fixed by its first ZAL: a request may repeat them but not change them.</summary>
    private static IEnumerable<InvoiceCheck> ChainLockErrors(IssuedInvoice document, IssuedInvoiceBuyerInput? buyer, string? currency)
    {
        const string message = "Nabywcę i walutę ustala pierwsza faktura zaliczkowa zamówienia. Nie można ich zmienić w kolejnych dokumentach.";
        static bool Differs(string? given, string? stored) => given is not null && (string.IsNullOrWhiteSpace(given) ? null : given.Trim()) != stored;
        if (buyer is not null && (
                (buyer.Kind is not null && buyer.Kind != document.BuyerKind.ToString())
                || Differs(buyer.Nip?.Replace("-", ""), document.BuyerNip)
                || Differs(buyer.Name, document.BuyerName)
                || Differs(buyer.AddressLine1, document.BuyerAddressLine1)
                || Differs(buyer.AddressLine2, document.BuyerAddressLine2)
                || Differs(buyer.ForeignId, document.BuyerForeignId)
                || (buyer.CountryCode is not null && document.BuyerKind == BuyerKind.Foreign
                    && EuCountries.FromVatPrefix(buyer.CountryCode.Trim().ToUpperInvariant()) != document.BuyerCountryCode)))
            yield return new(IssueChecks.ChainLockedCode, "buyer", message);
        if (currency is not null && currency.Trim().ToUpperInvariant() != document.Currency)
            yield return new(IssueChecks.ChainLockedCode, "currency", message);
    }

    /// <summary>Row lock on the chain's first ZAL serialises every document of the chain (ADR-007 §5). Call inside a transaction.</summary>
    private async Task<IReadOnlyList<IssuedInvoice>> LockChainAsync(Guid tenantId, Guid chainId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"""SELECT 1 FROM "IssuedInvoices" WHERE "TenantId" = {tenantId} AND "Id" = {chainId} FOR UPDATE""", ct);
        return await ChainOfAsync(tenantId, chainId, ct);
    }

    private async Task<IReadOnlyList<IssuedInvoice>> ChainOfAsync(Guid tenantId, Guid chainId, CancellationToken ct) =>
        await db.IssuedInvoices.Include(i => i.Lines)
            .Where(i => i.TenantId == tenantId && i.OrderChainId == chainId)
            .ToListAsync(ct);

    private static IReadOnlyList<IssuedInvoice> AcceptedAdvances(IReadOnlyList<IssuedInvoice> chain) =>
        chain.Where(c => c.InvoiceType == IssuedInvoiceType.Zal && c.Status == IssuedInvoiceStatus.Accepted)
            .OrderBy(c => c.IssuedAt)
            .ThenBy(c => c.IssueDate)
            .ThenBy(c => c.CreatedAt)
            .ToList();

    private static IssuedInvoice CurrentAdvance(IssuedInvoice advance, IReadOnlyList<IssuedInvoice> chain) => Corrections.CurrentState(advance, chain);

    private static decimal OtherAdvances(IReadOnlyList<IssuedInvoice> chain, Guid exceptAdvanceId) =>
        AcceptedAdvances(chain).Where(a => a.Id != exceptAdvanceId).Sum(a => CurrentAdvance(a, chain).AdvanceAmount ?? 0m);

    private static IssuedInvoice? LatestOrder(IReadOnlyList<IssuedInvoice> chain) =>
        AcceptedAdvances(chain) is { Count: > 0 } advances ? CurrentAdvance(advances[^1], chain) : null;

    private static bool HasFinal(IReadOnlyList<IssuedInvoice> chain) =>
        chain.Any(c => c.InvoiceType == IssuedInvoiceType.Roz && (c.Status == IssuedInvoiceStatus.Accepted || c.IsOpen));

    /// <summary>
    /// FakturaZaliczkowa and the deducted split. A ROZ lists every accepted ZAL whose current state is not zero;
    /// a ZAL that reaches the order value lists every earlier accepted ZAL (art. 106f ust. 4).
    /// </summary>
    private static (Fa3Refs? Refs, IReadOnlyList<IssuedInvoice> Advances) ChainRefs(IssuedInvoice invoice, IReadOnlyList<IssuedInvoice> chain)
    {
        var advances = AcceptedAdvances(chain).Where(a => a.Id != invoice.Id && a.IssuedAt <= (invoice.IssuedAt ?? DateTime.MaxValue)).ToList();
        if (invoice.InvoiceType == IssuedInvoiceType.Roz)
        {
            var listed = advances.Where(a => CurrentAdvance(a, chain).AdvanceAmount != 0).ToList();
            return (new Fa3Refs(null, null, null, listed.Select(a => a.KsefNumber!).ToList(), Advances.Deducted(advances.Select(a => CurrentAdvance(a, chain)))), listed);
        }

        var isLast = invoice.InvoiceType == IssuedInvoiceType.Zal && advances.Count > 0
            && advances.Sum(a => CurrentAdvance(a, chain).AdvanceAmount ?? 0m) + (invoice.AdvanceAmount ?? 0m) == Advances.OrderValue(invoice.Lines);
        return isLast ? (new Fa3Refs(null, null, null, advances.Select(a => a.KsefNumber!).ToList()), advances) : (null, []);
    }

    /// <summary>P_15ZK: for KOR_ZAL the advance amount of the corrected state, for KOR_ROZ the ROZ remaining amount before this correction.</summary>
    private static decimal? AmountBefore(IssuedInvoice correction, IssuedInvoice original, IEnumerable<IssuedInvoice> corrections) => correction.InvoiceType switch
    {
        IssuedInvoiceType.KorZal => Corrections.StateBefore(original, corrections.Where(c => c.Id != correction.Id).Append(correction), correction).AdvanceAmount,
        IssuedInvoiceType.KorRoz => Corrections.AcceptedChain(original, corrections.Where(c => c.Id != correction.Id).Append(correction)) is var accepted
            && accepted.ToList().FindIndex(d => d.Id == correction.Id) is var index
                ? accepted.Take(index > 0 ? index : accepted.Count).Sum(d => d.TotalGross)
                : null,
        _ => null
    };

    private OrderChainView ChainView(IssuedInvoice invoice, IReadOnlyList<IssuedInvoice> chain)
    {
        var order = invoice.InvoiceType.IsAdvance() ? invoice : LatestOrder(chain);
        var orderValue = order is null ? 0m : Advances.OrderValue(order.Lines);
        var paid = AcceptedAdvances(chain).Where(a => !(invoice.IsEditable && a.Id == invoice.Id)).Sum(a => CurrentAdvance(a, chain).AdvanceAmount ?? 0m);
        return new OrderChainView(
            orderValue,
            chain.Where(c => c.InvoiceType == IssuedInvoiceType.Zal).OrderBy(c => c.IssueDate).ThenBy(c => c.CreatedAt).ToList(),
            chain.Where(c => c.InvoiceType == IssuedInvoiceType.Roz).OrderByDescending(c => c.CreatedAt).FirstOrDefault(),
            chain.Where(c => c.InvoiceType.IsCorrection()).OrderBy(c => c.IssueDate).ThenBy(c => c.CreatedAt).ToList(),
            orderValue - paid,
            chain.FirstOrDefault(c => c.IsOpen)?.Id);
    }

    private const string ChainClosedMessage = "Zamówienie jest już rozliczone fakturą rozliczającą. Kolejnej zaliczki nie można wystawić.";
    private const string FinalExistsMessage = "To zamówienie ma już fakturę rozliczającą.";

    private static InvoiceStateException ChainOpen(IssuedInvoice open) => new(
        $"To zamówienie ma już otwarty dokument {open.Number ?? $"(szkic z {open.CreatedAt:yyyy-MM-dd})"}. Dokończ go albo usuń szkic, zanim wystawisz kolejny.",
        ChainOpenCode);

    private static InvoiceStateException UseFinalCorrection() => new(
        "To zamówienie ma fakturę rozliczającą. Zaliczek nie koryguje się już osobno: skoryguj fakturę rozliczającą (KOR_ROZ).",
        UseFinalCorrectionCode);

    private static void CheckAmount(decimal? amount, List<InvoiceCheck> errors)
    {
        if (amount is { } value && (value < 0 || value >= MaxLineNet || Math.Round(value, 2) != value))
            errors.Add(new(IssueChecks.AdvanceAmountCode, "advanceAmount", "Kwota zaliczki poza zakresem lub ponad 2 miejsca po przecinku"));
    }

    private static void CheckRate(decimal? rate, List<InvoiceCheck> errors)
    {
        if (rate is { } exchangeRate && (Math.Abs(exchangeRate) >= MaxExchangeRate || Math.Round(exchangeRate, 6) != exchangeRate))
            errors.Add(new("INV-VAL-093", "exchangeRate", "Kurs waluty poza zakresem lub ponad 6 miejsc po przecinku"));
    }

    private static void CheckBuyer(IssuedInvoiceBuyerInput? buyer, List<InvoiceCheck> errors)
    {
        if (buyer?.Nip?.Trim().Replace("-", "").Length > 10)
            errors.Add(new("INV-VAL-013", "buyer.nip", "Dla nabywcy firmy wymagany jest poprawny NIP"));
        CheckLength(buyer?.Name, MaxTextLength, "buyer.name", errors);
        CheckLength(buyer?.AddressLine1, MaxTextLength, "buyer.addressLine1", errors);
        CheckLength(buyer?.AddressLine2, MaxTextLength, "buyer.addressLine2", errors);
        CheckLength(buyer?.ForeignId, MaxForeignIdLength, "buyer.foreignId", errors);
        var country = buyer?.CountryCode?.Trim();
        if (!string.IsNullOrEmpty(country) && !IsLetters(country, 2))
            errors.Add(new("INV-VAL-030", "buyer.countryCode", "Podaj kraj nabywcy inny niż Polska"));
    }

    private async Task<IssuedInvoiceView> AddCopyAsync(Guid tenantId, InvoiceTemplate snapshot, CancellationToken ct)
    {
        var today = Today();
        var copy = IssuedInvoice.Draft(Guid.NewGuid(), tenantId, today, Now());
        copy.SetBuyer(snapshot.BuyerKind, snapshot.BuyerNip, snapshot.BuyerName, snapshot.BuyerAddressLine1, snapshot.BuyerAddressLine2);
        copy.SetForeignBuyer(snapshot.BuyerCountryCode, snapshot.BuyerForeignId);
        copy.SetCurrency(snapshot.Currency, null, null);
        copy.SetDates(today, today, today.AddDays(snapshot.PaymentDays));
        copy.SetPayment(snapshot.PaymentMethod, snapshot.SplitPayment);
        copy.SetExemptionBasis(snapshot.ExemptionBasisKind, snapshot.ExemptionBasisText);
        copy.ReplaceLines(snapshot.Lines.OrderBy(l => l.LineNo).Select(l => ((string?)l.Name, (string?)l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate)));
        await ProposeRateIfMissingAsync(copy, ct);
        db.IssuedInvoices.Add(copy);
        await db.SaveChangesAsync(ct);
        return await ViewAsync(copy, ct);
    }

    /// <summary>Row lock on the original serialises corrections, so they stay linear (ADR-007 §5). Call inside a transaction.</summary>
    private async Task<(IssuedInvoice Original, IReadOnlyList<IssuedInvoice> Corrections)> LockOriginalAsync(Guid tenantId, Guid originalId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"""SELECT 1 FROM "IssuedInvoices" WHERE "TenantId" = {tenantId} AND "Id" = {originalId} FOR UPDATE""", ct);
        var original = await LoadAsync(tenantId, originalId, ct) ?? throw new InvoiceStateException("Nie znaleziono faktury korygowanej.");
        return (original, await CorrectionsOfAsync(tenantId, originalId, ct));
    }

    private async Task<IReadOnlyList<IssuedInvoice>> CorrectionsOfAsync(Guid tenantId, Guid originalId, CancellationToken ct) =>
        await db.IssuedInvoices.Include(i => i.Lines)
            .Where(i => i.TenantId == tenantId && i.CorrectedInvoiceId == originalId)
            .ToListAsync(ct);

    private static InvoiceStateException OpenCorrection(IssuedInvoice open) => new(
        $"Ta faktura ma już otwartą korektę {open.Number ?? $"(szkic z {open.CreatedAt:yyyy-MM-dd})"}. Dokończ ją albo usuń szkic, zanim wystawisz kolejną.",
        CorrectionOpenCode);

    private static void EnsureKind(IssuedInvoice invoice, string? invoiceType)
    {
        if ((invoiceType ?? "VAT") != invoice.InvoiceType.Code())
            throw new InvoiceStateException(KindMismatchMessage, KindMismatchCode);
    }

    private static IEnumerable<InvoiceCheck> BuyerIdChanges(IssuedInvoice correction, IssuedInvoiceBuyerInput buyer, string? idType)
    {
        const string message = "Korekta nie może zmienić rodzaju ani identyfikatora nabywcy. Przy błędnym NIP użyj „Błędny NIP nabywcy”.";
        if (buyer.Kind is not null && buyer.Kind != correction.BuyerKind.ToString())
            yield return new(IssueChecks.BuyerIdChangeCode, "buyer.kind", message);
        if (buyer.Nip is not null && (string.IsNullOrWhiteSpace(buyer.Nip) ? null : buyer.Nip.Trim().Replace("-", "")) != correction.BuyerNip)
            yield return new(IssueChecks.BuyerIdChangeCode, "buyer.nip", message);
        if (buyer.ForeignId is not null && (string.IsNullOrWhiteSpace(buyer.ForeignId) ? null : buyer.ForeignId.Trim()) is var foreignId
            && foreignId != correction.BuyerForeignId
            && !(correction.BuyerCountryCode is { } country && EuCountries.Contains(country) && foreignId is not null && EuCountries.VatNumberWithoutPrefix(foreignId, country) == correction.BuyerForeignId))
            yield return new(IssueChecks.BuyerIdChangeCode, "buyer.foreignId", message);
        if (idType is not null && idType != correction.BuyerIdType)
            yield return new(IssueChecks.BuyerIdChangeCode, "buyer.idType", message);
        if (buyer.CountryCode is not null && correction.BuyerForeignId is not null
            && EuCountries.FromVatPrefix(buyer.CountryCode.Trim().ToUpperInvariant()) != correction.BuyerCountryCode)
            yield return new(IssueChecks.BuyerIdChangeCode, "buyer.countryCode", message);
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

    public static bool IsUniqueViolation(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private void Apply(IssuedInvoice invoice, IssuedInvoiceDraftInput input)
    {
        var errors = new List<InvoiceCheck>();

        var kind = ParseName(input.Buyer?.Kind, BuyerKind.Company, "INV-VAL-070", "buyer.kind", "Rodzaj nabywcy: Company, Person lub Foreign", errors);
        var method = ParseName(input.PaymentMethod, PaymentMethod.Transfer, "INV-VAL-071", "paymentMethod", "Forma płatności: Transfer, Cash lub Card", errors);
        ExemptionBasisKind? exemption = input.ExemptionBasisKind is null
            ? null
            : ParseName(input.ExemptionBasisKind, default(ExemptionBasisKind), "INV-VAL-072", "exemptionBasis.kind", "Podstawa zwolnienia: Act, Directive lub Other", errors);

        CheckBuyer(input.Buyer, errors);
        CheckLength(input.ExemptionBasisText, MaxShortTextLength, "exemptionBasis.text", errors);

        var currency = input.Currency?.Trim().ToUpperInvariant();
        if (currency is not null && !IsLetters(currency, 3))
            errors.Add(new("INV-VAL-090", "currency", "Waluta musi być trzyliterowym kodem ISO 4217"));
        CheckRate(input.ExchangeRate, errors);

        var parsedLines = ParseLines(input.Lines ?? [], errors);

        if (errors.Count > 0)
            throw new InvoiceValidationException(errors);

        var issueDate = input.IssueDate ?? Today();
        invoice.SetBuyer(kind, input.Buyer?.Nip, input.Buyer?.Name, input.Buyer?.AddressLine1, input.Buyer?.AddressLine2);
        invoice.SetForeignBuyer(input.Buyer?.CountryCode, input.Buyer?.ForeignId);
        invoice.SetCurrency(currency, input.ExchangeRate, input.ExchangeRateDate);
        invoice.SetDates(issueDate, input.SaleDate ?? issueDate, input.DueDate ?? issueDate.AddDays(IssuedInvoice.DefaultPaymentDays));
        invoice.SetPayment(method, input.SplitPayment);
        invoice.SetExemptionBasis(exemption, input.ExemptionBasisText);
        invoice.ReplaceLines(parsedLines.Select(l => (l.Name, l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate)));
    }

    private static List<(int? BaseLineNo, string? Name, string? Unit, decimal Quantity, decimal UnitNetPrice, VatRate VatRate)> ParseLines(
        IReadOnlyList<IssuedInvoiceLineInput> lines, List<InvoiceCheck> errors)
    {
        if (lines.Count > MaxLines)
            errors.Add(new("INV-VAL-003", "lines", $"Faktura może mieć najwyżej {MaxLines} pozycji"));

        var parsedLines = new List<(int? BaseLineNo, string? Name, string? Unit, decimal Quantity, decimal UnitNetPrice, VatRate VatRate)>();
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
                parsedLines.Add((line.BaseLineNo, line.Name, line.Unit, line.Quantity, line.UnitNetPrice, rate));
            else
                errors.Add(new("INV-VAL-060", $"{field}.vatRate", "Dozwolone stawki VAT: 23, 8, 5, 0 KR, zw"));
        }

        return parsedLines;
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

    private uint VersionOf(IssuedInvoice invoice) => db.Entry(invoice).Property<uint>("Version").CurrentValue;

    private uint SellerVersionOf(SellerProfile profile) =>
        db.Entry(profile).State == EntityState.Detached ? 0 : db.Entry(profile).Property<uint>("Version").CurrentValue;

    private void EnsureVersion(IssuedInvoice invoice, uint? expected)
    {
        if (expected is { } version && version != VersionOf(invoice))
            throw new InvoiceStateException(ConcurrentChangeMessage);
    }

    private async Task SaveOrConflictAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvoiceStateException(ConcurrentChangeMessage);
        }
    }

    private async Task<IssuedInvoiceView> ViewAsync(IssuedInvoice invoice, CancellationToken ct)
    {
        var view = await DocumentViewAsync(invoice, ct);
        if (invoice.OrderChainId is not { } chainId)
            return view;

        var chain = (await db.IssuedInvoices.AsNoTracking().Include(i => i.Lines)
                .Where(i => i.TenantId == invoice.TenantId && i.OrderChainId == chainId && i.Id != invoice.Id)
                .ToListAsync(ct))
            .Append(invoice)
            .ToList();
        if (invoice.InvoiceType.IsCorrection())
            return view with { Chain = ChainView(invoice, chain) };
        var (refs, advances) = ChainRefs(invoice, chain);
        return view with { Chain = ChainView(invoice, chain), AdvanceInvoices = advances, Deducted = refs?.Deducted };
    }

    private async Task<IssuedInvoiceView> DocumentViewAsync(IssuedInvoice invoice, CancellationToken ct)
    {
        if (invoice.InvoiceType.IsCorrection())
        {
            var original = await db.IssuedInvoices.AsNoTracking().Include(i => i.Lines)
                .SingleAsync(i => i.TenantId == invoice.TenantId && i.Id == invoice.CorrectedInvoiceId, ct);
            var siblings = await db.IssuedInvoices.AsNoTracking().Include(i => i.Lines)
                .Where(i => i.TenantId == invoice.TenantId && i.CorrectedInvoiceId == original.Id && i.Id != invoice.Id)
                .ToListAsync(ct);
            var sellerVersion = invoice.IsEditable
                ? await db.SellerProfiles.AsNoTracking().Where(p => p.TenantId == invoice.TenantId).Select(p => EF.Property<uint>(p, "Version")).SingleOrDefaultAsync(ct)
                : 0;
            return SnapshotView(invoice) with
            {
                SellerVersion = sellerVersion,
                Original = original,
                CorrectedState = Corrections.StateBefore(original, siblings.Append(invoice), invoice),
                AmountBefore = AmountBefore(invoice, original, siblings)
            };
        }

        if (!invoice.IsEditable)
        {
            if (invoice.Status != IssuedInvoiceStatus.Accepted)
                return SnapshotView(invoice);
            var corrections = await db.IssuedInvoices.AsNoTracking().Include(i => i.Lines)
                .Where(i => i.TenantId == invoice.TenantId && i.CorrectedInvoiceId == invoice.Id)
                .ToListAsync(ct);
            var current = Corrections.CurrentState(invoice, corrections);
            return SnapshotView(invoice) with { Corrections = corrections, CurrentState = current == invoice ? null : current };
        }

        var nip = await db.Tenants.Where(t => t.Id == invoice.TenantId).Select(t => t.Nip).SingleAsync(ct);
        var row = await db.SellerProfiles.AsNoTracking()
            .Where(p => p.TenantId == invoice.TenantId)
            .Select(p => new { Profile = p, Version = EF.Property<uint>(p, "Version") })
            .SingleOrDefaultAsync(ct);
        var profile = row?.Profile;
        var account = profile?.PaymentAccountFor(invoice.BuyerKind);
        var seller = new IssuedInvoiceSeller(nip, profile?.LegalName, profile?.AddressLine1, profile?.AddressLine2, account?.BankAccount, profile?.CashAccounting ?? false,
            account?.Iban, account?.Swift, account?.BankName);
        return new IssuedInvoiceView(invoice, seller, environment.Current, IssueChecks.Warnings(invoice), VersionOf(invoice), row?.Version ?? 0);
    }

    private IssuedInvoiceView SnapshotView(IssuedInvoice invoice)
    {
        var snapshot = new IssuedInvoiceSeller(invoice.SellerNip!, invoice.SellerName, invoice.SellerAddressLine1,
            invoice.SellerAddressLine2, invoice.SellerBankAccount, invoice.SellerCashAccounting,
            invoice.SellerIban, invoice.SellerBankSwift, invoice.SellerBankName);
        return new IssuedInvoiceView(invoice, snapshot, invoice.KsefEnvironment ?? environment.Current, IssueChecks.Warnings(invoice), VersionOf(invoice));
    }

    private DateOnly Today() => IssuedInvoice.PolandDate(timeProvider.GetUtcNow());

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
