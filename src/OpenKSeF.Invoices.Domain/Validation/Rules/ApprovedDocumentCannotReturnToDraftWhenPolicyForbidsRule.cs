using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class ApprovedDocumentCannotReturnToDraftWhenPolicyForbidsRule : IDomainValidationRule<Invoice>, IStateTransitionRule<Invoice>
{
    public string Code => "INV-VAL-102";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage == ValidationStage.Approve;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        var transition = context.Items.TryGetValue("RequestedTransition", out var item)
            ? item as string
            : null;

        var allowReopen = context.Items.TryGetValue("AllowReopenApproved", out var allowItem)
            ? allowItem as bool?
            : null;

        if (transition != "ApprovedToDraft" || allowReopen != false)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Ta konfiguracja nie pozwala edytować zatwierdzonego dokumentu.",
                "Approved->Draft forbidden by policy.",
                "Status")
        ];
    }
}
