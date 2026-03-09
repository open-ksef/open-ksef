namespace OpenKSeF.Api.Models;

public record TransferDetailsResponse(
    string RecipientName,
    string? RecipientAccount,
    string RecipientNip,
    decimal Amount,
    string Currency,
    string Title,
    string TransferText,
    string QrCodeBase64);
