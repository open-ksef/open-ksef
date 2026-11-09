using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Domain.DTOs;

/// <summary>
/// Data read from a downloaded FA(2)/FA(3) invoice XML. Null <c>Lines</c> means the XML could not be read as an invoice.
/// </summary>
public sealed record InvoiceXmlDetails(
    string? BankAccount,
    IReadOnlyList<InvoiceLineDto>? Lines,
    DateOnly? DueDate,
    SettlementState Settlement = SettlementState.None,
    decimal? SettlementAmount = null,
    bool SplitPayment = false,
    DateOnly? SellerPaidOn = null,
    short? PartialPayment = null,
    string? FactorBankAccount = null,
    string? FactorNip = null,
    string? FactorName = null)
{
    public static readonly InvoiceXmlDetails Unreadable = new(null, null, null);
}
