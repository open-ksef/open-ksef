namespace OpenKSeF.Domain.Invoicing;

/// <summary>Advance (ZAL) split over rates, final (ROZ) remaining per rate and KOR_ZAL order rows (REQ-0028). Writer, view and totals all use it.</summary>
public static class Advances
{
    public const string ExceedsMessage = "Zaliczki przekraczają wartość rozliczenia. Najpierw skoryguj fakturę zaliczkową.";

    /// <summary>WartoscZamowienia: sum of P_11NettoZ + P_11VatZ, VAT rounded per line.</summary>
    public static decimal OrderValue(IEnumerable<IssuedInvoiceLine> order) => order.Sum(l => l.NetAmount + l.VatAmount);

    /// <summary>Gross per rate proportional to the order's gross per rate, rounded to grosze, remainder on the largest share; P_14 = round(G·r/(100+r), 2) (art. 106f ust. 1 pkt 3).</summary>
    public static IReadOnlyList<VatSummary> Split(IReadOnlyList<IssuedInvoiceLine> order, decimal amount)
    {
        var grossByRate = order.GroupBy(l => l.VatRate).OrderBy(g => g.Key)
            .Select(g => (Rate: g.Key, Gross: g.Sum(l => l.NetAmount + l.VatAmount)))
            .ToList();
        var total = grossByRate.Sum(r => r.Gross);
        if (total == 0)
            return grossByRate.Select(r => new VatSummary(r.Rate, 0m, 0m)).ToList();

        var shares = grossByRate.Select(r => Math.Round(amount * r.Gross / total, 2, MidpointRounding.AwayFromZero)).ToArray();
        var largest = grossByRate.FindIndex(r => r.Gross == grossByRate.Max(x => x.Gross));
        shares[largest] += amount - shares.Sum();

        return grossByRate.Select((r, i) =>
        {
            var vat = r.Rate.HasVatAmount() ? Math.Round(shares[i] * r.Rate.Fraction() / (1 + r.Rate.Fraction()), 2, MidpointRounding.AwayFromZero) : 0m;
            return new VatSummary(r.Rate, shares[i] - vat, vat);
        }).ToList();
    }

    public static IReadOnlyList<VatSummary> Subtract(IReadOnlyList<VatSummary> from, IReadOnlyList<VatSummary> minus)
    {
        var a = from.ToDictionary(s => s.VatRate);
        var b = minus.GroupBy(s => s.VatRate).ToDictionary(g => g.Key, g => (Net: g.Sum(s => s.Net), Vat: g.Sum(s => s.Vat)));
        return a.Keys.Union(b.Keys).Order()
            .Select(rate => new VatSummary(
                rate,
                (a.GetValueOrDefault(rate)?.Net ?? 0m) - b.GetValueOrDefault(rate).Net,
                (a.GetValueOrDefault(rate)?.Vat ?? 0m) - b.GetValueOrDefault(rate).Vat))
            .ToList();
    }

    /// <summary>Sum of the split of each advance's current state (after KOR_ZAL).</summary>
    public static IReadOnlyList<VatSummary> Deducted(IEnumerable<IssuedInvoice> advanceStates) =>
        advanceStates.SelectMany(a => Split(a.Lines, a.AdvanceAmount ?? 0m))
            .GroupBy(s => s.VatRate)
            .OrderBy(g => g.Key)
            .Select(g => new VatSummary(g.Key, g.Sum(s => s.Net), g.Sum(s => s.Vat)))
            .ToList();

    /// <summary>Zamowienie rows of a KOR_ZAL: changed lines as before/after pairs; when only the amount changes, every line as a pair (Podręcznik 2.13.5).</summary>
    public static IReadOnlyList<CorrectionRow> OrderRows(IssuedInvoice state, IssuedInvoice after)
    {
        var rows = Corrections.Rows(state.Lines, after.Lines);
        if (rows.Count > 0 || state.AdvanceAmount == after.AdvanceAmount)
            return rows;

        return state.Lines.OrderBy(l => l.LineNo)
            .SelectMany((l, i) => new[] { true, false }.Select(isBefore =>
                new CorrectionRow(i * 2 + (isBefore ? 1 : 2), isBefore, l.LineNo, l.Name, l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate, l.NetAmount)))
            .ToList();
    }
}
