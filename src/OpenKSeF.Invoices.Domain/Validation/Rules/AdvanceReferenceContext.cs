namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed record AdvanceReferenceContext(
    string SellerNip,
    string? BuyerNip,
    string Currency);
