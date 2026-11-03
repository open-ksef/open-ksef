using System.Globalization;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Domain.Services;

public class TransferDetailsService : ITransferDetailsService
{
    private const decimal MppNoticeThreshold = 15000m;

    public string BuildTransferText(InvoiceHeader invoice)
    {
        var lines = new List<string>
        {
            $"Odbiorca: {invoice.VendorName}"
        };

        if (!string.IsNullOrWhiteSpace(invoice.VendorNip))
            lines.Add($"NIP: {invoice.VendorNip}");

        if (!string.IsNullOrWhiteSpace(invoice.VendorBankAccount))
            lines.Add($"Nr rachunku: {invoice.VendorBankAccount}");

        lines.Add($"Kwota: {invoice.AmountGross.ToString("N2", CultureInfo.InvariantCulture)} {invoice.Currency}");
        lines.Add($"Tytul: {invoice.InvoiceNumber ?? invoice.KSeFInvoiceNumber}");

        return string.Join("\n", lines);
    }

    public TransferData BuildTransferData(InvoiceHeader invoice)
    {
        var title = invoice.InvoiceNumber ?? invoice.KSeFInvoiceNumber;
        var blocked = GetQrBlockMessage(invoice, title);
        var manualAmount = IsType(invoice, "Zal") || IsType(invoice, "Roz");

        var notices = new List<string>();
        if (manualAmount)
            notices.Add("Zaliczka/rozliczenie: wpisz kwotę ręcznie.");
        if (invoice.AmountGross > MppNoticeThreshold)
            notices.Add("Sprawdź, czy faktura nie wymaga podzielonej płatności (MPP).");

        return new TransferData
        {
            RecipientName = invoice.VendorName,
            RecipientNip = invoice.VendorNip,
            RecipientAccount = invoice.VendorBankAccount,
            Amount = invoice.AmountGross,
            Currency = invoice.Currency,
            Title = title,
            QrAvailable = blocked is null,
            ManualAmount = manualAmount,
            QrMessage = blocked ?? (notices.Count > 0 ? string.Join(" ", notices) : null)
        };
    }

    private static string? GetQrBlockMessage(InvoiceHeader invoice, string title)
    {
        if (!string.Equals(invoice.Currency, "PLN", StringComparison.OrdinalIgnoreCase))
            return $"Faktura w walucie {invoice.Currency}: kod QR obsługuje tylko przelew w PLN.";
        if (invoice.InvoiceType?.StartsWith("Kor", StringComparison.OrdinalIgnoreCase) == true)
            return "Faktura korygująca: kwota to różnica. Sprawdź, czy jest coś do zapłaty.";
        if (string.IsNullOrWhiteSpace(invoice.InvoiceType))
            return "Nie można ustalić rodzaju faktury. Sprawdź kwotę na fakturze. Aby uzyskać kod QR, uruchom pełną resynchronizację (portal: Dane logowania).";
        if (invoice.AmountGross <= 0m)
            return "Brak kwoty do zapłaty.";
        if (QrCodeService.SanitizeAccount(invoice.VendorBankAccount).Length == 0)
            return "Brak poprawnego numeru rachunku na fakturze.";
        if (string.IsNullOrWhiteSpace(QrCodeService.Sanitize(invoice.VendorName, QrCodeService.MaxNameLength))
            || string.IsNullOrWhiteSpace(QrCodeService.Sanitize(title, QrCodeService.MaxTitleLength)))
            return "Nie można utworzyć kodu QR: brak czytelnej nazwy sprzedawcy lub numeru faktury.";
        return null;
    }

    private static bool IsType(InvoiceHeader invoice, string type) =>
        string.Equals(invoice.InvoiceType, type, StringComparison.OrdinalIgnoreCase);
}
