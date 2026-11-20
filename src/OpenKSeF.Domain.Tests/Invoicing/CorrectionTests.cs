using System.Text.Json;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Tests.Issuing;

namespace OpenKSeF.Domain.Tests.Invoicing;

public class CorrectionTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 20);

    public static IEnumerable<object[]> Vectors()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Invoicing", "CorrectionVectors.json"));
        foreach (var vector in JsonDocument.Parse(json).RootElement.EnumerateArray())
            yield return [vector.GetProperty("name").GetString()!, vector.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void RowsDeltasAndTotals_MatchSharedVectors(string name, string vectorJson)
    {
        var vector = JsonDocument.Parse(vectorJson).RootElement;
        var original = Accepted(i => ForeignIfNotTaxable(i, vector).ReplaceLines(vector.GetProperty("state").EnumerateArray().Select(l => (
            (string?)l.GetProperty("name").GetString(), (string?)l.GetProperty("unit").GetString(), l.GetProperty("quantity").GetDecimal(),
            l.GetProperty("unitNetPrice").GetDecimal(), VatRates.Parse(l.GetProperty("vatRate").GetString()!)))));
        var correction = IssuedInvoice.Correction(Guid.NewGuid(), original, original, CorrectionMode.Edit, Today, Now);

        correction.ReplaceCorrectionLines(vector.GetProperty("after").EnumerateArray().Select(l => (
            l.GetProperty("baseLineNo").ValueKind == JsonValueKind.Number ? l.GetProperty("baseLineNo").GetInt32() : (int?)null,
            (string?)l.GetProperty("name").GetString(), (string?)l.GetProperty("unit").GetString(), l.GetProperty("quantity").GetDecimal(),
            l.GetProperty("unitNetPrice").GetDecimal(), VatRates.Parse(l.GetProperty("vatRate").GetString()!))), original);

        var expected = vector.GetProperty("expected");
        var expectedRows = expected.GetProperty("rows").EnumerateArray().Select(r => new CorrectionRow(
            r.GetProperty("rowNo").GetInt32(), r.GetProperty("isBefore").GetBoolean(),
            r.GetProperty("baseLineNo").ValueKind == JsonValueKind.Number ? r.GetProperty("baseLineNo").GetInt32() : null,
            r.GetProperty("name").GetString()!, r.GetProperty("unit").GetString()!, r.GetProperty("quantity").GetDecimal(),
            r.GetProperty("unitNetPrice").GetDecimal(), VatRates.Parse(r.GetProperty("vatRate").GetString()!), r.GetProperty("netAmount").GetDecimal())).ToList();
        Assert.Equal(expectedRows, Corrections.Rows(original.Lines, correction.Lines));
        Assert.Equal(
            expected.GetProperty("deltas").EnumerateArray().Select(d => (d.GetProperty("vatRate").GetString(), d.GetProperty("net").GetDecimal(), d.GetProperty("vat").GetDecimal())).ToList(),
            Corrections.Deltas(original.Lines, correction.Lines).Select(d => ((string?)d.VatRate.Code(), d.Net, d.Vat)).ToList());
        Assert.Equal(expected.GetProperty("totalNet").GetDecimal(), correction.TotalNet);
        Assert.Equal(expected.GetProperty("totalVat").GetDecimal(), correction.TotalVat);
        Assert.Equal(expected.GetProperty("totalGross").GetDecimal(), correction.TotalGross);
        Assert.NotEmpty(name);
    }

    [Fact]
    public void Correction_CopiesBuyerCurrencyRateAndSellerSnapshot_LinkedToState()
    {
        var original = Accepted(i =>
        {
            i.SetBuyer(BuyerKind.Foreign, null, "Example Buyer Ltd", "1 Test Street", null);
            i.SetForeignBuyer("GB", null);
            i.SetCurrency("GBP", 5.1234m, null);
            i.ReplaceLines([("Consulting", "h", 10, 50m, VatRate.NotTaxable)]);
        });

        var correction = IssuedInvoice.Correction(Guid.NewGuid(), original, original, CorrectionMode.Edit, Today, Now);

        Assert.Equal(IssuedInvoiceType.Kor, correction.InvoiceType);
        Assert.Equal(original.Id, correction.CorrectedInvoiceId);
        Assert.Equal("GBP", correction.Currency);
        Assert.Equal(5.1234m, correction.ExchangeRate);
        Assert.Equal(original.SellerName, correction.SellerName);
        Assert.Equal(original.SellerBankAccount, correction.SellerBankAccount);
        Assert.Equal([1], correction.Lines.Select(l => l.BaseLineNo));
        Assert.Equal(0m, correction.TotalGross);
    }

    [Fact]
    public void WrongBuyerId_IsToZeroWithPresetReason()
    {
        var original = Accepted(i => i.ReplaceLines([("A", null, 1, 100m, VatRate.Rate23)]));

        var correction = IssuedInvoice.Correction(Guid.NewGuid(), original, original, CorrectionMode.WrongBuyerId, Today, Now);

        Assert.Empty(correction.Lines);
        Assert.Equal(Corrections.WrongBuyerIdReason, correction.CorrectionReason);
        Assert.Equal(-123m, correction.TotalGross);
    }

    [Fact]
    public void CurrentState_IsLatestAcceptedCorrection_AndStateBeforeIsThePreviousOne()
    {
        var original = Accepted(i => i.ReplaceLines([("A", null, 1, 1000m, VatRate.Rate23)]));
        var first = Correct(original, original, 900m, Now.AddMinutes(1));
        first.Issue(new SellerProfile(original.TenantId), "4444444444", "KOR/1/11/2026", Now.AddMinutes(1), original, original);
        first.MarkAccepted("4444444444-20261120-0A1B2C-3D4E5F-AB", Now, 200, null, Now);
        var second = IssuedInvoice.Correction(Guid.NewGuid(), original, Corrections.CurrentState(original, [first]), CorrectionMode.Edit, Today, Now.AddMinutes(2));

        Assert.Same(first, Corrections.CurrentState(original, [first, second]));
        Assert.Same(original, Corrections.StateBefore(original, [first, second], first));
        Assert.Same(first, Corrections.StateBefore(original, [first, second], second));
        Assert.Equal(900m, second.Lines.Single().UnitNetPrice);
    }

    [Fact]
    public void Issue_NoChange_NoReason_DateBeforeOriginal_AreBlocked()
    {
        var original = Accepted(i => i.ReplaceLines([("A", null, 1, 100m, VatRate.Rate23)]));
        var correction = IssuedInvoice.Correction(Guid.NewGuid(), original, original, CorrectionMode.Edit, Today, Now);
        correction.SetDates(original.IssueDate.AddDays(-1), original.IssueDate.AddDays(-1), Today.AddDays(14));

        var e = Assert.Throws<InvoiceValidationException>(() => correction.Issue(new SellerProfile(original.TenantId), "4444444444", "KOR/1/11/2026", Now, original, original));

        Assert.Contains(e.Errors, c => c.Code == IssueChecks.NoChangeCode);
        Assert.Contains(e.Errors, c => c.Code == IssueChecks.CorrectionReasonCode && c.Field == "correctionReason");
        Assert.Contains(e.Errors, c => c.Code == IssueChecks.CorrectionIssueDateCode && c.Field == "issueDate");
    }

    [Fact]
    public void Issue_KeepsSellerSnapshotOfOriginal_AndUsesCorrectionPatternPeriod()
    {
        var original = Accepted(i => i.ReplaceLines([("A", null, 1, 100m, VatRate.Rate23)]));
        var correction = Correct(original, original, 120m, Now);
        var profile = new SellerProfile(original.TenantId);
        profile.Update("Inna nazwa", "ul. Inna 9", null, null, false, InvoiceNumbering.DefaultPattern);

        correction.Issue(profile, "4444444444", "KOR/1/11/2026", Now, original, original);

        Assert.Equal(original.SellerName, correction.SellerName);
        Assert.Equal("KOR/1/11/2026", correction.Number);
        Assert.Equal("2026-11", correction.NumberPeriod);
        Assert.Equal(24.60m, correction.TotalGross);
    }

    private static IssuedInvoice ForeignIfNotTaxable(IssuedInvoice invoice, JsonElement vector)
    {
        if (vector.GetProperty("state").EnumerateArray().Any(l => l.GetProperty("vatRate").GetString() == "np I"))
        {
            invoice.SetBuyer(BuyerKind.Foreign, null, "Example Buyer Ltd", "1 Test Street", null);
            invoice.SetForeignBuyer("GB", null);
        }

        return invoice;
    }

    internal static IssuedInvoice Accepted(Action<IssuedInvoice> configure)
    {
        var invoice = Fa3InvoiceWriterTests.Issued(configure, number: "FV/3/11/2026", now: new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc));
        invoice.MarkAccepted("4444444444-20261103-0A1B2C-3D4E5F-AB", Now, 200, null, Now);
        return invoice;
    }

    internal static IssuedInvoice Correct(IssuedInvoice original, IssuedInvoice state, decimal newPrice, DateTime now)
    {
        var correction = IssuedInvoice.Correction(Guid.NewGuid(), original, state, CorrectionMode.Edit, Today, now);
        correction.ReplaceCorrectionLines(state.Lines.Select(l => ((int?)l.LineNo, (string?)l.Name, (string?)l.Unit, l.Quantity, newPrice, l.VatRate)), state);
        correction.SetCorrection("Błąd w cenie lub ilości", null);
        return correction;
    }
}
