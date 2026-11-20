using System.Xml.Linq;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Sync.Issuing;

namespace OpenKSeF.Domain.Tests.Issuing;

public class Fa3AdvanceWriterTests
{
    private static readonly XNamespace Ns = Fa3InvoiceWriter.Namespace;
    private static readonly Fa3InvoiceWriter Writer = new();
    private static readonly Guid Tenant = Guid.NewGuid();
    private const string SellerNip = "4444444444";
    private const string Zal1Ksef = "4444444444-20261106-0A1B2C-3D4E5F-AB";
    private const string Zal2Ksef = "4444444444-20261110-1A1B2C-3D4E5F-AC";
    private const string RozKsef = "4444444444-20261120-2A1B2C-3D4E5F-AD";

    private static DateTime At(int day) => new(2026, 11, day, 10, 0, 0, DateTimeKind.Utc);

    private static SellerProfile Profile()
    {
        var profile = new SellerProfile(Tenant);
        profile.Update("Sprzedawca sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "93000000002026111700000815", false, InvoiceNumbering.DefaultPattern);
        return profile;
    }

    private static IssuedInvoice Zal(
        decimal amount, DateOnly payment, int issueDay, string number, IssuedInvoice? first = null,
        Action<IssuedInvoice>? configure = null, params (string Name, decimal Net, VatRate Rate)[] order)
    {
        var now = At(issueDay);
        var today = DateOnly.FromDateTime(now);
        var advance = first is null
            ? IssuedInvoice.Advance(Guid.NewGuid(), Tenant, today, now)
            : IssuedInvoice.ChainDocument(Guid.NewGuid(), IssuedInvoiceType.Zal, first, first, today, now);
        if (first is null)
            advance.SetBuyer(BuyerKind.Company, "3333333333", "Nabywca sp. z o.o.", "ul. Długa 2", "00-002 Warszawa");
        configure?.Invoke(advance);
        if (order.Length > 0)
            advance.ReplaceLines(order.Select(o => ((string?)o.Name, (string?)null, 1m, o.Net, o.Rate)));
        advance.SetAdvanceAmount(amount);
        advance.SetDates(today, payment, today);
        advance.Issue(Profile(), SellerNip, number, now);
        return advance;
    }

    private static IssuedInvoice Accept(IssuedInvoice invoice, string ksef)
    {
        invoice.MarkAccepted(ksef, invoice.IssuedAt!.Value, 200, null, invoice.IssuedAt.Value);
        return invoice;
    }

    private static IssuedInvoice Zal369() =>
        Accept(Zal(369m, new DateOnly(2026, 11, 5), 6, "ZAL/1/11/2026", order: ("Projekt strony", 1000m, VatRate.Rate23)), Zal1Ksef);

    private static IssuedInvoice Roz(IssuedInvoice first, IReadOnlyList<IssuedInvoice> advances, out Fa3Refs refs, params (string Name, decimal Net, VatRate Rate)[] lines)
    {
        var now = At(20);
        var final = IssuedInvoice.ChainDocument(Guid.NewGuid(), IssuedInvoiceType.Roz, first, advances[^1], DateOnly.FromDateTime(now), now);
        if (lines.Length > 0)
            final.ReplaceLines(lines.Select(o => ((string?)o.Name, (string?)null, 1m, o.Net, o.Rate)));
        var deducted = Advances.Deducted(advances);
        final.Issue(Profile(), SellerNip, "FV/7/11/2026", now, deducted: deducted);
        refs = new Fa3Refs(null, null, null, advances.Select(a => a.KsefNumber!).ToList(), deducted);
        return final;
    }

    private static XElement Golden(string name, IssuedInvoice invoice, Fa3Refs? refs = null)
    {
        var result = Writer.Write(invoice, At(20), refs);
        Fa3GoldenTests.AssertGolden(name, result);
        return XDocument.Load(new MemoryStream(result.Xml)).Root!.Element(Ns + "Fa")!;
    }

    private static string? Of(XElement e, string name) => e.Element(Ns + name)?.Value;

    [Fact]
    public void Zal_OrderSplitPaidAndNoLines()
    {
        var fa = Golden("zal-first", Zal369());

        Assert.Equal("ZAL", Of(fa, "RodzajFaktury"));
        Assert.Equal(("2026-11-06", "2026-11-05"), (Of(fa, "P_1"), Of(fa, "P_6")));
        Assert.Equal(("300.00", "69.00", "369.00"), (Of(fa, "P_13_1"), Of(fa, "P_14_1"), Of(fa, "P_15")));
        Assert.Empty(fa.Elements(Ns + "FaWiersz"));
        Assert.Empty(fa.Elements(Ns + "FakturaZaliczkowa"));
        var payment = fa.Element(Ns + "Platnosc")!;
        Assert.Equal(("1", "2026-11-05", null), (Of(payment, "Zaplacono"), Of(payment, "DataZaplaty"), Of(payment, "TerminPlatnosci")));
        var order = fa.Element(Ns + "Zamowienie")!;
        Assert.Equal("1230.00", Of(order, "WartoscZamowienia"));
        var row = Assert.Single(order.Elements(Ns + "ZamowienieWiersz"));
        Assert.Equal(("1", "1000.00", "230.00", "23"), (Of(row, "NrWierszaZam"), Of(row, "P_11NettoZ"), Of(row, "P_11VatZ"), Of(row, "P_12Z")));
        Assert.Equal("Zamowienie", fa.Elements().Last().Name.LocalName);
    }

    [Fact]
    public void SecondZal_OwnAmountSplit()
    {
        var first = Zal369();
        var fa = Golden("zal-second", Zal(246m, new DateOnly(2026, 11, 10), 10, "ZAL/2/11/2026", first));

        Assert.Equal(("200.00", "46.00", "246.00"), (Of(fa, "P_13_1"), Of(fa, "P_14_1"), Of(fa, "P_15")));
        Assert.Null(Of(fa, "P_6"));
    }

    [Fact]
    public void MultiRate_ProportionalSplit()
    {
        var fa = Golden("zal-multi-rate", Zal(115.50m, new DateOnly(2026, 11, 6), 6, "ZAL/3/11/2026",
            order: [("Usługa", 100m, VatRate.Rate23), ("Książka", 100m, VatRate.Rate8)]));

        Assert.Equal(("50.00", "11.50", "50.00", "4.00", "115.50"), (Of(fa, "P_13_1"), Of(fa, "P_14_1"), Of(fa, "P_13_2"), Of(fa, "P_14_2"), Of(fa, "P_15")));
        Assert.Equal("231.00", Of(fa.Element(Ns + "Zamowienie")!, "WartoscZamowienia"));
    }

    [Fact]
    public void Roz_FullLinesAdvancesListedAndRemainingPerBucket()
    {
        var first = Zal369();
        var second = Accept(Zal(246m, new DateOnly(2026, 11, 10), 10, "ZAL/2/11/2026", first), Zal2Ksef);
        var final = Roz(first, [first, second], out var refs);
        var fa = Golden("roz", final, refs);

        Assert.Equal("ROZ", Of(fa, "RodzajFaktury"));
        Assert.Equal([Zal1Ksef, Zal2Ksef], fa.Elements(Ns + "FakturaZaliczkowa").Select(f => Of(f, "NrKSeFFaZaliczkowej")));
        Assert.Equal("1000.00", Of(fa.Element(Ns + "FaWiersz")!, "P_11"));
        Assert.Equal(("500.00", "115.00", "615.00"), (Of(fa, "P_13_1"), Of(fa, "P_14_1"), Of(fa, "P_15")));
        Assert.Null(fa.Element(Ns + "Zamowienie"));
        Assert.NotNull(fa.Element(Ns + "Platnosc")!.Element(Ns + "TerminPlatnosci"));
    }

    [Fact]
    public void LastZalListsEarlierAdvances_ThenZeroRoz()
    {
        var first = Zal369();
        var last = Zal(861m, new DateOnly(2026, 11, 10), 10, "ZAL/2/11/2026", first);
        var lastFa = Golden("zal-last", last, new Fa3Refs(null, null, null, [Zal1Ksef]));
        Assert.Equal([Zal1Ksef], lastFa.Elements(Ns + "FakturaZaliczkowa").Select(f => Of(f, "NrKSeFFaZaliczkowej")));
        Accept(last, Zal2Ksef);

        var fa = Golden("roz-zero", Roz(first, [first, last], out var refs), refs);

        Assert.Equal(("0.00", "0.00", "0.00"), (Of(fa, "P_13_1"), Of(fa, "P_14_1"), Of(fa, "P_15")));
        Assert.Equal("1000.00", Of(fa.Element(Ns + "FaWiersz")!, "P_11"));
        Assert.Null(fa.Element(Ns + "Platnosc"));
    }

    [Fact]
    public void GbpNp1Zal_ExchangeRateNoVat()
    {
        var advance = Zal(2000m, new DateOnly(2026, 11, 6), 6, "ZAL/4/11/2026", configure: i =>
        {
            i.SetBuyer(BuyerKind.Foreign, null, "Fictitious Trading Ltd", "1 Example Street", "London");
            i.SetForeignBuyer("GB", "GB000000000");
            i.SetCurrency("GBP", 5.1234m, new DateOnly(2026, 11, 5));
        }, order: ("Consulting", 5000m, VatRate.NotTaxable));
        var fa = Golden("zal-gbp-np1", advance);

        Assert.Equal(("GBP", "2000.00", "2000.00", "5.1234"), (Of(fa, "KodWaluty"), Of(fa, "P_13_8"), Of(fa, "P_15"), Of(fa, "KursWalutyZ")));
        Assert.Empty(fa.Elements().Where(e => e.Name.LocalName.StartsWith("P_14", StringComparison.Ordinal)));
        Assert.Equal("1", fa.Element(Ns + "Adnotacje")!.Element(Ns + "P_18")!.Value);
    }

    [Fact]
    public void KorZal_AmountDownBeforeAfterOrderRowsAndP15ZK()
    {
        var zal = Zal369();
        var correction = IssuedInvoice.Correction(Guid.NewGuid(), zal, zal, CorrectionMode.Edit, new DateOnly(2026, 11, 20), At(20));
        correction.SetCorrection("Zwrot części zaliczki", null);
        correction.SetAdvanceAmount(246m);
        correction.Issue(Profile(), SellerNip, "KOR/1/11/2026", At(20), zal, zal);
        var fa = Golden("kor-zal", correction, new Fa3Refs(zal, zal, 369m));

        Assert.Equal("KOR_ZAL", Of(fa, "RodzajFaktury"));
        Assert.Equal(("-100.00", "-23.00", "-123.00", "369.00"), (Of(fa, "P_13_1"), Of(fa, "P_14_1"), Of(fa, "P_15"), Of(fa, "P_15ZK")));
        Assert.Equal("ZAL/1/11/2026", Of(fa.Element(Ns + "DaneFaKorygowanej")!, "NrFaKorygowanej"));
        var order = fa.Element(Ns + "Zamowienie")!;
        Assert.Equal("1230.00", Of(order, "WartoscZamowienia"));
        Assert.Equal(["1", null], order.Elements(Ns + "ZamowienieWiersz").Select(r => Of(r, "StanPrzedZ")));
        Assert.Empty(fa.Elements(Ns + "FaWiersz"));
        Assert.Null(fa.Element(Ns + "Platnosc"));
    }

    [Fact]
    public void KorRozToZero_ReversesFullLinesKeepsRemainingBefore()
    {
        var first = Zal369();
        var second = Accept(Zal(246m, new DateOnly(2026, 11, 10), 10, "ZAL/2/11/2026", first), Zal2Ksef);
        var final = Accept(Roz(first, [first, second], out _), RozKsef);
        var correction = IssuedInvoice.Correction(Guid.NewGuid(), final, final, CorrectionMode.ToZero, new DateOnly(2026, 11, 20), At(20));
        correction.SetCorrection("Anulowanie", null);
        correction.Issue(Profile(), SellerNip, "KOR/3/11/2026", At(20), final, final);
        var result = Writer.Write(correction, At(20), new Fa3Refs(final, final, final.TotalGross));

        Assert.Empty(result.SchemaErrors);
        Assert.Equal((IssuedInvoiceType.KorRoz, 0, -1230m), (correction.InvoiceType, correction.Lines.Count, correction.TotalGross));
        var fa = XDocument.Load(new MemoryStream(result.Xml)).Root!.Element(Ns + "Fa")!;
        Assert.Equal(("-1000.00", "-230.00", "-1230.00", "615.00"), (Of(fa, "P_13_1"), Of(fa, "P_14_1"), Of(fa, "P_15"), Of(fa, "P_15ZK")));
        Assert.Null(fa.Element(Ns + "Platnosc"));
    }

    [Fact]
    public void KorZalBuyerDataOnly_NoZamowienieRows()
    {
        var zal = Zal369();
        var correction = IssuedInvoice.Correction(Guid.NewGuid(), zal, zal, CorrectionMode.Edit, new DateOnly(2026, 11, 20), At(20));
        correction.SetCorrection("Zmiana adresu nabywcy", null);
        correction.SetCorrectionBuyer(zal.BuyerName, "ul. Nowa 5", zal.BuyerAddressLine2, null);
        correction.Issue(Profile(), SellerNip, "KOR/4/11/2026", At(20), zal, zal);
        var result = Writer.Write(correction, At(20), new Fa3Refs(zal, zal, 369m));

        Assert.Empty(result.SchemaErrors);
        var fa = XDocument.Load(new MemoryStream(result.Xml)).Root!.Element(Ns + "Fa")!;
        Assert.NotNull(fa.Element(Ns + "Podmiot2K"));
        Assert.Null(fa.Element(Ns + "Zamowienie"));
        Assert.Equal("0.00", Of(fa, "P_15"));
    }

    [Fact]
    public void KorRoz_LineDownDeltasAndRemainingBefore()
    {
        var first = Zal369();
        var second = Accept(Zal(246m, new DateOnly(2026, 11, 10), 10, "ZAL/2/11/2026", first), Zal2Ksef);
        var final = Accept(Roz(first, [first, second], out _), RozKsef);
        var correction = IssuedInvoice.Correction(Guid.NewGuid(), final, final, CorrectionMode.Edit, new DateOnly(2026, 11, 20), At(20));
        correction.SetCorrection("Rabat", null);
        correction.ReplaceCorrectionLines([(1, "Projekt strony", "szt.", 1m, 900m, VatRate.Rate23)], final);
        correction.Issue(Profile(), SellerNip, "KOR/2/11/2026", At(20), final, final);
        var fa = Golden("kor-roz", correction, new Fa3Refs(final, final, final.TotalGross));

        Assert.Equal("KOR_ROZ", Of(fa, "RodzajFaktury"));
        Assert.Equal(("-100.00", "-23.00", "-123.00", "615.00"), (Of(fa, "P_13_1"), Of(fa, "P_14_1"), Of(fa, "P_15"), Of(fa, "P_15ZK")));
        Assert.Equal(["1", null], fa.Elements(Ns + "FaWiersz").Select(r => Of(r, "StanPrzed")));
        Assert.Empty(fa.Elements(Ns + "FakturaZaliczkowa"));
    }
}
