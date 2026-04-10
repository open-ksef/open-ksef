using OpenKSeF.Invoices.Domain.Entities;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class ExemptLineRequiresReasonRule : IDomainValidationRule<InvoiceLine>
{
    public string Code => "INV-VAL-061";

    public bool AppliesTo(ValidationContext context, InvoiceLine target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, InvoiceLine target)
    {
        var classificationSuggestsExemption = string.Equals(target.VatClassification?.Code, "EXEMPT", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(target.VatClassification?.Code, "NP", StringComparison.OrdinalIgnoreCase);
        var isExempt = target.VatRate?.IsExempt == true || classificationSuggestsExemption;

        if (!isExempt || target.VatRate?.ExemptionReason is not null)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Dla pozycji zwolnionej z VAT podaj podstawę zwolnienia.",
                "Exempt line missing TaxExemptionReason.",
                $"LineItems[{target.LineNumber}].VatRate")
        ];
    }
}
