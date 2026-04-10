using OpenKSeF.Invoices.Domain.Integration;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class KsefPayloadSchemaMustBeValidRule : IKsefTechnicalValidationRule<KsefInvoicePayload>
{
    public string Code => "INV-VAL-111";

    public bool AppliesTo(ValidationContext context, KsefInvoicePayload target) =>
        context.Stage == ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, KsefInvoicePayload target)
    {
        var schemaValid = context.Items.TryGetValue("KsefSchemaValid", out var item)
            ? item as bool? ?? true
            : true;

        if (schemaValid)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Dokument nie przeszedł walidacji technicznej wymaganej przez KSeF.",
                "KSeF schema validation failed.",
                "KsefPayload")
        ];
    }
}
