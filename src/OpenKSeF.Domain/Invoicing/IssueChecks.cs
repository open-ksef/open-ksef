using System.Text.RegularExpressions;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Invoicing;

public static class IssueChecks
{
    public const decimal SplitPaymentWarningThreshold = 15000m;
    public const string NumberPeriodCode = "INV-VAL-022";
    public const string NumberTakenCode = "INV-VAL-023";
    public const string PastIssueDateCode = "INV-W-PAST-DATE";
    public const int PastIssueDateDays = 30;
    public const string NoChangeCode = "INV-VAL-082";
    public const string BuyerIdChangeCode = "INV-VAL-083";
    public const string CorrectionReasonCode = "INV-VAL-084";
    public const string CorrectionIssueDateCode = "INV-VAL-085";
    public const string BaseLineCode = "INV-VAL-086";
    public const string AdvanceAmountCode = "INV-VAL-095";
    public const string PaymentDateCode = "INV-VAL-096";
    public const string AdvancesExceedCode = "INV-VAL-097";
    public const string ChainLockedCode = "INV-VAL-098";
    public const string LateAdvanceCode = "INV-W-ZAL-LATE";

    private static readonly Regex VatUeNumber = new(@"^[0-9A-Z+*]{1,12}$", RegexOptions.CultureInvariant);

    public static InvoiceCheck? PastIssueDate(IssuedInvoice invoice, DateOnly today) =>
        invoice.IssueDate < today.AddDays(-PastIssueDateDays)
            ? new(PastIssueDateCode, "issueDate",
                $"Data wystawienia {invoice.IssueDate:yyyy-MM-dd} jest starsza niż {PastIssueDateDays} dni. Sprawdź rok i miesiąc. " +
                "Faktura wysłana do KSeF po dacie wystawienia jest fakturą offline (art. 106nda ustawy o VAT). Potwierdź, aby wystawić ją z tą datą.")
            : null;

    public static IReadOnlyList<InvoiceCheck> Errors(IssuedInvoice invoice, SellerProfile? profile, DateOnly today)
    {
        var errors = new List<InvoiceCheck>();

        if (string.IsNullOrWhiteSpace(profile?.LegalName))
            errors.Add(new("INV-VAL-010", "seller.name", "Uzupełnij nazwę firmy w danych do faktur"));
        if (string.IsNullOrWhiteSpace(profile?.AddressLine1))
            errors.Add(new("INV-VAL-011", "seller.addressLine1", "Uzupełnij adres firmy w danych do faktur"));
        var usesForeignAccount = invoice.BuyerKind == BuyerKind.Foreign && profile?.ForeignBankAccount is not null;
        if (invoice.PaymentMethod == PaymentMethod.Transfer && !usesForeignAccount && !Nrb.IsValid(profile?.BankAccount))
            errors.Add(new("INV-VAL-012", "seller.bankAccount", "Uzupełnij numer rachunku w danych firmy"));

        if (invoice.BuyerKind == BuyerKind.Company && !NipValidator.HasValidChecksum(invoice.BuyerNip))
            errors.Add(new("INV-VAL-013", "buyer.nip", "Dla nabywcy firmy wymagany jest poprawny NIP"));
        if (invoice.BuyerKind == BuyerKind.Person && invoice.BuyerNip is not null)
            errors.Add(new("INV-VAL-014", "buyer.nip", "Dla osoby prywatnej nie podawaj NIP"));
        if (invoice.BuyerKind == BuyerKind.Foreign)
            AddForeignBuyerErrors(invoice, errors);
        if (invoice.BuyerName is null)
            errors.Add(new("INV-VAL-015", "buyer.name", "Uzupełnij nazwę nabywcy"));
        if (invoice.BuyerAddressLine1 is null)
            errors.Add(new("INV-VAL-016", "buyer.addressLine1", "Uzupełnij adres nabywcy"));

        if (invoice.IssueDate > today)
            errors.Add(new("INV-VAL-020", "issueDate", "Data wystawienia nie może być późniejsza niż dzisiejsza"));
        if (invoice.DueDate < invoice.IssueDate)
            errors.Add(new("INV-VAL-021", "dueDate", "Termin płatności jest wcześniejszy niż data wystawienia"));

        if (invoice.IsNumberTakenInKsef)
        {
            var found = invoice.DuplicateKsefNumber is { } ksefNumber ? $" Numer KSeF: {ksefNumber}." : "";
            errors.Add(new(NumberTakenCode, "number",
                $"Numer {invoice.Number} jest już w KSeF, więc nie można go wysłać ponownie.{found} Sprawdź tę fakturę w KSeF. Jeśli to ta sama sprzedaż, nie wystawiaj jej ponownie."));
        }

        if (invoice.Lines.Count == 0)
            errors.Add(new("INV-VAL-002", "lines", "Faktura musi zawierać co najmniej jedną pozycję"));

        AddLineErrors(invoice, errors);

        if (invoice.Currency != IssuedInvoice.DefaultCurrency)
        {
            if (invoice.BuyerKind != BuyerKind.Foreign)
                errors.Add(new("INV-VAL-091", "currency", "Waluta inna niż PLN jest dostępna tylko dla firmy zagranicznej"));
            else if (invoice.InvoiceType != IssuedInvoiceType.Pro && invoice.ExchangeRate is not > 0)
                errors.Add(new("INV-VAL-092", "exchangeRate", "Podaj kurs waluty większy od zera"));
        }

        return errors;
    }

    public static IReadOnlyList<InvoiceCheck> Warnings(IssuedInvoice invoice)
    {
        var warnings = new List<InvoiceCheck>();

        if (invoice.BuyerKind == BuyerKind.Company && invoice.BuyerNip is not null && !NipValidator.HasValidChecksum(invoice.BuyerNip))
            warnings.Add(new("INV-W-NIP", "buyer.nip", "NIP nabywcy ma niepoprawną sumę kontrolną"));
        if (invoice.BuyerKind != BuyerKind.Foreign && invoice.TotalGross > SplitPaymentWarningThreshold && !invoice.SplitPayment)
            warnings.Add(new("INV-W-MPP", "splitPayment", "Sprawdź, czy faktura wymaga mechanizmu podzielonej płatności (załącznik nr 15)"));
        if (invoice.BuyerKind == BuyerKind.Foreign && NipValidator.HasValidChecksum(ForeignIdLettersAndDigits(invoice)))
            warnings.Add(new("INV-W-FOREIGN-NIP", "buyer.foreignId", "Numer nabywcy wygląda jak polski NIP. Nabywca z polskim NIP to firma, nie firma zagraniczna"));
        if (invoice.InvoiceType == IssuedInvoiceType.Zal && invoice.IssueDate > new DateOnly(invoice.SaleDate.Year, invoice.SaleDate.Month, 15).AddMonths(1))
            warnings.Add(new(LateAdvanceCode, "issueDate", "Fakturę zaliczkową wystawia się do 15. dnia miesiąca po miesiącu otrzymania zapłaty (art. 106i ust. 2)."));

        return warnings;
    }

    private static void AddLineErrors(IssuedInvoice invoice, List<InvoiceCheck> errors)
    {
            foreach (var line in invoice.Lines)
            {
                var field = $"lines[{line.LineNo - 1}]";
                if (line.Name.Length == 0)
                    errors.Add(new("INV-VAL-050", $"{field}.name", "Każda pozycja musi mieć nazwę"));
                if (line.Quantity <= 0)
                    errors.Add(new("INV-VAL-051", $"{field}.quantity", "Ilość na pozycji musi być większa od zera"));
                if (line.UnitNetPrice < 0)
                    errors.Add(new("INV-VAL-052", $"{field}.unitNetPrice", "Cena netto nie może być ujemna"));
                if (invoice.BuyerKind == BuyerKind.Foreign)
                    AddForeignRateError(invoice, line, field, errors);
                else if (!VatRates.Issuable.Contains(line.VatRate))
                    errors.Add(new("INV-VAL-060", $"{field}.vatRate", "Dozwolone stawki VAT: 23, 8, 5, 0 KR, zw"));
            }

            if (invoice.Lines.Any(l => l.VatRate == VatRate.Exempt)
                && (invoice.ExemptionBasisKind is null || invoice.ExemptionBasisText is null))
                errors.Add(new("INV-VAL-061", "exemptionBasis", "Podaj podstawę zwolnienia z VAT"));
    }

    /// <summary>REQ-0028 checks of a ZAL or ROZ that need no other document of the chain. The remaining-value check is the service's (ChainAmountError).</summary>
    public static IEnumerable<InvoiceCheck> ChainErrors(IssuedInvoice invoice, IReadOnlyList<VatSummary>? deducted, int advances = 0)
    {
        if (invoice.InvoiceType == IssuedInvoiceType.Zal)
        {
            if (invoice.AdvanceAmount is not > 0)
                yield return new(AdvanceAmountCode, "advanceAmount", "Podaj otrzymaną kwotę zaliczki większą od zera");
            if (invoice.SaleDate > invoice.IssueDate)
                yield return new(PaymentDateCode, "paymentDate", "Data otrzymania zapłaty nie może być późniejsza niż data wystawienia faktury zaliczkowej");
        }

        if (invoice.InvoiceType == IssuedInvoiceType.Roz && Advances.Exceeds(invoice.Buckets(null, deducted), advances))
            yield return new(AdvancesExceedCode, "lines", Advances.ExceedsMessage);
    }

    /// <summary>The amount of a ZAL or KOR_ZAL may not exceed the order value minus the other advances of the chain.</summary>
    public static InvoiceCheck? ChainAmountError(IssuedInvoice invoice, decimal otherAdvances)
    {
        var remaining = Advances.OrderValue(invoice.Lines) - otherAdvances;
        return invoice.AdvanceAmount > remaining || invoice.AdvanceAmount < 0
            ? new(AdvanceAmountCode, "advanceAmount", $"Kwota zaliczki przekracza pozostałą wartość zamówienia ({remaining:0.00} {invoice.Currency})")
            : null;
    }

    /// <summary>REQ-0027 checks of a KOR draft against its original and the state it corrects. Seller data is the snapshot of the original.</summary>
    public static IReadOnlyList<InvoiceCheck> CorrectionErrors(IssuedInvoice correction, IssuedInvoice original, IssuedInvoice state, DateOnly today)
    {
        var errors = BaseLineErrors(correction, state).ToList();
        var linesResolve = errors.Count == 0;

        if (correction.BuyerName is null)
            errors.Add(new("INV-VAL-015", "buyer.name", "Uzupełnij nazwę nabywcy"));
        if (correction.BuyerAddressLine1 is null)
            errors.Add(new("INV-VAL-016", "buyer.addressLine1", "Uzupełnij adres nabywcy"));
        if (correction.CorrectionReason is null)
            errors.Add(new(CorrectionReasonCode, "correctionReason", "Podaj przyczynę korekty"));
        if (correction.IssueDate < original.IssueDate)
            errors.Add(new(CorrectionIssueDateCode, "issueDate", $"Data korekty nie może być wcześniejsza niż data wystawienia faktury korygowanej ({original.IssueDate:yyyy-MM-dd})"));
        if (correction.IssueDate > today)
            errors.Add(new("INV-VAL-020", "issueDate", "Data wystawienia nie może być późniejsza niż dzisiejsza"));
        if (correction.DueDate < correction.IssueDate)
            errors.Add(new("INV-VAL-021", "dueDate", "Termin płatności jest wcześniejszy niż data wystawienia"));
        if (correction.IsNumberTakenInKsef)
            errors.Add(new(NumberTakenCode, "number", $"Numer {correction.Number} jest już w KSeF, więc nie można go wysłać ponownie."));
        if (linesResolve && Corrections.Rows(state.Lines, correction.Lines).Count == 0 && !Corrections.BuyerDataDiffers(correction, state) && correction.AdvanceAmount == state.AdvanceAmount)
            errors.Add(new(NoChangeCode, null, "Korekta nie zawiera zmian. Zmień pozycje albo dane nabywcy."));

        AddLineErrors(correction, errors);
        return errors;
    }

    public static IEnumerable<InvoiceCheck> BaseLineErrors(IssuedInvoice correction, IssuedInvoice state)
    {
        var stateLines = state.Lines.Select(l => l.LineNo).ToHashSet();
        var seen = new HashSet<int>();
        foreach (var line in correction.Lines.Where(l => l.BaseLineNo is not null))
        {
            if (!stateLines.Contains(line.BaseLineNo!.Value) || !seen.Add(line.BaseLineNo.Value))
                yield return new(BaseLineCode, $"lines[{line.LineNo - 1}].baseLineNo", "Pozycja korygowana nie istnieje w aktualnym stanie faktury. Odśwież stronę.");
        }
    }

    private static string ForeignIdLettersAndDigits(IssuedInvoice invoice) =>
        new string((invoice.BuyerForeignId ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static void AddForeignBuyerErrors(IssuedInvoice invoice, List<InvoiceCheck> errors)
    {
        if (invoice.BuyerCountryCode is null or "PL")
            errors.Add(new("INV-VAL-030", "buyer.countryCode", "Podaj kraj nabywcy inny niż Polska"));
        if (invoice.BuyerNip is not null)
            errors.Add(new("INV-VAL-031", "buyer.nip", "Nabywca z polskim NIP to firma, nie firma zagraniczna"));
        if (ForeignIdLettersAndDigits(invoice) is ['P', 'L', .. var nip] && NipValidator.HasValidChecksum(nip))
            errors.Add(new("INV-VAL-031", "buyer.foreignId", "Nabywca z polskim NIP to firma, nie firma zagraniczna"));
        if (EuCountries.Contains(invoice.BuyerCountryCode) && !VatUeNumber.IsMatch(invoice.BuyerForeignId ?? ""))
            errors.Add(new("INV-VAL-032", "buyer.vatUe", "Dla nabywcy z UE podaj numer VAT UE: do 12 liter lub cyfr, bez kodu kraju"));
    }

    private static void AddForeignRateError(IssuedInvoice invoice, IssuedInvoiceLine line, string field, List<InvoiceCheck> errors)
    {
        var isEu = EuCountries.Contains(invoice.BuyerCountryCode);
        if (!VatRates.IssuableForeign.Contains(line.VatRate))
            errors.Add(new("INV-VAL-062", $"{field}.vatRate", "Dla firmy zagranicznej dozwolone stawki to np I i np II"));
        else if (line.VatRate == VatRate.NotTaxable && isEu)
            errors.Add(new("INV-VAL-063", $"{field}.vatRate", "Stawka np I dotyczy nabywcy spoza UE. Dla nabywcy z UE wybierz np II"));
        else if (line.VatRate == VatRate.NotTaxableArt100 && !isEu)
            errors.Add(new("INV-VAL-064", $"{field}.vatRate", "Stawka np II dotyczy nabywcy z UE. Dla nabywcy spoza UE wybierz np I"));
    }
}
