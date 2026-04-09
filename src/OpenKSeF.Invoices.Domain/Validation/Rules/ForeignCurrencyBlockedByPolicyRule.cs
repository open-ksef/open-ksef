using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class ForeignCurrencyBlockedByPolicyRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-041";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.Currency is null)
        {
            return [];
        }

        var allowedCurrency = context.Policies.Currency.DefaultCurrency;
        if (string.Equals(target.Currency.Value, allowedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Ta konfiguracja nie obsługuje jeszcze faktur w tej walucie.",
                "Currency blocked by CurrencyPolicy.",
                "Currency")
        ];
    }
}
