using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class IssueDateRequiredRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-020";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.IssueDate != default)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Uzupełnij datę wystawienia.",
                "IssueDate missing.",
                "IssueDate")
        ];
    }
}
