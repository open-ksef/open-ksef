using OpenKSeF.Invoices.Contracts.Dtos;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Projection;

namespace OpenKSeF.Invoices.Application.Projection;

/// <summary>
/// Projects an <see cref="Invoice"/> aggregate to an <see cref="InvoicePrintModel"/>.
/// Supports standard (Polish), duplicate, and English-label variants.
/// </summary>
public sealed class InvoicePrintModelProjector : IInvoiceReadModelProjector<InvoicePrintModel>
{
    private readonly InvoiceReadDtoProjector _dtoProjector;
    private readonly PrintVariant _variant;

    public InvoicePrintModelProjector(PrintVariant variant)
    {
        _variant = variant;
        _dtoProjector = new InvoiceReadDtoProjector();
    }

    public InvoicePrintModel Project(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var dto = _dtoProjector.Project(invoice);
        var labels = _variant == PrintVariant.English ? PrintLabels.English : PrintLabels.Polish;

        DuplicatePrintInfo? duplicateInfo = null;
        if (_variant == PrintVariant.Duplicate)
        {
            var latest = invoice.DuplicateIssuances.Count > 0
                ? invoice.DuplicateIssuances[^1]
                : null;

            duplicateInfo = new DuplicatePrintInfo(
                IssuedAt: latest?.IssuedAt ?? DateTime.UtcNow,
                IssuedBy: latest?.IssuedBy,
                OriginalInvoiceId: invoice.Id.Value,
                OriginalDocumentNumber: invoice.DocumentNumber?.Value ?? string.Empty);
        }

        return new InvoicePrintModel(dto, _variant, labels, duplicateInfo);
    }
}
