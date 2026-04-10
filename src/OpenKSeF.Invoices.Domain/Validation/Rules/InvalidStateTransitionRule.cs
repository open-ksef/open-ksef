using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class InvalidStateTransitionRule : IDomainValidationRule<Invoice>, IStateTransitionRule<Invoice>
{
    public string Code => "INV-VAL-100";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        var transition = context.Items.TryGetValue("RequestedTransition", out var item)
            ? item as string
            : null;

        var immutable = target.Status == DocumentStatus.AcceptedByKsef ||
                        target.KsefSubmissionState == KsefSubmissionState.Accepted;

        if (transition == "ApprovedToDraft" && immutable)
        {
            return [];
        }

        var isValid = transition switch
        {
            "DraftToApproved" => target.Status is DocumentStatus.Draft or DocumentStatus.RejectedByKsef,
            "ApprovedToDraft" => target.Status == DocumentStatus.Approved,
            "DraftToSubmitted" => false,
            "ApprovedToSubmitted" => target.Status == DocumentStatus.Approved,
            _ => true
        };

        if (isValid)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Nie można wykonać tej operacji w aktualnym stanie dokumentu.",
                "Invalid state transition.",
                "Status")
        ];
    }
}
