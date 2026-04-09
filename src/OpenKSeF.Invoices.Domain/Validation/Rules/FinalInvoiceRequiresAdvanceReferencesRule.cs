using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class FinalInvoiceRequiresAdvanceReferencesRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-071";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.Kind != DocumentKind.FinalInvoice || target.AdvanceDocumentIds.Count > 0)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Faktura końcowa musi wskazywać co najmniej jedną fakturę zaliczkową.",
                "Final invoice missing advance references.",
                "AdvanceDocumentIds")
        ];
    }
}
