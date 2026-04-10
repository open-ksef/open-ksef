using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class SellerNipRequiredForPolishFiscalDocumentRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-011";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        var isPolishDefaultDocument = string.Equals(
            context.Policies.Currency.DefaultCurrency,
            CurrencyCode.Pln.Value,
            StringComparison.OrdinalIgnoreCase);

        var isFiscalDocument = target.Kind != DocumentKind.Proforma;

        if (!isPolishDefaultDocument || !isFiscalDocument || target.Seller.Nip is not null)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Uzupełnij NIP sprzedawcy.",
                "SellerSnapshot.Nip is missing.",
                "Seller.Nip")
        ];
    }
}
