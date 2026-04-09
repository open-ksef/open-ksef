using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class VatSummaryMustMatchLinesRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-063";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.LineItems.Any(line => line.VatRate is null))
        {
            return [];
        }

        var expected = target.LineItems
            .GroupBy(line => line.VatRate, line => line)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Net = Math.Round(group.Sum(x => x.NetAmount.Amount), 2, MidpointRounding.AwayFromZero),
                    Vat = Math.Round(group.Sum(x => x.VatAmount.Amount), 2, MidpointRounding.AwayFromZero),
                    Gross = Math.Round(group.Sum(x => x.GrossAmount.Amount), 2, MidpointRounding.AwayFromZero)
                });

        var actual = target.VatBreakdown.ToDictionary(
            x => x.Rate,
            x => new
            {
                Net = x.TaxableBase.Amount,
                Vat = x.VatAmount.Amount,
                Gross = x.GrossSubtotal.Amount
            });

        var matches = expected.Count == actual.Count &&
                      expected.All(kvp =>
                          actual.TryGetValue(kvp.Key, out var current) &&
                          current.Net == kvp.Value.Net &&
                          current.Vat == kvp.Value.Vat &&
                          current.Gross == kvp.Value.Gross);

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
                "Podsumowanie VAT jest niespójne z pozycjami.",
                "Vat breakdown mismatch.",
                "VatBreakdown")
        ];
    }
}
