namespace OpenKSeF.Domain.Models;

/// <summary>
/// Structured bank transfer data extracted from an invoice.
/// </summary>
public class TransferData
{
    public string RecipientName { get; set; } = string.Empty;
    public string? RecipientAccount { get; set; }
    public string RecipientNip { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";
    public string Title { get; set; } = string.Empty;
    public bool QrAvailable { get; set; }
    public bool ManualAmount { get; set; }
    public string? QrMessage { get; set; }
}
