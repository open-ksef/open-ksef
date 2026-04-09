using OpenKSeF.Invoices.Domain.Entities;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class LineAmountsMustBeInternallyConsistentRule : IDomainValidationRule<InvoiceLine>
{
    public string Code => "INV-VAL-052";

    public bool AppliesTo(ValidationContext context, InvoiceLine target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, InvoiceLine target)
    {
        var isConsistent = Math.Abs(target.GrossAmount.Amount - (target.NetAmount.Amount + target.VatAmount.Amount)) <= 0.01m;
        if (isConsistent)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Kwoty na pozycji są niespójne.",
                "Line net/vat/gross mismatch.",
                $"LineItems[{target.LineNumber}]")
        ];
    }
}
