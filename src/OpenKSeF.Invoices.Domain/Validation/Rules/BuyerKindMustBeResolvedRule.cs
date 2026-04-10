using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class BuyerKindMustBeResolvedRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-012";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage == ValidationStage.Draft;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.BuyerKind != BuyerKind.Unknown)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Warning,
                context.Stage,
                "Nie określono typu nabywcy (B2B/B2C). Może to wpływać na obowiązek wysyłki do KSeF.",
                "Buyer classification unresolved.",
                "Buyer.Kind")
        ];
    }
}
