using System.Text.RegularExpressions;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Invoicing;

public static class IssueChecks
{
    public const decimal SplitPaymentWarningThreshold = 15000m;
    public const string NumberPeriodCode = "INV-VAL-022";
    public const string NumberTakenCode = "INV-VAL-023";

    private static readonly Regex VatUeNumber = new(@"^[0-9A-Z+*]{1,12}$", RegexOptions.CultureInvariant);

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

        if (invoice.Currency != IssuedInvoice.DefaultCurrency)
        {
            if (invoice.BuyerKind != BuyerKind.Foreign)
                errors.Add(new("INV-VAL-091", "currency", "Waluta inna niż PLN jest dostępna tylko dla firmy zagranicznej"));
            else if (invoice.ExchangeRate is not > 0)
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

        return warnings;
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
