using OpenKSeF.Domain.DTOs;
using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Domain.Services;

/// <summary>
/// Default implementation of <see cref="ISyncedInvoiceMapper"/>.
/// Maps legacy <see cref="InvoiceHeader"/> / <see cref="InvoiceLine"/> EF entities
/// to clean <see cref="InvoiceDto"/> / <see cref="InvoiceLineDto"/> contracts.
/// </summary>
#pragma warning disable CS0618 // referenced legacy types intentionally
public sealed class SyncedInvoiceMapper : ISyncedInvoiceMapper
{
    public InvoiceDto ToDto(InvoiceHeader header, bool includeLines = true) => new(
        Number: header.KSeFInvoiceNumber,
        ReferenceNumber: header.KSeFReferenceNumber,
        InvoiceNumber: header.InvoiceNumber,
        VendorName: header.VendorName,
        VendorNip: header.VendorNip,
        BuyerName: header.BuyerName,
        BuyerNip: header.BuyerNip,
        AmountNet: header.AmountNet,
        AmountVat: header.AmountVat,
        AmountGross: header.AmountGross,
        Currency: header.Currency,
        IssueDate: header.IssueDate,
        AcquisitionDate: header.AcquisitionDate,
        InvoiceType: header.InvoiceType,
        VendorBankAccount: header.VendorBankAccount,
        Lines: includeLines
            ? header.Lines.OrderBy(l => l.LineNumber).Select(ToLineDto).ToList()
            : null);

    public InvoiceLineDto ToLineDto(InvoiceLine line) => new(
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
#pragma warning restore CS0618
