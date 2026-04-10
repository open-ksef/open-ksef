using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class AcceptedKsefDocumentCannotBeResentRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-093";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage == ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.Status != DocumentStatus.AcceptedByKsef &&
            target.KsefSubmissionState != KsefSubmissionState.Accepted)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Dokument wysłany i przyjęty przez KSeF jest niezmienny.",
                "Attempted send/edit on immutable document.",
                "Status")
        ];
    }
}
