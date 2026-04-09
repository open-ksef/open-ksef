#pragma warning disable CS0618 // TransferDetailsService is a legacy sync-side service; intentional InvoiceHeader reference
using System.Globalization;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Domain.Services;

public class TransferDetailsService : ITransferDetailsService
{
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
        return new TransferData
        {
            RecipientName = invoice.VendorName,
            RecipientNip = invoice.VendorNip,
            RecipientAccount = invoice.VendorBankAccount,
            Amount = invoice.AmountGross,
            Currency = invoice.Currency,
            Title = invoice.InvoiceNumber ?? invoice.KSeFInvoiceNumber
        };
    }
}
