using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class AdvanceInvoiceAmountMustBePositiveRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-070";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.Kind != DocumentKind.AdvanceInvoice || target.Totals.GrossTotal.Amount > 0m)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Faktura zaliczkowa musi zawierać dodatnią kwotę zaliczki.",
                "Advance amount <= 0.",
                "Totals.GrossTotal")
        ];
    }
}
