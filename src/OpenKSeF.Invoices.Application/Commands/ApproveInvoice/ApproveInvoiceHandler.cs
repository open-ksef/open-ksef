using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;

namespace OpenKSeF.Invoices.Application.Commands.ApproveInvoice;

public sealed class ApproveInvoiceHandler(ApprovalValidationService validationService) : IApproveInvoiceHandler
{
    public ValidationResult Handle(Invoice invoice, ApproveInvoiceCommand command, ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var result = validationService.Validate(invoice, context);
        if (result.HasErrors)
        {
            var codes = string.Join(", ", result.Messages.Select(m => m.Code));
            throw new InvoiceDomainException($"Invoice approval blocked by validation: {codes}");
        }

        invoice.Approve(command.ApprovedAt);
        return result;
    }
}
