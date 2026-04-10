using OpenKSeF.Invoices.Domain.Entities;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class LineDescriptionRequiredRule : IDomainValidationRule<InvoiceLine>
{
    public string Code => "INV-VAL-050";

    public bool AppliesTo(ValidationContext context, InvoiceLine target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, InvoiceLine target)
    {
        if (!string.IsNullOrWhiteSpace(target.Description))
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Każda pozycja musi mieć opis.",
                "Line description missing.",
                $"LineItems[{target.LineNumber}].Description")
        ];
    }
}
