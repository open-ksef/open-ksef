using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Policies;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class DocumentNumberRequiredOnApprovalRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-030";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage == ValidationStage.Approve;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        var numberingPolicy = context.Items.TryGetValue("NumberingPolicy", out var item)
            ? item as INumberingPolicy
            : null;

        if (numberingPolicy?.AssignOnApproval != true || target.DocumentNumber is not null)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Dokument nie ma nadanego numeru.",
                "DocumentNumber missing at approval.",
                "DocumentNumber")
        ];
    }
}
