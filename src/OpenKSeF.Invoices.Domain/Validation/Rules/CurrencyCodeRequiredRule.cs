using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class CurrencyCodeRequiredRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-040";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.Currency is not null)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Uzupełnij walutę dokumentu.",
                "CurrencyCode missing.",
                "Currency")
        ];
    }
}
