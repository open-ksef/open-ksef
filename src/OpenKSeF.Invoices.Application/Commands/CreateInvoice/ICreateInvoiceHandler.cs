using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Application.Commands.CreateInvoice;

public interface ICreateInvoiceHandler
{
    Invoice Handle(CreateInvoiceCommand command);
}
