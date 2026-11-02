using System.Globalization;

namespace OpenKSeF.Domain.Invoicing;

public static class InvoiceNumbering
{
    public const string DefaultPattern = "FV/{nr}/{MM}/{RRRR}";
    public const string NumberToken = "{nr}";

    public static bool HasNumberToken(string? pattern) => pattern?.Contains(NumberToken, StringComparison.Ordinal) == true;

    public static string PeriodKey(string pattern, DateOnly issueDate) =>
        issueDate.ToString(pattern.Contains("{MM}", StringComparison.Ordinal) ? "yyyy-MM" : "yyyy", CultureInfo.InvariantCulture);

    public static DateOnly PeriodStart(string period) =>
        DateOnly.ParseExact(period.Length == 4 ? $"{period}-01" : period, "yyyy-MM", CultureInfo.InvariantCulture);

    public static string Format(string pattern, int number, DateOnly issueDate) =>
        FormatDate(pattern.Replace(NumberToken, number.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), issueDate);

    public static (string Prefix, string Suffix) NumberAffixes(string pattern, DateOnly issueDate)
    {
        var parts = pattern.Split(NumberToken, 2);
        return (FormatDate(parts[0], issueDate), FormatDate(parts[1], issueDate));
    }

    private static string FormatDate(string text, DateOnly issueDate) => text
        .Replace("{MM}", issueDate.ToString("MM", CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{RRRR}", issueDate.ToString("yyyy", CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
