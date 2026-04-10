using OpenKSeF.Domain.DTOs;
using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Domain.Services;

/// <summary>
/// Default implementation of <see cref="ISyncedInvoiceMapper"/>.
/// Maps <see cref="SyncedInvoice"/> / <see cref="SyncedInvoiceLine"/> EF entities
/// to clean <see cref="InvoiceDto"/> / <see cref="InvoiceLineDto"/> contracts.
/// </summary>
public sealed class SyncedInvoiceMapper : ISyncedInvoiceMapper
{
    public InvoiceDto ToDto(SyncedInvoice invoice, bool includeLines = true) => new(
        Number: invoice.KSeFInvoiceNumber,
        ReferenceNumber: invoice.KSeFReferenceNumber,
        InvoiceNumber: invoice.InvoiceNumber,
        VendorName: invoice.VendorName,
        VendorNip: invoice.VendorNip,
        BuyerName: invoice.BuyerName,
        BuyerNip: invoice.BuyerNip,
        AmountNet: invoice.AmountNet,
        AmountVat: invoice.AmountVat,
        AmountGross: invoice.AmountGross,
        Currency: invoice.Currency,
        IssueDate: invoice.IssueDate,
        AcquisitionDate: invoice.AcquisitionDate,
        InvoiceType: invoice.InvoiceType,
        VendorBankAccount: invoice.VendorBankAccount,
        Lines: includeLines
            ? invoice.Lines.OrderBy(l => l.LineNumber).Select(ToLineDto).ToList()
            : null);

    public InvoiceLineDto ToLineDto(SyncedInvoiceLine line) => new(
        LineNumber: line.LineNumber,
        Name: line.Name,
        Unit: line.Unit,
        Quantity: line.Quantity,
        UnitPriceNet: line.UnitPriceNet,
        UnitPriceGross: line.UnitPriceGross,
        AmountNet: line.AmountNet,
        AmountGross: line.AmountGross,
        AmountVat: line.AmountVat,
        VatRate: line.VatRate);
}
