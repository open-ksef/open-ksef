using OpenKSeF.Domain.Models;

namespace OpenKSeF.Domain.Services;

/// <summary>
/// Generates QR codes for bank transfers using the Polish ZBP 2D standard.
/// Format: |PL|ACCOUNT|AMOUNT|NAME|TITLE|||
/// </summary>
public interface IQrCodeService
{
    /// <summary>
    /// Generates a PNG-encoded QR code image from transfer data.
    /// </summary>
    byte[] GenerateTransferQr(TransferData data);

    /// <summary>
    /// Builds the ZBP 2D payload string without generating the image.
    /// Useful for testing and debugging.
    /// </summary>
    string BuildZbpPayload(TransferData data);
}
