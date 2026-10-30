using System.Globalization;

namespace OpenKSeF.Domain.Invoicing;

public static class InvoiceNumbering
{
    public const string DefaultPattern = "FV/{nr}/{MM}/{RRRR}";
    public const string NumberToken = "{nr}";

    public static bool HasNumberToken(string? pattern) => pattern?.Contains(NumberToken, StringComparison.Ordinal) == true;

    public static string PeriodKey(string pattern, DateOnly issueDate) =>
        issueDate.ToString(pattern.Contains("{MM}", StringComparison.Ordinal) ? "yyyy-MM" : "yyyy", CultureInfo.InvariantCulture);

    public static string Format(string pattern, int number, DateOnly issueDate) => pattern
        .Replace(NumberToken, number.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{MM}", issueDate.ToString("MM", CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{RRRR}", issueDate.ToString("yyyy", CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
