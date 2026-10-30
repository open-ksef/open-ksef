using System.Xml.Linq;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Sync.Issuing;

namespace OpenKSeF.Domain.Tests.Issuing;

public class Fa3InvoiceWriterTests
{
    private const string SellerNip = "5260250274";
    private const string BuyerNip = "5261040828";
    private const string Nrb = "61109010140000071219812874";
    private static readonly XNamespace Ns = Fa3InvoiceWriter.Namespace;
    private static readonly DateOnly Day = new(2026, 11, 3);
    private static readonly DateTime Now = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Fa3InvoiceWriter Writer = new();

    internal static IssuedInvoice Issued(
        Action<IssuedInvoice> configure,
        bool cashAccounting = false,
        string? bankAccount = Nrb,
        string sellerNip = SellerNip,
        string buyerNip = BuyerNip,
        string number = "FV/1/11/2026")
    {
        var invoice = IssuedInvoice.Draft(Guid.NewGuid(), Guid.NewGuid(), Day, Now);
        invoice.SetBuyer(BuyerKind.Company, buyerNip, "Nabywca sp. z o.o.", "ul. Długa 2", "00-002 Warszawa");
        configure(invoice);
        var profile = new SellerProfile(invoice.TenantId);
        profile.Update("Sprzedawca sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", bankAccount, cashAccounting, InvoiceNumbering.DefaultPattern);
        invoice.Issue(profile, sellerNip, number, Now);
        return invoice;
    }

    private static XElement WriteValid(IssuedInvoice invoice)
    {
        var result = Writer.Write(invoice, Now);
        Assert.Empty(result.SchemaErrors);
        Assert.True(result.IsValid);
        return XDocument.Load(new MemoryStream(result.Xml)).Root!;
    }

    private static string? Fa(XElement root, string name) => root.Element(Ns + "Fa")!.Element(Ns + name)?.Value;

    private static string? Adnotacja(XElement root, params string[] path) =>
        path.Aggregate((XElement?)root.Element(Ns + "Fa")!.Element(Ns + "Adnotacje"), (e, n) => e?.Element(Ns + n))?.Value;

    [Fact]
    public void B2B23_HeaderPartiesTotalsAndPayment()
    {
        var root = WriteValid(Issued(i => i.AddLine("Usługa programistyczna", "godz.", 10, 150m, VatRate.Rate23)));

        Assert.Equal(Ns, root.Name.Namespace);
        var form = root.Element(Ns + "Naglowek")!.Element(Ns + "KodFormularza")!;
        Assert.Equal("FA", form.Value);
        Assert.Equal("FA (3)", form.Attribute("kodSystemowy")!.Value);
        Assert.Equal("1-0E", form.Attribute("wersjaSchemy")!.Value);
        Assert.Equal("3", root.Element(Ns + "Naglowek")!.Element(Ns + "WariantFormularza")!.Value);
        Assert.Equal(SellerNip, root.Element(Ns + "Podmiot1")!.Element(Ns + "DaneIdentyfikacyjne")!.Element(Ns + "NIP")!.Value);
        var buyer = root.Element(Ns + "Podmiot2")!;
        Assert.Equal(BuyerNip, buyer.Element(Ns + "DaneIdentyfikacyjne")!.Element(Ns + "NIP")!.Value);
        Assert.Equal("2", buyer.Element(Ns + "JST")!.Value);
        Assert.Equal("2", buyer.Element(Ns + "GV")!.Value);

        Assert.Equal("2026-11-03", Fa(root, "P_1"));
        Assert.Equal("FV/1/11/2026", Fa(root, "P_2"));
        Assert.Null(Fa(root, "P_6"));
        Assert.Equal("1500.00", Fa(root, "P_13_1"));
        Assert.Equal("345.00", Fa(root, "P_14_1"));
        Assert.Equal("1845.00", Fa(root, "P_15"));

        var line = root.Element(Ns + "Fa")!.Element(Ns + "FaWiersz")!;
        Assert.Equal("1", line.Element(Ns + "NrWierszaFa")!.Value);
        Assert.Equal("godz.", line.Element(Ns + "P_8A")!.Value);
        Assert.Equal("10", line.Element(Ns + "P_8B")!.Value);
        Assert.Equal("150.00", line.Element(Ns + "P_9A")!.Value);
        Assert.Equal("1500.00", line.Element(Ns + "P_11")!.Value);
        Assert.Equal("23", line.Element(Ns + "P_12")!.Value);

        var payment = root.Element(Ns + "Fa")!.Element(Ns + "Platnosc")!;
        Assert.Equal("2026-11-17", payment.Element(Ns + "TerminPlatnosci")!.Element(Ns + "Termin")!.Value);
        Assert.Equal("6", payment.Element(Ns + "FormaPlatnosci")!.Value);
        Assert.Equal(Nrb, payment.Element(Ns + "RachunekBankowy")!.Element(Ns + "NrRB")!.Value);
    }

    [Fact]
    public void B2B23_AdnotacjeDefaults()
    {
        var root = WriteValid(Issued(i => i.AddLine("Usługa", null, 1, 100m, VatRate.Rate23)));

        Assert.Equal("2", Adnotacja(root, "P_16"));
        Assert.Equal("2", Adnotacja(root, "P_17"));
        Assert.Equal("2", Adnotacja(root, "P_18"));
        Assert.Equal("2", Adnotacja(root, "P_18A"));
        Assert.Equal("1", Adnotacja(root, "Zwolnienie", "P_19N"));
        Assert.Equal("1", Adnotacja(root, "NoweSrodkiTransportu", "P_22N"));
        Assert.Equal("2", Adnotacja(root, "P_23"));
        Assert.Equal("1", Adnotacja(root, "PMarzy", "P_PMarzyN"));
    }

    [Fact]
    public void CashAccountingAndSplitPayment_SetP16AndP18A()
    {
        var root = WriteValid(Issued(i =>
        {
            i.AddLine("Usługa", null, 1, 20000m, VatRate.Rate23);
            i.SetPayment(PaymentMethod.Transfer, splitPayment: true);
        }, cashAccounting: true));

        Assert.Equal("1", Adnotacja(root, "P_16"));
        Assert.Equal("1", Adnotacja(root, "P_18A"));
    }

    [Fact]
    public void MultiRate_23_8_5_PerRateBuckets()
    {
        var root = WriteValid(Issued(i =>
        {
            i.AddLine("A", null, 2, 100.01m, VatRate.Rate23);
            i.AddLine("B", null, 3, 33.33m, VatRate.Rate8);
            i.AddLine("C", null, 1, 19.99m, VatRate.Rate5);
        }));

        Assert.Equal("200.02", Fa(root, "P_13_1"));
        Assert.Equal("46.00", Fa(root, "P_14_1"));
        Assert.Equal("99.99", Fa(root, "P_13_2"));
        Assert.Equal("8.00", Fa(root, "P_14_2"));
        Assert.Equal("19.99", Fa(root, "P_13_3"));
        Assert.Equal("1.00", Fa(root, "P_14_3"));
        Assert.Equal("375.00", Fa(root, "P_15"));
        Assert.Equal(["23", "8", "5"], root.Element(Ns + "Fa")!.Elements(Ns + "FaWiersz").Select(l => l.Element(Ns + "P_12")!.Value));
    }

    [Fact]
    public void ZeroDomestic_P13_6_1WithoutVat()
    {
        var root = WriteValid(Issued(i => i.AddLine("Towar", null, 1, 500m, VatRate.ZeroDomestic)));

        Assert.Equal("500.00", Fa(root, "P_13_6_1"));
        Assert.Equal("500.00", Fa(root, "P_15"));
        Assert.Equal("0 KR", root.Element(Ns + "Fa")!.Element(Ns + "FaWiersz")!.Element(Ns + "P_12")!.Value);
    }

    [Fact]
    public void Exempt_P13_7AndP19A()
    {
        var root = WriteValid(Issued(i =>
        {
            i.AddLine("Szkolenie", null, 1, 800m, VatRate.Exempt);
            i.SetExemptionBasis(ExemptionBasisKind.Act, "art. 113 ust. 1 ustawy o VAT");
        }));

        Assert.Equal("800.00", Fa(root, "P_13_7"));
        Assert.Equal("1", Adnotacja(root, "Zwolnienie", "P_19"));
        Assert.Equal("art. 113 ust. 1 ustawy o VAT", Adnotacja(root, "Zwolnienie", "P_19A"));
        Assert.Null(Adnotacja(root, "Zwolnienie", "P_19N"));
        Assert.Equal("zw", root.Element(Ns + "Fa")!.Element(Ns + "FaWiersz")!.Element(Ns + "P_12")!.Value);
    }

    [Fact]
    public void B2C_BrakIdWithNameAndAddress()
    {
        var root = WriteValid(Issued(i =>
        {
            i.SetBuyer(BuyerKind.Person, null, "Jan Kowalski", "ul. Krótka 3", "00-003 Warszawa");
            i.AddLine("Usługa", null, 1, 100m, VatRate.Rate23);
            i.SetPayment(PaymentMethod.Cash, false);
        }, bankAccount: null));

        var ids = root.Element(Ns + "Podmiot2")!.Element(Ns + "DaneIdentyfikacyjne")!;
        Assert.Equal("1", ids.Element(Ns + "BrakID")!.Value);
        Assert.Null(ids.Element(Ns + "NIP"));
        Assert.Equal("Jan Kowalski", ids.Element(Ns + "Nazwa")!.Value);
        Assert.Equal("ul. Krótka 3", root.Element(Ns + "Podmiot2")!.Element(Ns + "Adres")!.Element(Ns + "AdresL1")!.Value);
        var payment = root.Element(Ns + "Fa")!.Element(Ns + "Platnosc")!;
        Assert.Equal("1", payment.Element(Ns + "FormaPlatnosci")!.Value);
        Assert.Null(payment.Element(Ns + "RachunekBankowy"));
    }

    [Fact]
    public void Card_FormaPlatnosci2()
    {
        var root = WriteValid(Issued(i =>
        {
            i.AddLine("Usługa", null, 1, 100m, VatRate.Rate23);
            i.SetPayment(PaymentMethod.Card, false);
        }));

        Assert.Equal("2", root.Element(Ns + "Fa")!.Element(Ns + "Platnosc")!.Element(Ns + "FormaPlatnosci")!.Value);
    }

    [Fact]
    public void SaleDateDifferentFromIssueDate_WritesP6()
    {
        var root = WriteValid(Issued(i =>
        {
            i.AddLine("Usługa", null, 1, 100m, VatRate.Rate23);
            i.SetDates(Day, Day.AddDays(-5), Day.AddDays(14));
        }));

        Assert.Equal("2026-10-29", Fa(root, "P_6"));
    }

    [Fact]
    public void Bytes_AreUtf8WithoutBom()
    {
        var xml = Writer.Write(Issued(i => i.AddLine("Zażółć gęślą jaźń", null, 1, 100m, VatRate.Rate23)), Now).Xml;

        Assert.NotEqual(0xEF, xml[0]);
        Assert.Contains("Zażółć gęślą jaźń", System.Text.Encoding.UTF8.GetString(xml));
    }

    [Fact]
    public void SchemaViolation_ReturnsErrors()
    {
        var result = Writer.Write(Issued(i => i.AddLine("Usługa", null, 1, 100m, VatRate.Rate23), sellerNip: "12345"), Now);

        Assert.False(result.IsValid);
        Assert.Contains(result.SchemaErrors, e => e.Contains("NIP"));
    }
}
