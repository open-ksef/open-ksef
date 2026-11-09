using System.Globalization;

namespace OpenKSeF.Domain.Enums;

public enum SettlementState
{
    None = 1,
    Overpaid = 2,
    NothingDue = 3,
    Confirmed = 4,
    Unconfirmed = 5
}

/// <summary>
/// Classifies FA <c>Fa/Rozliczenie</c> against <c>P_15</c> (REQ-0036 "Rule: amount due"). Inputs are raw XML values, null when absent.
/// </summary>
public static class Settlement
{
    public static (SettlementState State, decimal? Amount) Classify(
        string? p15,
        IReadOnlyList<string> charges,
        string? chargesSum,
        IReadOnlyList<string> deductions,
        string? deductionsSum,
        string? amountDue,
        string? overpaid)
    {
        if (overpaid is not null)
            return Parse(overpaid) is { } over ? (SettlementState.Overpaid, over) : (SettlementState.Unconfirmed, null);

        if (amountDue is null)
            return charges.Count == 0 && deductions.Count == 0 && chargesSum is null && deductionsSum is null
                ? (SettlementState.None, null)
                : (SettlementState.Unconfirmed, null);

        if (Parse(amountDue) is not { } due)
            return (SettlementState.Unconfirmed, null);
        if (due <= 0m)
            return (SettlementState.NothingDue, due);

        var gross = Parse(p15);
        var added = Total(chargesSum, charges);
        var deducted = Total(deductionsSum, deductions);
        return gross + added - deducted == due
            ? (SettlementState.Confirmed, due)
            : (SettlementState.Unconfirmed, due);
    }

    private static decimal? Total(string? sum, IReadOnlyList<string> items)
    {
        if (sum is not null)
            return Parse(sum);
        decimal total = 0m;
        foreach (var item in items)
        {
            if (Parse(item) is not { } value)
                return null;
            total += value;
        }
        return total;
    }

    private static decimal? Parse(string? value) =>
        decimal.TryParse(value?.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
}
