using System.Globalization;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Domain.Services;

public class TransferDetailsService : ITransferDetailsService
{
    private const decimal MppNoticeThreshold = 15000m;
    private static readonly CultureInfo PolishCulture = CultureInfo.GetCultureInfo("pl-PL");
    private static readonly string[] QrTypes = ["Vat", "Zal", "Roz", "Upr"];

    public string BuildTransferText(InvoiceHeader invoice, string tenantNip)
    {
        var data = BuildTransferData(invoice, tenantNip);
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(data.RecipientName))
            lines.Add($"Odbiorca: {data.RecipientName}");

        if (!string.IsNullOrWhiteSpace(data.RecipientNip))
            lines.Add($"NIP: {data.RecipientNip}");

        if (!string.IsNullOrWhiteSpace(data.RecipientAccount))
            lines.Add($"Nr rachunku: {data.RecipientAccount}");

        var amount = $"{Money(data.Amount)} {data.Currency}";
        lines.Add(invoice.Settlement is null or SettlementState.Unconfirmed
            ? $"Kwota brutto z faktury: {amount} (sprawdź kwotę do zapłaty na fakturze)"
            : $"Kwota: {amount}");
        lines.Add($"Tytul: {data.Title}");

        return string.Join("\n", lines);
    }

    public TransferData BuildTransferData(InvoiceHeader invoice, string tenantNip)
    {
        var factor = invoice.FactorBankAccount is not null;
        var data = new TransferData
        {
            RecipientName = factor ? invoice.FactorName ?? string.Empty : invoice.VendorName,
            RecipientNip = factor ? invoice.FactorNip ?? string.Empty : invoice.VendorNip,
            RecipientAccount = factor ? invoice.FactorBankAccount : invoice.VendorBankAccount,
            Amount = invoice.Settlement switch
            {
                SettlementState.Overpaid or SettlementState.NothingDue => 0m,
                SettlementState.Confirmed => invoice.SettlementAmount ?? invoice.AmountGross,
                _ => invoice.AmountGross
            },
            Currency = invoice.Currency,
            Title = invoice.InvoiceNumber ?? invoice.KSeFInvoiceNumber
        };

        if (string.Equals(invoice.VendorNip.Trim(), tenantNip.Trim(), StringComparison.Ordinal))
            return data;

        var blocked = GetQrBlockMessage(invoice, data);
        if (blocked is not null)
        {
            data.QrMessage = blocked;
            return data;
        }

        var notices = new List<string>();
        if (IsType(invoice, "Zal") || IsType(invoice, "Roz"))
            notices.Add("Zaliczka/rozliczenie: wpisz kwotę ręcznie.");
        if (invoice.PartialPayment == 1)
            notices.Add("Faktura opłacona w części: wpisz pozostałą kwotę ręcznie, sprawdź ją na fakturze.");
        if (invoice.Settlement is null)
            notices.Add("Nie odczytano jeszcze rozliczeń z tej faktury: wpisz kwotę ręcznie, sprawdź ją na fakturze. Aby uzyskać kwotę w kodzie QR, uruchom pełną resynchronizację (portal: Firma, zakładka Token KSeF i pobieranie).");
        if (invoice.Settlement == SettlementState.Unconfirmed)
            notices.Add("Faktura zawiera dodatkowe rozliczenia i nie można potwierdzić kwoty do zapłaty: wpisz kwotę ręcznie, sprawdź ją na fakturze.");
        data.ManualAmount = notices.Count > 0;

        if (!data.ManualAmount && invoice.Settlement == SettlementState.Confirmed && data.Amount != invoice.AmountGross)
            notices.Add("Kwota w kodzie QR to kwota do zapłaty z faktury, po uwzględnieniu dodatkowych rozliczeń.");
        if (factor)
            notices.Add("Płatność na rachunek faktora.");
        if (invoice.Settlement is null && invoice.AmountGross > MppNoticeThreshold)
            notices.Add("Sprawdź, czy faktura nie wymaga podzielonej płatności (MPP).");
        if (QrCodeService.IsTitleTruncated(data.Title))
            notices.Add($"Numer faktury nie mieści się w tytule przelewu (32 znaki) i został skrócony. Sprawdź tytuł w banku. Pełny numer: {data.Title}.");

        data.QrAvailable = true;
        data.QrMessage = notices.Count > 0 ? string.Join(" ", notices) : null;
        return data;
    }

    private static string? GetQrBlockMessage(InvoiceHeader invoice, TransferData data)
    {
        if (!string.Equals(invoice.Currency, "PLN", StringComparison.OrdinalIgnoreCase))
            return $"Faktura w walucie {invoice.Currency}: kod QR obsługuje tylko przelew w PLN.";
        if (invoice.InvoiceType?.StartsWith("Kor", StringComparison.OrdinalIgnoreCase) == true)
            return "Faktura korygująca: kwota to różnica. Sprawdź, czy jest coś do zapłaty.";
        if (string.IsNullOrWhiteSpace(invoice.InvoiceType))
            return "Nie można ustalić rodzaju faktury. Sprawdź kwotę na fakturze. Aby uzyskać kod QR, uruchom pełną resynchronizację (portal: Firma, zakładka Token KSeF i pobieranie).";
        if (!QrTypes.Any(t => IsType(invoice, t)))
            return invoice.InvoiceType.ToUpperInvariant() switch
            {
                "VATRR" => "Faktura VAT RR: kod QR nie jest dostępny dla tego rodzaju faktury. Sprawdź dane do zapłaty na fakturze.",
                "VATPEF" or "VATPEFSP" => "Faktura PEF: kod QR nie jest dostępny dla tego rodzaju faktury. Sprawdź dane do zapłaty na fakturze.",
                _ => $"Nieznany rodzaj faktury ({invoice.InvoiceType}): kod QR nie jest dostępny. Sprawdź dane do zapłaty na fakturze."
            };
        if (invoice.Settlement == SettlementState.Overpaid)
            return $"Faktura wykazuje nadpłatę {Money(invoice.SettlementAmount ?? 0m)} {invoice.Currency} do rozliczenia lub zwrotu: nie ma kwoty do zapłaty.";
        if ((invoice.SettlementAmount ?? invoice.AmountGross) <= 0m)
            return "Brak kwoty do zapłaty.";
        if (invoice.SplitPayment == true)
            return $"Faktura z mechanizmem podzielonej płatności: zapłać przelewem MPP w banku. Kwota VAT: {Money(invoice.AmountVat)} PLN, NIP sprzedawcy: {invoice.VendorNip}, numer faktury: {data.Title}.";
        if (invoice.SellerPaidOn == DateOnly.MinValue)
            return "Sprzedawca oznaczył fakturę jako zapłaconą.";
        if (invoice.SellerPaidOn is { } paidOn)
            return $"Sprzedawca oznaczył fakturę jako zapłaconą ({paidOn.ToString("dd.MM.yyyy", PolishCulture)}).";
        if (invoice.PartialPayment == 2)
            return "Sprzedawca oznaczył fakturę jako zapłaconą w całości, w częściach.";
        if (string.IsNullOrWhiteSpace(invoice.VendorBankAccount) && invoice.XmlFetchedAt is null && invoice.DueDate is null)
            return "Numer rachunku pobieramy jeszcze z KSeF. Sprawdź ponownie za kilka minut.";
        if (QrCodeService.SanitizeAccount(data.RecipientAccount).Length == 0)
            return "Brak poprawnego numeru rachunku na fakturze.";
        if (invoice.FactorBankAccount is not null && string.IsNullOrWhiteSpace(QrCodeService.Sanitize(data.RecipientName, QrCodeService.MaxNameLength)))
            return "Płatność na rachunek faktora, ale faktura nie podaje nazwy faktora: nie można utworzyć kodu QR. Sprawdź odbiorcę przelewu na fakturze.";
        if (string.IsNullOrWhiteSpace(QrCodeService.Sanitize(data.RecipientName, QrCodeService.MaxNameLength))
            || string.IsNullOrWhiteSpace(QrCodeService.Sanitize(data.Title, QrCodeService.MaxTitleLength)))
            return "Nie można utworzyć kodu QR: brak czytelnej nazwy sprzedawcy lub numeru faktury.";
        return null;
    }

    private static string Money(decimal amount) => amount.ToString("N2", PolishCulture);

    private static bool IsType(InvoiceHeader invoice, string type) =>
        string.Equals(invoice.InvoiceType, type, StringComparison.OrdinalIgnoreCase);
}
