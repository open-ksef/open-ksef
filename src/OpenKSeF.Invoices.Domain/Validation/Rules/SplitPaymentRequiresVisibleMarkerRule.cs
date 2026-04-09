using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class SplitPaymentRequiresVisibleMarkerRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-064";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage == ValidationStage.Draft;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        var splitPaymentRequired = context.Items.TryGetValue("SplitPaymentRequired", out var item) &&
                                   item is true;
        var hasVisibleMarker = !string.IsNullOrWhiteSpace(target.PublicNotes) &&
                               target.PublicNotes.Contains("split payment", StringComparison.OrdinalIgnoreCase) ||
                               target.PublicNotes?.Contains("podzielonej płatności", StringComparison.OrdinalIgnoreCase) == true;

        if (!splitPaymentRequired || hasVisibleMarker)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Warning,
                context.Stage,
                "Dokument wskazuje split payment, ale brakuje oznaczenia do prezentacji.",
                "Split payment flag set without presentation marker.",
                "PublicNotes")
        ];
    }
}
