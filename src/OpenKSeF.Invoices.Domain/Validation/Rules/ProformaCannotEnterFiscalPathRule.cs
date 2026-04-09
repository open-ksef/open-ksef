using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class ProformaCannotEnterFiscalPathRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-003";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.Kind != DocumentKind.Proforma)
        {
            return [];
        }

        var enteredFiscalPath =
            context.Stage == ValidationStage.SendToKsef ||
            context.IsKsefSubmissionRequested ||
            target.KsefSubmissionRequirement != KsefSubmissionRequirement.Forbidden;

        if (!enteredFiscalPath)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Proforma nie jest dokumentem fiskalnym i nie może zostać wysłana do KSeF.",
                "Proforma entered fiscal/KSeF path.",
                "KsefSubmissionRequirement")
        ];
    }
}
