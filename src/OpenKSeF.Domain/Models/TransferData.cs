namespace OpenKSeF.Domain.Models;

/// <summary>
/// Structured bank transfer data extracted from an invoice.
/// </summary>
public class TransferData
{
    public string RecipientName { get; set; } = string.Empty;

    /// <summary>
    /// Recipient bank account number. Null in MVP — vendor bank account
    /// retrieval may require additional KSeF API calls or manual entry.
    /// </summary>
    public string? RecipientAccount { get; set; }

    public string RecipientNip { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";
    public string Title { get; set; } = string.Empty;
}
