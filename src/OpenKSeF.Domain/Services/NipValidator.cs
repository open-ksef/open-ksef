using System.Text.RegularExpressions;

namespace OpenKSeF.Domain.Services;

public static partial class NipValidator
{
    private static readonly int[] ChecksumWeights = [6, 5, 7, 2, 3, 4, 5, 6, 7];

    [GeneratedRegex(@"^\d{10}$")]
    private static partial Regex NipDigitsRegex();

    public static string Normalize(string nip)
    {
        ArgumentException.ThrowIfNullOrEmpty(nip);
        return nip.Replace("-", "");
    }

    public static bool IsValid(string nip)
    {
        var normalized = Normalize(nip);
        return NipDigitsRegex().IsMatch(normalized);
    }

    public static bool HasValidChecksum(string? nip)
    {
        if (string.IsNullOrEmpty(nip) || !IsValid(nip))
            return false;

        var digits = Normalize(nip);
        var sum = 0;
        for (var i = 0; i < ChecksumWeights.Length; i++)
            sum += (digits[i] - '0') * ChecksumWeights[i];
        return sum % 11 == digits[9] - '0';
    }
}
