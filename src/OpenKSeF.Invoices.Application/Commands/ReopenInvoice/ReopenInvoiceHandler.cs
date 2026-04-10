using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Policies;

namespace OpenKSeF.Invoices.Application.Commands.ReopenInvoice;

public sealed class ReopenInvoiceHandler(IApprovedEditPolicy approvedEditPolicy) : IReopenInvoiceHandler
{
    public Invoice Handle(Invoice invoice, ReopenInvoiceCommand command)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(command);

        invoice.Reopen(command.ReopenedAt, approvedEditPolicy.CanReopen(invoice));
        return invoice;
    }
}
