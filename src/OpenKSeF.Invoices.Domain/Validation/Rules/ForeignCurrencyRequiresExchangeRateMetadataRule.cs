using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class ForeignCurrencyRequiresExchangeRateMetadataRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-042";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage == ValidationStage.Draft;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.Currency is null)
        {
            return [];
        }

        var isForeignCurrency = !string.Equals(
            target.Currency.Value,
            context.Policies.Currency.DefaultCurrency,
            StringComparison.OrdinalIgnoreCase);

        if (!isForeignCurrency || context.Items.ContainsKey("ExchangeRate"))
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Warning,
                context.Stage,
                "Dla waluty obcej może być wymagany kurs przeliczeniowy.",
                "Foreign currency document missing exchange rate metadata.",
                "Currency")
        ];
    }
}
