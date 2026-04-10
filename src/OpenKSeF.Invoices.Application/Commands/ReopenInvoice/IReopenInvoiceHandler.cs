using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Application.Commands.ReopenInvoice;

public interface IReopenInvoiceHandler
{
    Invoice Handle(Invoice invoice, ReopenInvoiceCommand command);
}
