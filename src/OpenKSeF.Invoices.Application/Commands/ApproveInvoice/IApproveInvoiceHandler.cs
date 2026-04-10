using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Validation;

namespace OpenKSeF.Invoices.Application.Commands.ApproveInvoice;

public interface IApproveInvoiceHandler
{
    ValidationResult Handle(Invoice invoice, ApproveInvoiceCommand command, ValidationContext context);
}
