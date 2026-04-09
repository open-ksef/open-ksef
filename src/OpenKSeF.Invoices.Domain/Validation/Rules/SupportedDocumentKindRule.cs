using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class SupportedDocumentKindRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-001";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (Enum.IsDefined(target.Kind))
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Wybierz poprawny typ dokumentu.",
                "Unsupported or missing DocumentKind.",
                "Kind")
        ];
    }
}
