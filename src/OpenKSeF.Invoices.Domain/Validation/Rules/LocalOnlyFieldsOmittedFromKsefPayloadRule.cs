using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class LocalOnlyFieldsOmittedFromKsefPayloadRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-112";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage == ValidationStage.Draft;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        var hasLocalOnlyFields = context.Items.TryGetValue("HasLocalOnlyFields", out var item)
            ? item as bool? ?? false
            : !string.IsNullOrWhiteSpace(target.InternalNotes);

        if (!hasLocalOnlyFields)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Warning,
                context.Stage,
                "Część danych ma charakter lokalny i nie zostanie wysłana do KSeF.",
                "Local-only fields omitted from KSeF payload.",
                "InternalNotes")
        ];
    }
}
