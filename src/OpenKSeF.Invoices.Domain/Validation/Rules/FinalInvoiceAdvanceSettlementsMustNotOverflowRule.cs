using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Policies;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class FinalInvoiceAdvanceSettlementsMustNotOverflowRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-072";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.Kind != DocumentKind.FinalInvoice)
        {
            return [];
        }

        var policy = context.Items.TryGetValue("AdvanceSettlementPolicy", out var item)
            ? item as IAdvanceSettlementPolicy
            : null;

        if (policy is null || policy.AreAllocationsValid(target, target.SettledAdvanceAllocations))
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Suma rozliczanych zaliczek przekracza wartość faktury końcowej.",
                "Advance settlement overflow.",
                "SettledAdvanceAllocations")
        ];
    }
}
