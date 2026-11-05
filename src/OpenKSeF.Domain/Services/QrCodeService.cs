using System.Text;
using System.Text.RegularExpressions;
using OpenKSeF.Domain.Models;
using QRCoder;

namespace OpenKSeF.Domain.Services;

/// <summary>
/// QR code generation using the Polish ZBP 2D standard.
/// Payload: NIP|PL|NRB|AMOUNT|NAME|TITLE|||
/// </summary>
public partial class QrCodeService : IQrCodeService
{
    internal const int MaxNameLength = 20;
    internal const int MaxTitleLength = 32;
    private const int NrbLength = 26;
    private const int NipLength = 10;
    private const string ManualAmount = "000000";

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
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.L);
        using var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(20);
    }

    public string BuildZbpPayload(TransferData data)
    {
        var account = SanitizeAccount(data.RecipientAccount);
        if (account.Length == 0)
            throw new InvalidOperationException("ZBP payload requires a valid NRB.");

        var nip = data.RecipientNip.Length == NipLength && data.RecipientNip.All(char.IsAsciiDigit)
            ? data.RecipientNip
            : string.Empty;
        var amount = data.ManualAmount ? ManualAmount : AmountToGrosze(data.Amount);
        var name = Sanitize(data.RecipientName, MaxNameLength);
        var title = Sanitize(data.Title, MaxTitleLength);

        return $"{nip}|PL|{account}|{amount}|{name}|{title}|||";
    }

    internal static string AmountToGrosze(decimal amount)
    {
        if (amount <= 0m)
            return ManualAmount;
        var grosze = Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        return ((long)grosze).ToString("D6");
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

        var nrb = AccountSeparatorsRegex().Replace(account, "");
        if (nrb.StartsWith("PL", StringComparison.OrdinalIgnoreCase))
            nrb = nrb[2..];

        return nrb.Length == NrbLength && nrb.All(char.IsAsciiDigit) && HasValidCheckDigits(nrb)
            ? nrb
            : string.Empty;
    }

    private static bool HasValidCheckDigits(string nrb)
    {
        var remainder = 0;
        foreach (var digit in nrb[2..] + "2521" + nrb[..2])
            remainder = (remainder * 10 + digit - '0') % 97;
        return remainder == 1;
    }

    [GeneratedRegex(@"[^a-zA-Z0-9 ,./\\\-@#&*_]")]
    private static partial Regex AllowedCharsRegex();

    [GeneratedRegex(@"[\s\-]")]
    private static partial Regex AccountSeparatorsRegex();
}
