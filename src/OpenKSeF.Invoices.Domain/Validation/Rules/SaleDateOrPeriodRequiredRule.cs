using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class SaleDateOrPeriodRequiredRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-022";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (!context.Policies.Validation.SaleDateRequired)
        {
            return [];
        }

        var hasSaleDate = target.SaleDate is not null;
        var hasSalePeriod =
            context.Items.ContainsKey("SalePeriodStart") ||
            context.Items.ContainsKey("SalePeriodEnd");

        if (hasSaleDate || hasSalePeriod)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Uzupełnij datę lub okres sprzedaży.",
                "Sale date/period missing.",
                "SaleDate")
        ];
    }
}
