namespace OpenKSeF.Domain.Invoicing;

public sealed record CorrectionRow(
    int RowNo,
    bool IsBefore,
    int? BaseLineNo,
    string Name,
    string Unit,
    decimal Quantity,
    decimal UnitNetPrice,
    VatRate VatRate,
    decimal NetAmount);

/// <summary>StanPrzed rows and per-rate deltas of a correction (REQ-0027), and the current state of an original. Writer, view and totals all use it.</summary>
public static class Corrections
{
    public const string WrongBuyerIdReason = "Błędny NIP nabywcy";

    public static IReadOnlyList<CorrectionRow> Rows(IEnumerable<IssuedInvoiceLine> state, IEnumerable<IssuedInvoiceLine> after)
    {
        var afterLines = after.OrderBy(l => l.LineNo).ToList();
        var byBase = afterLines.Where(l => l.BaseLineNo is not null).DistinctBy(l => l.BaseLineNo).ToDictionary(l => l.BaseLineNo!.Value);
        var rows = new List<CorrectionRow>();

        foreach (var before in state.OrderBy(l => l.LineNo))
        {
            byBase.TryGetValue(before.LineNo, out var changed);
            if (changed is not null && Same(before, changed))
                continue;

            rows.Add(Row(rows.Count + 1, true, before.LineNo, before));
            rows.Add(changed is null
                ? new CorrectionRow(rows.Count + 1, false, before.LineNo, before.Name, before.Unit, 0m, before.UnitNetPrice, before.VatRate, 0m)
                : Row(rows.Count + 1, false, before.LineNo, changed));
        }

        foreach (var added in afterLines.Where(l => l.BaseLineNo is null))
            rows.Add(Row(rows.Count + 1, false, null, added));

        return rows;
    }

    /// <summary>After minus state per rate, each side with group VAT (REQ-0024). Every rate touched by a row is listed, also with a zero delta.</summary>
    public static IReadOnlyList<VatSummary> Deltas(IReadOnlyList<IssuedInvoiceLine> state, IReadOnlyList<IssuedInvoiceLine> after)
    {
        var before = IssuedInvoice.SummaryOf(state).ToDictionary(s => s.VatRate);
        var now = IssuedInvoice.SummaryOf(after).ToDictionary(s => s.VatRate);
        return Rows(state, after)
            .Select(r => r.VatRate)
            .Distinct()
            .Order()
            .Select(rate => new VatSummary(
                rate,
                (now.GetValueOrDefault(rate)?.Net ?? 0m) - (before.GetValueOrDefault(rate)?.Net ?? 0m),
                (now.GetValueOrDefault(rate)?.Vat ?? 0m) - (before.GetValueOrDefault(rate)?.Vat ?? 0m)))
            .ToList();
    }

    /// <summary>Accepted documents of one original in acceptance order: the original, then its accepted corrections by IssuedAt, CreatedAt.</summary>
    public static IReadOnlyList<IssuedInvoice> AcceptedChain(IssuedInvoice original, IEnumerable<IssuedInvoice> corrections) =>
        corrections
            .Where(c => c.Status == IssuedInvoiceStatus.Accepted && c.CorrectedInvoiceId == original.Id)
            .OrderBy(c => c.IssuedAt)
            .ThenBy(c => c.CreatedAt)
            .Prepend(original)
            .ToList();

    public static IssuedInvoice CurrentState(IssuedInvoice original, IEnumerable<IssuedInvoice> corrections) =>
        AcceptedChain(original, corrections)[^1];

    /// <summary>The state a correction corrects: the accepted document before it, or the current state while it is not accepted.</summary>
    public static IssuedInvoice StateBefore(IssuedInvoice original, IEnumerable<IssuedInvoice> corrections, IssuedInvoice correction)
    {
        var chain = AcceptedChain(original, corrections);
        var index = chain.ToList().FindIndex(d => d.Id == correction.Id);
        return index > 0 ? chain[index - 1] : chain[^1];
    }

    public static bool BuyerDataDiffers(IssuedInvoice a, IssuedInvoice b) =>
        a.BuyerName != b.BuyerName
        || a.BuyerAddressLine1 != b.BuyerAddressLine1
        || a.BuyerAddressLine2 != b.BuyerAddressLine2
        || a.BuyerCountryCode != b.BuyerCountryCode;

    private static bool Same(IssuedInvoiceLine a, IssuedInvoiceLine b) =>
        a.Name == b.Name && a.Unit == b.Unit && a.Quantity == b.Quantity && a.UnitNetPrice == b.UnitNetPrice && a.VatRate == b.VatRate;

    private static CorrectionRow Row(int rowNo, bool isBefore, int? baseLineNo, IssuedInvoiceLine line) =>
        new(rowNo, isBefore, baseLineNo, line.Name, line.Unit, line.Quantity, line.UnitNetPrice, line.VatRate, line.NetAmount);
}
