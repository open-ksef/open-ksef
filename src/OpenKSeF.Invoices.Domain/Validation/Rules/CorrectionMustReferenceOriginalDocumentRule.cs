using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class CorrectionMustReferenceOriginalDocumentRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-080";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.Kind != DocumentKind.CorrectionInvoice || target.CorrectionReference is not null)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Faktura korygująca musi wskazywać dokument korygowany.",
                "CorrectionReference missing.",
                "CorrectionReference")
        ];
    }
}
