using OpenKSeF.Invoices.Contracts.Dtos;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Projection;

namespace OpenKSeF.Invoices.Application.Projection;

/// <summary>
/// Projects an <see cref="Invoice"/> aggregate to an API-facing <see cref="InvoiceReadDto"/>.
/// Controllers and query handlers depend on this projector — never on aggregate internals.
/// </summary>
public sealed class InvoiceReadDtoProjector : IInvoiceReadModelProjector<InvoiceReadDto>
{
    public InvoiceReadDto Project(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        return new InvoiceReadDto(
            Id: invoice.Id.Value,
            TenantId: invoice.TenantId.Value,
            Kind: invoice.Kind.ToString(),
            Status: invoice.Status.ToString(),
            BuyerKind: invoice.BuyerKind.ToString(),
            KsefSubmissionRequirement: invoice.KsefSubmissionRequirement.ToString(),
            KsefSubmissionState: invoice.KsefSubmissionState.ToString(),
            Seller: new PartyReadDto(invoice.Seller.Name.Value, invoice.Seller.Nip?.Value),
            Buyer: new PartyReadDto(invoice.Buyer.Name.Value, invoice.Buyer.Nip?.Value),
            IssueDate: invoice.IssueDate,
            SaleDate: invoice.SaleDate,
            DueDate: invoice.DueDate,
            ApprovedAt: invoice.ApprovedAt,
            SubmittedToKsefAt: invoice.SubmittedToKsefAt,
            AcceptedByKsefAt: invoice.AcceptedByKsefAt,
            Currency: invoice.Currency.Value,
            TotalNet: new MoneyReadDto(invoice.Totals.NetTotal.Amount, invoice.Currency.Value),
            TotalVat: new MoneyReadDto(invoice.Totals.VatTotal.Amount, invoice.Currency.Value),
            TotalGross: new MoneyReadDto(invoice.Totals.GrossTotal.Amount, invoice.Currency.Value),
            DocumentNumber: invoice.DocumentNumber?.Value,
            ExternalReference: invoice.ExternalReference,
            PaymentMethod: invoice.PaymentMethod,
            PublicNotes: invoice.PublicNotes,
            InternalNotes: invoice.InternalNotes,
            KsefDocumentNumber: invoice.KsefIdentifiers?.KsefDocumentNumber,
            KsefReferenceNumber: invoice.KsefIdentifiers?.KsefReferenceNumber,
            KsefRejectionReason: invoice.KsefRejectionReason,
            CorrectionReference: invoice.CorrectionReference is null
                ? null
                : new CorrectionReferenceReadDto(
                    invoice.CorrectionReference.OriginalInvoiceId.Value,
                    invoice.CorrectionReference.OriginalDocumentNumber.Value,
                    invoice.CorrectionReference.ReasonKind.ToString(),
                    invoice.CorrectionReference.ReasonDescription),
            Lines: invoice.LineItems
                .Select(l => new InvoiceLineReadDto(
                    l.LineNumber,
                    l.Description,
                    l.Quantity,
                    l.UnitOfMeasure,
                    l.PricingMode.ToString(),
                    new MoneyReadDto(l.UnitPrice.Amount, invoice.Currency.Value),
                    l.Discount?.Value,
                    l.VatRate.ToString(),
                    new MoneyReadDto(l.NetAmount.Amount, invoice.Currency.Value),
                    new MoneyReadDto(l.VatAmount.Amount, invoice.Currency.Value),
                    new MoneyReadDto(l.GrossAmount.Amount, invoice.Currency.Value),
                    l.CorrectionRole.ToString()))
                .ToList(),
            AdvanceDocumentIds: invoice.AdvanceDocumentIds
                .Select(id => id.Value.ToString())
                .ToList(),
            SettledAdvanceAllocations: invoice.SettledAdvanceAllocations
                .Select(a => new AdvanceAllocationReadDto(
                    a.AdvanceInvoiceId.Value,
                    a.AdvanceDocumentNumber.Value,
                    new MoneyReadDto(a.SettledAmount.Amount, invoice.Currency.Value)))
                .ToList(),
            DuplicateIssuances: invoice.DuplicateIssuances
                .Select(d => new DuplicateIssuanceReadDto(d.IssuedAt, d.IssuedBy))
                .ToList());
    }
}
