using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class DocumentTotalsMustMatchLineSumRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-053";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        var net = target.LineItems.Sum(x => x.NetAmount.Amount);
        var vat = target.LineItems.Sum(x => x.VatAmount.Amount);
        var gross = target.LineItems.Sum(x => x.GrossAmount.Amount);

        var matches =
            Math.Abs(target.Totals.NetTotal.Amount - net) <= 0.01m &&
            Math.Abs(target.Totals.VatTotal.Amount - vat) <= 0.01m &&
            Math.Abs(target.Totals.GrossTotal.Amount - gross) <= 0.01m;

        if (matches)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Podsumowanie dokumentu nie zgadza się z sumą pozycji.",
                "Document totals mismatch.",
                "Totals")
        ];
    }
}
