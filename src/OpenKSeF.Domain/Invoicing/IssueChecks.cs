using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Invoicing;

public static class IssueChecks
{
    public const decimal SplitPaymentWarningThreshold = 15000m;

    public static IReadOnlyList<InvoiceCheck> Errors(IssuedInvoice invoice, SellerProfile? profile)
    {
        var errors = new List<InvoiceCheck>();

        if (string.IsNullOrWhiteSpace(profile?.LegalName))
            errors.Add(new("INV-VAL-010", "seller.name", "Uzupełnij nazwę firmy w danych do faktur"));
        if (string.IsNullOrWhiteSpace(profile?.AddressLine1))
            errors.Add(new("INV-VAL-011", "seller.addressLine1", "Uzupełnij adres firmy w danych do faktur"));
        if (invoice.PaymentMethod == PaymentMethod.Transfer && !Nrb.IsValid(profile?.BankAccount))
            errors.Add(new("INV-VAL-012", "seller.bankAccount", "Uzupełnij numer rachunku w danych firmy"));

        if (invoice.BuyerKind == BuyerKind.Company && !NipValidator.HasValidChecksum(invoice.BuyerNip))
            errors.Add(new("INV-VAL-013", "buyer.nip", "Dla nabywcy firmy wymagany jest poprawny NIP"));
        if (invoice.BuyerKind == BuyerKind.Person && invoice.BuyerNip is not null)
            errors.Add(new("INV-VAL-014", "buyer.nip", "Dla osoby prywatnej nie podawaj NIP"));
        if (invoice.BuyerName is null)
            errors.Add(new("INV-VAL-015", "buyer.name", "Uzupełnij nazwę nabywcy"));
        if (invoice.BuyerAddressLine1 is null)
            errors.Add(new("INV-VAL-016", "buyer.addressLine1", "Uzupełnij adres nabywcy"));

        if (invoice.DueDate < invoice.IssueDate)
            errors.Add(new("INV-VAL-021", "dueDate", "Termin płatności jest wcześniejszy niż data wystawienia"));

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
            if (!VatRates.Issuable.Contains(line.VatRate))
                errors.Add(new("INV-VAL-060", $"{field}.vatRate", "Dozwolone stawki VAT: 23, 8, 5, 0 KR, zw"));
        }

        if (invoice.Lines.Any(l => l.VatRate == VatRate.Exempt)
            && (invoice.ExemptionBasisKind is null || invoice.ExemptionBasisText is null))
            errors.Add(new("INV-VAL-061", "exemptionBasis", "Podaj podstawę zwolnienia z VAT"));

        return errors;
    }

    public static IReadOnlyList<InvoiceCheck> Warnings(IssuedInvoice invoice)
    {
        var warnings = new List<InvoiceCheck>();

        if (invoice.BuyerKind == BuyerKind.Company && invoice.BuyerNip is not null && !NipValidator.HasValidChecksum(invoice.BuyerNip))
            warnings.Add(new("INV-W-NIP", "buyer.nip", "NIP nabywcy ma niepoprawną sumę kontrolną"));
        if (invoice.TotalGross > SplitPaymentWarningThreshold && !invoice.SplitPayment)
            warnings.Add(new("INV-W-MPP", "splitPayment", "Sprawdź, czy faktura wymaga mechanizmu podzielonej płatności (załącznik nr 15)"));

        return warnings;
    }
}
