using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class SellerLegalNameRequiredRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-010";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (!string.IsNullOrWhiteSpace(target.Seller.Name?.Value))
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Uzupełnij nazwę sprzedawcy.",
                "SellerSnapshot.Name is missing.",
                "Seller.Name")
        ];
    }
}
