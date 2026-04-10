using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class AcceptedKsefDocumentCannotBeEditedRule : IDomainValidationRule<Invoice>, IStateTransitionRule<Invoice>
{
    public string Code => "INV-VAL-101";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        var transition = context.Items.TryGetValue("RequestedTransition", out var item)
            ? item as string
            : null;

        var editAttempt = transition is "ApprovedToDraft" or "DraftToApproved";
        var immutable = target.Status == DocumentStatus.AcceptedByKsef ||
                        target.KsefSubmissionState == KsefSubmissionState.Accepted;

        if (!editAttempt || !immutable)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Dokument po skutecznej wysyłce do KSeF nie może być edytowany.",
                "Immutable aggregate mutation attempted.",
                "Status")
        ];
    }
}
