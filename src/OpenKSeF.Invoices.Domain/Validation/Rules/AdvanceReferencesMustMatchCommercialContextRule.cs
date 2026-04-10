using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class AdvanceReferencesMustMatchCommercialContextRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-073";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.Kind != DocumentKind.FinalInvoice)
        {
            return [];
        }

        var contexts = context.Items.TryGetValue("AdvanceReferenceContexts", out var item)
            ? item as IReadOnlyDictionary<InvoiceId, AdvanceReferenceContext>
            : null;

        if (contexts is null)
        {
            return [];
        }

        var sellerNip = target.Seller.Nip.Value;
        var buyerNip = target.Buyer.Nip?.Value;
        var currency = target.Currency.Value;

        var invalidReference = target.AdvanceDocumentIds.Any(id =>
            !contexts.TryGetValue(id, out var refContext) ||
            !string.Equals(refContext.SellerNip, sellerNip, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(refContext.BuyerNip, buyerNip, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(refContext.Currency, currency, StringComparison.OrdinalIgnoreCase));

        if (!invalidReference)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Rozliczane zaliczki muszą dotyczyć tego samego kontraktu sprzedażowego.",
                "Advance references inconsistent with final invoice.",
                "AdvanceDocumentIds")
        ];
    }
}
