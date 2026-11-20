using System.Text.Json;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests.Invoicing;

public class AdvanceTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly DateTime Now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

    private static JsonElement Vectors(string kind) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Invoicing", "AdvanceVectors.json"))).RootElement.GetProperty(kind);

    public static IEnumerable<object[]> SplitVectors() => Vectors("split").EnumerateArray().Select(v => new object[] { v.GetProperty("name").GetString()!, v.GetRawText() });

    public static IEnumerable<object[]> FinalVectors() => Vectors("final").EnumerateArray().Select(v => new object[] { v.GetProperty("name").GetString()!, v.GetRawText() });

    private static IEnumerable<(string?, string?, decimal, decimal, VatRate)> Lines(JsonElement lines) =>
        lines.EnumerateArray().Select(l => ((string?)"Pozycja", (string?)null, l.GetProperty("quantity").GetDecimal(), l.GetProperty("unitNetPrice").GetDecimal(),
            VatRates.Parse(l.GetProperty("vatRate").GetString()!)));

    private static List<(string?, decimal, decimal)> Buckets(JsonElement buckets) =>
        buckets.EnumerateArray().Select(b => (b.GetProperty("vatRate").GetString(), b.GetProperty("net").GetDecimal(), b.GetProperty("vat").GetDecimal())).ToList();

    internal static IssuedInvoice Zal(IEnumerable<(string?, string?, decimal, decimal, VatRate)> order, decimal amount)
    {
        var advance = IssuedInvoice.Advance(Guid.NewGuid(), Guid.NewGuid(), Today, Now);
        advance.ReplaceLines(order);
        advance.SetAdvanceAmount(amount);
        advance.RecalculateTotals(null);
        return advance;
    }

    [Theory]
    [MemberData(nameof(SplitVectors))]
    public void Split_MatchesSharedVectors(string name, string vectorJson)
    {
        var vector = JsonDocument.Parse(vectorJson).RootElement;
        var advance = Zal(Lines(vector.GetProperty("order")), vector.GetProperty("amount").GetDecimal());

        Assert.Equal(Buckets(vector.GetProperty("buckets")), advance.Buckets().Select(b => ((string?)b.VatRate.Code(), b.Net, b.Vat)).ToList());
        Assert.Equal(vector.GetProperty("orderValue").GetDecimal(), Advances.OrderValue(advance.Lines));
        Assert.Equal(vector.GetProperty("amount").GetDecimal(), advance.TotalGross);
        Assert.True(name.Length > 0);
    }

    [Theory]
    [MemberData(nameof(FinalVectors))]
    public void Final_RemainingPerBucketMatchesSharedVectors(string name, string vectorJson)
    {
        var vector = JsonDocument.Parse(vectorJson).RootElement;
        var lines = Lines(vector.GetProperty("lines")).ToList();
        var advances = vector.GetProperty("advances").EnumerateArray().Select(a => Zal(lines, a.GetDecimal())).ToList();
        var final = IssuedInvoice.ChainDocument(Guid.NewGuid(), IssuedInvoiceType.Roz, advances[0], advances[0], Today, Now);

        var deducted = Advances.Deducted(advances);
        final.RecalculateTotals(null, deducted);

        Assert.Equal(Buckets(vector.GetProperty("buckets")), final.Buckets(null, deducted).Select(b => ((string?)b.VatRate.Code(), b.Net, b.Vat)).ToList());
        Assert.Equal(vector.GetProperty("p15").GetDecimal(), final.TotalGross);
        Assert.Equal(vector.GetProperty("exceeds").GetBoolean(), IssueChecks.ChainErrors(final, deducted).Any(e => e.Code == IssueChecks.AdvancesExceedCode));
        Assert.True(name.Length > 0);
    }

    [Fact]
    public void ChainDocument_CopiesBuyerCurrencyAndLatestOrderButNotRate()
    {
        var first = Zal([("Projekt", "szt.", 1, 1000m, VatRate.NotTaxable)], 0m);
        first.SetBuyer(BuyerKind.Foreign, null, "Fictitious Ltd", "1 Test Street", "London");
        first.SetForeignBuyer("GB", "GB000000");
        first.SetCurrency("GBP", 5.1m, new DateOnly(2026, 11, 5));
        var order = Zal([("Projekt v2", "szt.", 2, 600m, VatRate.NotTaxable)], 0m);

        var next = IssuedInvoice.ChainDocument(Guid.NewGuid(), IssuedInvoiceType.Zal, first, order, Today, Now);

        Assert.Equal((first.OrderChainId, BuyerKind.Foreign, "Fictitious Ltd", "GB", "GBP"), (next.OrderChainId, next.BuyerKind, next.BuyerName, next.BuyerCountryCode, next.Currency));
        Assert.Null(next.ExchangeRate);
        Assert.Equal(("Projekt v2", 1200m), (next.Lines.Single().Name, next.Lines.Single().NetAmount));
        Assert.Equal(0m, next.AdvanceAmount);
    }

    [Fact]
    public void ChainAmountError_AboveRemainingOrderValue()
    {
        var advance = Zal([("Projekt", null, 1, 1000m, VatRate.Rate23)], 700m);

        Assert.Equal(IssueChecks.AdvanceAmountCode, IssueChecks.ChainAmountError(advance, 615m)!.Code);
        advance.SetAdvanceAmount(615m);
        Assert.Null(IssueChecks.ChainAmountError(advance, 615m));
    }

    [Fact]
    public void ChainErrors_PaymentDateAfterIssueDateAndZeroAmount()
    {
        var advance = Zal([("Projekt", null, 1, 1000m, VatRate.Rate23)], 0m);
        advance.SetDates(new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 6));

        Assert.Equal([IssueChecks.AdvanceAmountCode, IssueChecks.PaymentDateCode], IssueChecks.ChainErrors(advance, null).Select(e => e.Code));
    }

    [Fact]
    public void LateAdvance_WarnsAfterThe15thOfNextMonth()
    {
        var advance = Zal([("Projekt", null, 1, 1000m, VatRate.Rate23)], 100m);
        advance.SetDates(new DateOnly(2026, 12, 15), new DateOnly(2026, 11, 5), new DateOnly(2026, 12, 15));
        Assert.DoesNotContain(IssueChecks.Warnings(advance), w => w.Code == IssueChecks.LateAdvanceCode);

        advance.SetDates(new DateOnly(2026, 12, 16), new DateOnly(2026, 11, 5), new DateOnly(2026, 12, 16));
        Assert.Contains(IssueChecks.Warnings(advance), w => w.Code == IssueChecks.LateAdvanceCode);
    }

    [Fact]
    public void KorZal_AmountOnlyChangeWritesEveryOrderLineAsPairAndSplitDeltas()
    {
        var advance = Zal([("Projekt", null, 1, 1000m, VatRate.Rate23)], 369m);

        var correction = IssuedInvoice.Correction(Guid.NewGuid(), advance, advance, CorrectionMode.Edit, Today, Now);
        correction.SetAdvanceAmount(246m);
        correction.RecalculateTotals(advance);

        Assert.Equal(IssuedInvoiceType.KorZal, correction.InvoiceType);
        Assert.Equal((-100m, -23m, -123m), (correction.TotalNet, correction.TotalVat, correction.TotalGross));
        var rows = Advances.OrderRows(advance, correction);
        Assert.Equal([true, false], rows.Select(r => r.IsBefore));
    }

    [Fact]
    public void KorZal_ToZeroKeepsOrderAndRefundsTheAdvance()
    {
        var advance = Zal([("Projekt", null, 1, 1000m, VatRate.Rate23)], 369m);

        var correction = IssuedInvoice.Correction(Guid.NewGuid(), advance, advance, CorrectionMode.ToZero, Today, Now);

        Assert.Equal((0m, 1, -369m), (correction.AdvanceAmount, correction.Lines.Count, correction.TotalGross));
    }
}
