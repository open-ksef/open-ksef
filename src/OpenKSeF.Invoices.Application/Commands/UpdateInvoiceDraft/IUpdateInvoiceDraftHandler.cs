using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Application.Commands.UpdateInvoiceDraft;

public interface IUpdateInvoiceDraftHandler
{
    Invoice Handle(Invoice invoice, UpdateInvoiceDraftCommand command);
}
