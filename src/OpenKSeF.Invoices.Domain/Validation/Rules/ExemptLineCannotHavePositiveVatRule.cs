using OpenKSeF.Invoices.Domain.Entities;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class ExemptLineCannotHavePositiveVatRule : IDomainValidationRule<InvoiceLine>
{
    public string Code => "INV-VAL-062";

    public bool AppliesTo(ValidationContext context, InvoiceLine target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, InvoiceLine target)
    {
        if (target.VatRate?.IsExempt != true || target.VatAmount.Amount <= 0m)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Pozycja zwolniona z VAT nie może zawierać kwoty VAT.",
                "Exempt line has non-zero VAT amount.",
                $"LineItems[{target.LineNumber}].VatAmount")
        ];
    }
}
