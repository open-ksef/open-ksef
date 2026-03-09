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

        lines.Add($"Kwota: {invoice.AmountGross.ToString("N2", CultureInfo.InvariantCulture)} {invoice.Currency}");
        lines.Add($"Tytul: Faktura {invoice.KSeFInvoiceNumber}");

        return string.Join("\n", lines);
    }

    public TransferData BuildTransferData(InvoiceHeader invoice)
    {
        return new TransferData
        {
            RecipientName = invoice.VendorName,
            RecipientNip = invoice.VendorNip,
            // RecipientAccount is null for MVP — vendor bank account lookup deferred.
            // TODO: Integrate with KSeF API or manual entry for bank account retrieval.
            RecipientAccount = null,
            Amount = invoice.AmountGross,
            Currency = invoice.Currency,
            Title = $"Faktura {invoice.KSeFInvoiceNumber}"
        };
    }
}
