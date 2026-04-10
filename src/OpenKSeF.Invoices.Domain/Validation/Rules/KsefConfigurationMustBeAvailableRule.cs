using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class KsefConfigurationMustBeAvailableRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-092";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage == ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        var configAvailable = context.Items.TryGetValue("KsefConfigAvailable", out var item)
            ? item as bool? ?? false
            : false;

        if (configAvailable)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Brakuje konfiguracji wymaganej do wysyłki do KSeF.",
                "KSeF credentials/config not available.",
                "Ksef")
        ];
    }
}
