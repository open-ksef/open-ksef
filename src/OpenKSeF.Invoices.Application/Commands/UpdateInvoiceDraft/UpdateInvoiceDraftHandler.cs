using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Application.Commands.UpdateInvoiceDraft;

public sealed class UpdateInvoiceDraftHandler : IUpdateInvoiceDraftHandler
{
    public Invoice Handle(Invoice invoice, UpdateInvoiceDraftCommand command)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(command);

        if (invoice.Status != DocumentStatus.Draft)
        {
            throw new InvoiceDomainException(
                $"Cannot update invoice draft in state {invoice.Status}. Expected Draft.");
        }

        invoice.SetIssueDates(command.IssueDate, command.SaleDate, command.DueDate);
        invoice.SetCommercialData(command.PaymentMethod, command.PublicNotes, command.InternalNotes);

        if (!string.IsNullOrWhiteSpace(command.DocumentNumber))
        {
            invoice.SetDocumentNumber(new DocumentNumber(command.DocumentNumber));
        }

        return invoice;
    }
}
