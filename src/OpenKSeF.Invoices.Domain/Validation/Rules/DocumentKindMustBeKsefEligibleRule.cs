using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class DocumentKindMustBeKsefEligibleRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-091";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage == ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        var eligible = target.Kind is not DocumentKind.Proforma &&
                       target.KsefSubmissionRequirement != KsefSubmissionRequirement.Forbidden;

        if (eligible)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Ten typ dokumentu nie może zostać wysłany do KSeF.",
                "Document kind blocked for KSeF submission.",
                "Kind")
        ];
    }
}
