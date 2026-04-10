using OpenKSeF.Invoices.Domain.Integration;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class KsefPayloadSchemaMustBeValidRule : IKsefTechnicalValidationRule<KsefInvoicePayload>
{
    public string Code => "INV-VAL-111";

    public bool AppliesTo(ValidationContext context, KsefInvoicePayload target) =>
        context.Stage == ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, KsefInvoicePayload target)
    {
        // When a real validator is provided in context, run it; otherwise fall back to the flag.
        if (context.Items.TryGetValue("KsefXmlSchemaValidator", out var validatorObj) &&
            validatorObj is IKsefXmlSchemaValidator validator)
        {
            if (!validator.IsValid(target.InvoiceXml, out var errors))
            {
                var detail = errors.Count > 0 ? $" ({errors[0]})" : string.Empty;
                return
                [
                    new ValidationMessage(
                        Code,
                        ValidationSeverity.Error,
                        context.Stage,
                        "Dokument nie przeszedł walidacji technicznej wymaganej przez KSeF.",
                        $"KSeF schema validation failed.{detail}",
                        "KsefPayload")
                ];
            }
            return [];
        }

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
