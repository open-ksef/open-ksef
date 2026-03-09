using System.Text.RegularExpressions;

namespace OpenKSeF.Domain.Services;

public static partial class NipValidator
{
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
}
