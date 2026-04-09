using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class DueDateCannotBeEarlierThanIssueDateRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-021";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage == ValidationStage.Draft;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.DueDate is null || target.IssueDate == default || target.DueDate.Value.Date >= target.IssueDate.Date)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Warning,
                context.Stage,
                "Termin płatności jest wcześniejszy niż data wystawienia.",
                "DueDate < IssueDate.",
                "DueDate")
        ];
    }
}
