using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class BuyerClassificationMustResolveForKsefRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-090";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        var dependsOnBuyerClassification =
            context.Items.TryGetValue("KsefObligationDependsOnBuyerClassification", out var item) &&
            item is true;

        if (!dependsOnBuyerClassification || target.BuyerKind != BuyerKind.Unknown)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Nie można ustalić, czy dokument wymaga wysyłki do KSeF.",
                "KSeF obligation unresolved due to buyer classification ambiguity.",
                "Buyer.Kind")
        ];
    }
}
