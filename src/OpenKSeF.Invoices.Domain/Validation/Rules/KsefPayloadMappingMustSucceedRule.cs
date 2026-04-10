using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class KsefPayloadMappingMustSucceedRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-110";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage == ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        var mappingFailed = context.Items.TryGetValue("KsefPayloadMappingFailed", out var item) &&
                            item is true;

        if (!mappingFailed)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Nie udało się przygotować danych dokumentu do wysyłki do KSeF.",
                "Domain-to-KSeF payload mapping failed.",
                "KsefPayload")
        ];
    }
}
