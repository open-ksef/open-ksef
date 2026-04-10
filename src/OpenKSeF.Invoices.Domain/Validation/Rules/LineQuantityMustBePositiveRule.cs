using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class LineQuantityMustBePositiveRule : IDomainValidationRule<InvoiceLine>
{
    public string Code => "INV-VAL-051";

    public bool AppliesTo(ValidationContext context, InvoiceLine target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, InvoiceLine target)
    {
        if (target.CorrectionRole != CorrectionRole.Normal || target.Quantity > 0m)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Ilość na pozycji musi być większa od zera.",
                "Line quantity invalid for non-correction line.",
                $"LineItems[{target.LineNumber}].Quantity")
        ];
    }
}
