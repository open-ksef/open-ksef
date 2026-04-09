using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class FiscalDocumentRequiresLineItemsRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-002";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.Kind == DocumentKind.Proforma || target.LineItems.Count > 0)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Faktura musi zawierać co najmniej jedną pozycję.",
                "No line items found for fiscal document.",
                "LineItems")
        ];
    }
}
