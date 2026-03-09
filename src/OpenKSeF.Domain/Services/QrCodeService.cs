using System.Text;
using System.Text.RegularExpressions;
using OpenKSeF.Domain.Models;
using QRCoder;

namespace OpenKSeF.Domain.Services;

/// <summary>
/// QR code generation using the Polish ZBP 2D standard.
/// Payload: |PL|ACCOUNT|AMOUNT|NAME|TITLE|||
/// </summary>
public partial class QrCodeService : IQrCodeService
{
    private const int MaxNameLength = 20;
    private const int MaxTitleLength = 32;
    private const int NrbLength = 26;

    private static readonly Dictionary<char, char> PolishDiacritics = new()
    {
        ['ą'] = 'a', ['ć'] = 'c', ['ę'] = 'e', ['ł'] = 'l',
        ['ń'] = 'n', ['ó'] = 'o', ['ś'] = 's', ['ź'] = 'z', ['ż'] = 'z',
        ['Ą'] = 'A', ['Ć'] = 'C', ['Ę'] = 'E', ['Ł'] = 'L',
        ['Ń'] = 'N', ['Ó'] = 'O', ['Ś'] = 'S', ['Ź'] = 'Z', ['Ż'] = 'Z',
    };

    public byte[] GenerateTransferQr(TransferData data)
    {
        var payload = BuildZbpPayload(data);

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(20);
    }

    public string BuildZbpPayload(TransferData data)
    {
        var account = SanitizeAccount(data.RecipientAccount);
        var amount = AmountToGrosze(data.Amount);
        var name = Sanitize(data.RecipientName, MaxNameLength);
        var title = Sanitize(data.Title, MaxTitleLength);

        return $"|PL|{account}|{amount}|{name}|{title}|||";
    }

    internal static string AmountToGrosze(decimal amount)
    {
        if (amount <= 0m)
            return "0";
        var grosze = Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        return ((long)grosze).ToString();
    }

    internal static string Sanitize(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (PolishDiacritics.TryGetValue(ch, out var replacement))
                sb.Append(replacement);
            else
                sb.Append(ch);
        }

        var transliterated = sb.ToString();
        var cleaned = AllowedCharsRegex().Replace(transliterated, "");
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    internal static string SanitizeAccount(string? account)
    {
        if (string.IsNullOrWhiteSpace(account))
            return string.Empty;

        var digitsOnly = DigitsOnlyRegex().Replace(account, "");
        return digitsOnly.Length == NrbLength ? digitsOnly : string.Empty;
    }

    [GeneratedRegex("[^a-zA-Z0-9 .\\-]")]
    private static partial Regex AllowedCharsRegex();

    [GeneratedRegex("[^0-9]")]
    private static partial Regex DigitsOnlyRegex();
}
