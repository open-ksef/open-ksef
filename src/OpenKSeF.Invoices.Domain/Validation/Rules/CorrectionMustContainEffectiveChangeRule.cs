using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class CorrectionMustContainEffectiveChangeRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-082";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.Kind != DocumentKind.CorrectionInvoice || target.CorrectionReference is null)
        {
            return [];
        }

        var hasChanges = context.Items.TryGetValue("CorrectionHasChanges", out var item)
            ? item as bool? ?? false
            : target.LineItems.Any(line => line.CorrectionRole != CorrectionRole.Normal);

        if (hasChanges)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Korekta nie zawiera żadnej zmiany względem dokumentu pierwotnego.",
                "No detectable difference from original document.",
                "LineItems")
        ];
    }
}
