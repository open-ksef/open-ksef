using System.Xml.Linq;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Tests.Invoicing;
using OpenKSeF.Sync.Issuing;

namespace OpenKSeF.Domain.Tests.Issuing;

public class Fa3CorrectionWriterTests
{
    private static readonly XNamespace Ns = Fa3InvoiceWriter.Namespace;
    private static readonly DateTime Now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly Fa3InvoiceWriter Writer = new();
    private const string OriginalKsef = "4444444444-20261103-0A1B2C-3D4E5F-AB";

    private static IssuedInvoice One23() => CorrectionTests.Accepted(i => i.ReplaceLines([("Usługa", null, 1, 100m, VatRate.Rate23)]));

    private static (IssuedInvoice Correction, Fa3Refs Refs) Kor(
        IssuedInvoice original,
        Action<IssuedInvoice, IssuedInvoice> edit,
        IssuedInvoice? state = null,
        CorrectionMode mode = CorrectionMode.Edit,
        string reason = "Zwrot towaru",
        string number = "KOR/1/11/2026")
    {
        state ??= original;
        var correction = IssuedInvoice.Correction(Guid.NewGuid(), original, state, mode, Today, Now);
        if (mode != CorrectionMode.WrongBuyerId)
            correction.SetCorrection(reason, null);
        edit(correction, state);
        correction.Issue(new SellerProfile(original.TenantId), "4444444444", number, Now, original, state);
        return (correction, new Fa3Refs(original, state));
    }

    private static Action<IssuedInvoice, IssuedInvoice> Lines(params (int? Base, string Name, decimal Qty, decimal Price, VatRate Rate)[] lines) =>
        (c, state) => c.ReplaceCorrectionLines(lines.Select(l => (l.Base, (string?)l.Name, (string?)state.Lines.FirstOrDefault(s => s.LineNo == l.Base)?.Unit, l.Qty, l.Price, l.Rate)), state);

    private static XElement Golden(string name, (IssuedInvoice Correction, Fa3Refs Refs) kor)
    {
        var result = Writer.Write(kor.Correction, Now, kor.Refs);
        Fa3GoldenTests.AssertGolden(name, result);
        return XDocument.Load(new MemoryStream(result.Xml)).Root!;
    }

    private static XElement FaOf(XElement root) => root.Element(Ns + "Fa")!;

    private static string? Fa(XElement root, string name) => FaOf(root).Element(Ns + name)?.Value;

    private static List<XElement> Rows(XElement root) => FaOf(root).Elements(Ns + "FaWiersz").ToList();

    private static string? Of(XElement row, string name) => row.Element(Ns + name)?.Value;

    [Fact]
    public void QuantityToZero_KorWithOriginalReferenceRowsDeltasAndNoPayment()
    {
        var root = Golden("kor-qty-to-zero", Kor(One23(), Lines((1, "Usługa", 0, 100m, VatRate.Rate23))));

        Assert.Equal("KOR", Fa(root, "RodzajFaktury"));
        Assert.Equal("Zwrot towaru", Fa(root, "PrzyczynaKorekty"));
        Assert.Null(Fa(root, "TypKorekty"));
        Assert.Null(Fa(root, "P_6"));
        var reference = FaOf(root).Element(Ns + "DaneFaKorygowanej")!;
        Assert.Equal("2026-11-03", Of(reference, "DataWystFaKorygowanej"));
        Assert.Equal("FV/3/11/2026", Of(reference, "NrFaKorygowanej"));
        Assert.Equal("1", Of(reference, "NrKSeF"));
        Assert.Equal(OriginalKsef, Of(reference, "NrKSeFFaKorygowanej"));
        var rows = Rows(root);
        Assert.Equal(["1", "2"], rows.Select(r => Of(r, "NrWierszaFa")));
        Assert.Equal(("1", "100.00", "1"), (Of(rows[0], "P_8B"), Of(rows[0], "P_11"), Of(rows[0], "StanPrzed")));
        Assert.Equal(("0", "0.00", null), (Of(rows[1], "P_8B"), Of(rows[1], "P_11"), Of(rows[1], "StanPrzed")));
        Assert.Equal("-100.00", Fa(root, "P_13_1"));
        Assert.Equal("-23.00", Fa(root, "P_14_1"));
        Assert.Equal("-123.00", Fa(root, "P_15"));
        Assert.Null(FaOf(root).Element(Ns + "Platnosc"));
    }

    [Fact]
    public void PriceUp_PositiveDeltaWithPaymentAndOriginalAccount()
    {
        var root = Golden("kor-price-up", Kor(One23(), Lines((1, "Usługa", 1, 120m, VatRate.Rate23)), reason: "Błąd w cenie lub ilości"));

        Assert.Equal("20.00", Fa(root, "P_13_1"));
        Assert.Equal("4.60", Fa(root, "P_14_1"));
        Assert.Equal("24.60", Fa(root, "P_15"));
        var payment = FaOf(root).Element(Ns + "Platnosc")!;
        Assert.Equal("2026-12-04", payment.Element(Ns + "TerminPlatnosci")!.Element(Ns + "Termin")!.Value);
        Assert.Equal("93000000002026111700000815", payment.Element(Ns + "RachunekBankowy")!.Element(Ns + "NrRB")!.Value);
    }

    [Fact]
    public void RateChange_TwoBuckets()
    {
        var root = Golden("kor-rate-change", Kor(One23(), Lines((1, "Usługa", 1, 100m, VatRate.Rate8)), reason: "Błąd w stawce VAT"));

        Assert.Equal(("-100.00", "-23.00", "100.00", "8.00", "-15.00"),
            (Fa(root, "P_13_1"), Fa(root, "P_14_1"), Fa(root, "P_13_2"), Fa(root, "P_14_2"), Fa(root, "P_15")));
    }

    [Fact]
    public void ThreeTimes033_OneRemoved_GroupVatDelta()
    {
        var original = CorrectionTests.Accepted(i => i.ReplaceLines([("A", null, 1, 0.33m, VatRate.Rate23), ("B", null, 1, 0.33m, VatRate.Rate23), ("C", null, 1, 0.33m, VatRate.Rate23)]));

        var root = Golden("kor-remove-line", Kor(original, Lines((1, "A", 1, 0.33m, VatRate.Rate23), (2, "B", 1, 0.33m, VatRate.Rate23))));

        Assert.Equal(("-0.33", "-0.08", "-0.41"), (Fa(root, "P_13_1"), Fa(root, "P_14_1"), Fa(root, "P_15")));
    }

    [Fact]
    public void SecondCorrection_StateAfterFirst_ReferencesOriginal()
    {
        var original = CorrectionTests.Accepted(i => i.ReplaceLines([("Usługa", null, 1, 1000m, VatRate.Rate23)]));
        var (first, _) = Kor(original, Lines((1, "Usługa", 1, 900m, VatRate.Rate23)), number: "KOR/1/11/2026");
        first.MarkAccepted("4444444444-20261120-0A1B2C-3D4E5F-CD", Now, 200, null, Now);

        var root = Golden("kor-second", Kor(original, Lines((1, "Usługa", 1, 700m, VatRate.Rate23)), state: Corrections.CurrentState(original, [first]), number: "KOR/2/11/2026"));

        Assert.Equal(("-200.00", "-46.00", "-246.00"), (Fa(root, "P_13_1"), Fa(root, "P_14_1"), Fa(root, "P_15")));
        Assert.Equal("900.00", Of(Rows(root)[0], "P_11"));
        Assert.Equal("FV/3/11/2026", FaOf(root).Element(Ns + "DaneFaKorygowanej")!.Element(Ns + "NrFaKorygowanej")!.Value);
        Assert.Single(FaOf(root).Elements(Ns + "DaneFaKorygowanej"));
    }

    [Fact]
    public void ChangedAddedUnchanged_RowsNumberedOneToThree()
    {
        var original = CorrectionTests.Accepted(i => i.ReplaceLines([("A", null, 1, 10m, VatRate.Rate23), ("B", null, 1, 20m, VatRate.Rate23), ("C", null, 1, 30m, VatRate.Rate23)]));

        var root = Golden("kor-changed-added", Kor(original, Lines((1, "A", 1, 10m, VatRate.Rate23), (2, "B", 2, 20m, VatRate.Rate23), (null, "D", 1, 5m, VatRate.Rate23), (3, "C", 1, 30m, VatRate.Rate23))));

        var rows = Rows(root);
        Assert.Equal(["1", "2", "3"], rows.Select(r => Of(r, "NrWierszaFa")));
        Assert.Equal(["B", "B", "D"], rows.Select(r => Of(r, "P_7")));
        Assert.Equal(["1", null, null], rows.Select(r => Of(r, "StanPrzed")));
    }

    [Fact]
    public void NameOnly_ZeroDeltaBucketWritten()
    {
        var root = Golden("kor-name-only", Kor(One23(), Lines((1, "Usługa programistyczna", 1, 100m, VatRate.Rate23)), reason: "Błąd w nazwie"));

        Assert.Equal(("0.00", "0.00", "0.00"), (Fa(root, "P_13_1"), Fa(root, "P_14_1"), Fa(root, "P_15")));
        Assert.Equal(2, Rows(root).Count);
    }

    [Fact]
    public void BuyerAddressOnly_Podmiot2KWithOldDataNoRowsNoBuckets()
    {
        var root = Golden("kor-buyer-data", Kor(One23(), (c, _) => c.SetCorrectionBuyer("Nabywca sp. z o.o.", "ul. Długa 22", "00-002 Warszawa", null), reason: "Błąd w danych nabywcy"));

        Assert.Equal("ul. Długa 22", root.Element(Ns + "Podmiot2")!.Element(Ns + "Adres")!.Element(Ns + "AdresL1")!.Value);
        var before = FaOf(root).Element(Ns + "Podmiot2K")!;
        Assert.Equal("3333333333", before.Element(Ns + "DaneIdentyfikacyjne")!.Element(Ns + "NIP")!.Value);
        Assert.Equal("Nabywca sp. z o.o.", before.Element(Ns + "DaneIdentyfikacyjne")!.Element(Ns + "Nazwa")!.Value);
        Assert.Equal("ul. Długa 2", before.Element(Ns + "Adres")!.Element(Ns + "AdresL1")!.Value);
        Assert.Empty(Rows(root));
        Assert.DoesNotContain(FaOf(root).Elements(), e => e.Name.LocalName.StartsWith("P_13_"));
        Assert.Equal("0.00", Fa(root, "P_15"));
    }

    [Fact]
    public void WrongBuyerId_ToZeroSameNipNoPodmiot2K()
    {
        var root = Golden("kor-wrong-nip", Kor(One23(), (_, _) => { }, mode: CorrectionMode.WrongBuyerId));

        Assert.Equal("3333333333", root.Element(Ns + "Podmiot2")!.Element(Ns + "DaneIdentyfikacyjne")!.Element(Ns + "NIP")!.Value);
        Assert.Null(FaOf(root).Element(Ns + "Podmiot2K"));
        Assert.Equal("Błędny NIP nabywcy", Fa(root, "PrzyczynaKorekty"));
        Assert.Equal("-123.00", Fa(root, "P_15"));
    }

    [Fact]
    public void ToZero_AfterOneCorrection_EveryLineZeroed()
    {
        var original = CorrectionTests.Accepted(i => i.ReplaceLines([("A", null, 2, 50m, VatRate.Rate23), ("B", null, 1, 100m, VatRate.Rate8)]));
        var (first, _) = Kor(original, Lines((1, "A", 2, 40m, VatRate.Rate23), (2, "B", 1, 100m, VatRate.Rate8)), number: "KOR/1/11/2026");
        first.MarkAccepted("4444444444-20261120-0A1B2C-3D4E5F-CD", Now, 200, null, Now);
        var state = Corrections.CurrentState(original, [first]);

        var root = Golden("kor-to-zero", Kor(original, (_, _) => { }, state: state, mode: CorrectionMode.ToZero, number: "KOR/2/11/2026"));

        Assert.Equal(4, Rows(root).Count);
        Assert.Equal(["2", "0", "1", "0"], Rows(root).Select(r => Of(r, "P_8B")));
        Assert.Equal("-206.40", Fa(root, "P_15"));
    }

    [Fact]
    public void GbpNpI_RateOfOriginalOnBothRows_P13_8Only()
    {
        var original = CorrectionTests.Accepted(i =>
        {
            i.SetBuyer(BuyerKind.Foreign, null, "Example Buyer Ltd", "1 Test Street", "Testville");
            i.SetForeignBuyer("GB", null);
            i.SetCurrency("GBP", 5.1234m, null);
            i.ReplaceLines([("Consulting / Konsultacje", "h", 10, 50m, VatRate.NotTaxable)]);
        });

        var root = Golden("kor-gbp-np1", Kor(original, Lines((1, "Consulting / Konsultacje", 8, 50m, VatRate.NotTaxable))));

        Assert.Equal("GBP", Fa(root, "KodWaluty"));
        Assert.Equal(["5.1234", "5.1234"], Rows(root).Select(r => Of(r, "KursWaluty")));
        Assert.Equal("-100.00", Fa(root, "P_13_8"));
        Assert.DoesNotContain(FaOf(root).Elements(), e => e.Name.LocalName.StartsWith("P_14_"));
        Assert.Equal("1", FaOf(root).Element(Ns + "Adnotacje")!.Element(Ns + "P_18")!.Value);
    }

    [Fact]
    public void EurNpII_KeepsPrefiksPodatnikaAndVatUeWithoutPrefix_EvenToZero()
    {
        var original = CorrectionTests.Accepted(i =>
        {
            i.SetBuyer(BuyerKind.Foreign, null, "Example Buyer GmbH", "1 Teststrasse", "Berlin");
            i.SetForeignBuyer("DE", "DE123456789");
            i.SetCurrency("EUR", 4.2m, null);
            i.ReplaceLines([("Consulting", "h", 10, 50m, VatRate.NotTaxableArt100)]);
        });

        var root = Golden("kor-eur-np2", Kor(original, (_, _) => { }, mode: CorrectionMode.ToZero, reason: "Anulowanie sprzedaży"));

        Assert.Equal("PL", root.Element(Ns + "Podmiot1")!.Element(Ns + "PrefiksPodatnika")!.Value);
        var ids = root.Element(Ns + "Podmiot2")!.Element(Ns + "DaneIdentyfikacyjne")!;
        Assert.Equal(("DE", "123456789"), (ids.Element(Ns + "KodUE")!.Value, ids.Element(Ns + "NrVatUE")!.Value));
        Assert.Equal("-500.00", Fa(root, "P_13_9"));
    }

    [Fact]
    public void Exempt_KeepsZwolnienieBasis()
    {
        var original = CorrectionTests.Accepted(i =>
        {
            i.ReplaceLines([("Szkolenie", null, 2, 400m, VatRate.Exempt)]);
            i.SetExemptionBasis(ExemptionBasisKind.Act, "art. 113 ust. 1 ustawy o VAT");
        });

        var root = Golden("kor-exempt", Kor(original, Lines((1, "Szkolenie", 1, 400m, VatRate.Exempt))));

        var exemption = FaOf(root).Element(Ns + "Adnotacje")!.Element(Ns + "Zwolnienie")!;
        Assert.Equal("1", exemption.Element(Ns + "P_19")!.Value);
        Assert.Equal("art. 113 ust. 1 ustawy o VAT", exemption.Element(Ns + "P_19A")!.Value);
    }

    [Fact]
    public void TypKorekty_WrittenOnlyWhenSet()
    {
        var root = Golden("kor-typ-2", Kor(One23(), (c, state) =>
        {
            Lines((1, "Usługa", 1, 90m, VatRate.Rate23))(c, state);
            c.SetCorrection("Rabat", 2);
        }));

        Assert.Equal("2", Fa(root, "TypKorekty"));
        Assert.Equal(["RodzajFaktury", "PrzyczynaKorekty", "TypKorekty", "DaneFaKorygowanej", "FaWiersz"],
            FaOf(root).Elements().Select(e => e.Name.LocalName).SkipWhile(n => n != "RodzajFaktury").Distinct());
    }
}
