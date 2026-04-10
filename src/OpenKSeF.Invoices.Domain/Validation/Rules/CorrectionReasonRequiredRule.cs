using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class CorrectionReasonRequiredRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-081";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.Kind != DocumentKind.CorrectionInvoice || target.CorrectionReference is null)
        {
            return [];
        }

        var reasonProvided = context.Items.TryGetValue("CorrectionReasonProvided", out var item)
            ? item as bool? ?? true
            : !string.IsNullOrWhiteSpace(target.CorrectionReference.ReasonDescription);

        if (reasonProvided)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Podaj przyczynę korekty.",
                "Correction reason missing.",
                "CorrectionReference")
        ];
    }
}
