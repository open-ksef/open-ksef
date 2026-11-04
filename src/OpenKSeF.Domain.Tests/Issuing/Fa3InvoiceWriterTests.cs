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
        string number = "FV/1/11/2026",
        Guid? tenantId = null,
        DateTime? now = null,
        Action<SellerProfile>? configureProfile = null)
    {
        var at = now ?? Now;
        var invoice = IssuedInvoice.Draft(Guid.NewGuid(), tenantId ?? Guid.NewGuid(), DateOnly.FromDateTime(at), at);
        invoice.SetBuyer(BuyerKind.Company, buyerNip, "Nabywca sp. z o.o.", "ul. Długa 2", "00-002 Warszawa");
        configure(invoice);
        var profile = new SellerProfile(invoice.TenantId);
        profile.Update("Sprzedawca sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", bankAccount, cashAccounting, InvoiceNumbering.DefaultPattern);
        configureProfile?.Invoke(profile);
        invoice.Issue(profile, sellerNip, number, at);
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

    private const string MadeUpIban = "GB82WEST12345698765432";

    private static void FullBankDetails(SellerProfile profile) =>
        profile.SetBankDetails("TESTPLPW", "Bank Testowy S.A.", MadeUpIban, "TESTGB2L", "West Test Bank");

    private static IssuedInvoice CompanyInvoice(Action<SellerProfile>? configureProfile = null) => Issued(i =>
    {
        i.AddLine("Usługa", "godz.", 10, 150m, VatRate.Rate23);
        i.AddLine("Towar", null, 1.5m, 33.33m, VatRate.Rate8);
        i.AddLine("Książka", null, 2, 19.99m, VatRate.Rate5);
        i.AddLine("Eksport krajowy", null, 1, 500m, VatRate.ZeroDomestic);
        i.AddLine("Szkolenie", null, 1, 800m, VatRate.Exempt);
        i.SetExemptionBasis(ExemptionBasisKind.Act, "art. 113 ust. 1 ustawy o VAT");
        i.SetDates(Day, Day.AddDays(-5), Day.AddDays(14));
        i.SetPayment(PaymentMethod.Transfer, splitPayment: true);
    }, cashAccounting: true, configureProfile: configureProfile);

    private static IssuedInvoice PersonInvoice() => Issued(i =>
    {
        i.SetBuyer(BuyerKind.Person, null, "Jan Kowalski", "ul. Krótka 3", null);
        i.AddLine("Usługa", null, 1, 100m, VatRate.Rate23);
        i.SetPayment(PaymentMethod.Cash, false);
    }, bankAccount: null);

    private static string WrittenText(IssuedInvoice invoice) =>
        System.Text.Encoding.UTF8.GetString(Writer.Write(invoice, Now).Xml).Replace("\r\n", "\n");

    private static string Golden(string xml) => xml.Replace("\r\n", "\n");

    [Fact]
    public void Domestic_XmlEqualsBytesWrittenBeforeForeignInvoices()
    {
        Assert.Equal(Golden(CompanyGolden), WrittenText(CompanyInvoice()));
        Assert.Equal(Golden(PersonGolden), WrittenText(PersonInvoice()));
    }

    [Fact]
    public void Domestic_IgnoresSwiftAndForeignAccountInProfile()
    {
        Assert.Equal(Golden(CompanyGolden), WrittenText(CompanyInvoice(FullBankDetails)));
    }

    private static IssuedInvoice ForeignInvoice(
        string country,
        string? foreignId,
        VatRate rate,
        string currency = "GBP",
        decimal? exchangeRate = 5.2133m,
        Action<SellerProfile>? configureProfile = null) => Issued(i =>
    {
        i.SetBuyer(BuyerKind.Foreign, null, "Example Buyer Ltd", "1 Test Street", "Testville");
        i.SetForeignBuyer(country, foreignId);
        i.SetCurrency(currency, exchangeRate, null);
        i.AddLine("Consulting / Konsultacje", "h", 10, 50m, rate);
    }, configureProfile: configureProfile);

    private static XElement Buyer(XElement root) => root.Element(Ns + "Podmiot2")!;

    private static XElement BuyerIds(XElement root) => Buyer(root).Element(Ns + "DaneIdentyfikacyjne")!;

    private static XElement FirstLine(XElement root) => root.Element(Ns + "Fa")!.Element(Ns + "FaWiersz")!;

    private static XElement BankAccount(XElement root) =>
        root.Element(Ns + "Fa")!.Element(Ns + "Platnosc")!.Element(Ns + "RachunekBankowy")!;

    private static IEnumerable<string> ChildNames(XElement element) => element.Elements().Select(e => e.Name.LocalName);

    [Fact]
    public void NpI_ThirdCountryWithoutTaxId_BrakIdCurrencyRateAndP18()
    {
        var root = WriteValid(ForeignInvoice("GB", null, VatRate.NotTaxable));

        Assert.Equal("1", BuyerIds(root).Element(Ns + "BrakID")!.Value);
        Assert.Equal("GB", Buyer(root).Element(Ns + "Adres")!.Element(Ns + "KodKraju")!.Value);
        Assert.Equal("GBP", Fa(root, "KodWaluty"));
        Assert.Equal("500.00", Fa(root, "P_13_8"));
        Assert.Equal("500.00", Fa(root, "P_15"));
        Assert.Empty(root.Element(Ns + "Fa")!.Elements().Where(e => e.Name.LocalName.StartsWith("P_14_")));
        Assert.Equal("1", Adnotacja(root, "P_18"));
        Assert.Equal("2", Adnotacja(root, "P_18A"));
        Assert.Equal("1", Adnotacja(root, "Zwolnienie", "P_19N"));
        Assert.Equal("np I", FirstLine(root).Element(Ns + "P_12")!.Value);
        Assert.Equal("5.2133", FirstLine(root).Element(Ns + "KursWaluty")!.Value);
        Assert.Null(root.Element(Ns + "Podmiot1")!.Element(Ns + "PrefiksPodatnika"));
    }

    [Fact]
    public void NpI_ThirdCountryWithTaxId_KodKrajuAndNrIdWithoutBrakId()
    {
        var root = WriteValid(ForeignInvoice("GB", "GB-TEST-000111", VatRate.NotTaxable));

        Assert.Equal(["KodKraju", "NrID", "Nazwa"], ChildNames(BuyerIds(root)));
        Assert.Equal("GB", BuyerIds(root).Element(Ns + "KodKraju")!.Value);
        Assert.Equal("GB-TEST-000111", BuyerIds(root).Element(Ns + "NrID")!.Value);
    }

    [Fact]
    public void NpII_EuBuyer_KodUeNrVatUeWithoutPrefixP13_9AndPrefiksPodatnika()
    {
        var root = WriteValid(ForeignInvoice("DE", "DE 123 456 789", VatRate.NotTaxableArt100, "EUR", 4.2m));

        Assert.Equal(["KodUE", "NrVatUE", "Nazwa"], ChildNames(BuyerIds(root)));
        Assert.Equal("DE", BuyerIds(root).Element(Ns + "KodUE")!.Value);
        Assert.Equal("123456789", BuyerIds(root).Element(Ns + "NrVatUE")!.Value);
        Assert.Equal("500.00", Fa(root, "P_13_9"));
        Assert.Equal("1", Adnotacja(root, "P_18"));
        Assert.Equal("PL", root.Element(Ns + "Podmiot1")!.Elements().First().Value);
        Assert.Equal("PrefiksPodatnika", root.Element(Ns + "Podmiot1")!.Elements().First().Name.LocalName);
    }

    [Theory]
    [InlineData("GR")]
    [InlineData("EL")]
    public void NpII_Greece_AddressGrAndVatPrefixEl(string entered)
    {
        var root = WriteValid(ForeignInvoice(entered, "EL123456789", VatRate.NotTaxableArt100, "EUR", 4.2m));

        Assert.Equal("EL", BuyerIds(root).Element(Ns + "KodUE")!.Value);
        Assert.Equal("123456789", BuyerIds(root).Element(Ns + "NrVatUE")!.Value);
        Assert.Equal("GR", Buyer(root).Element(Ns + "Adres")!.Element(Ns + "KodKraju")!.Value);
    }

    [Fact]
    public void Foreign_InPln_HasNoRateOnLine()
    {
        var root = WriteValid(ForeignInvoice("GB", null, VatRate.NotTaxable, "PLN", null));

        Assert.Equal("PLN", Fa(root, "KodWaluty"));
        Assert.Null(FirstLine(root).Element(Ns + "KursWaluty"));
    }

    [Fact]
    public void Foreign_MainAccount_WrittenAsPlNrbWithSwiftAndBankName()
    {
        var root = WriteValid(ForeignInvoice("GB", null, VatRate.NotTaxable, configureProfile: p => p.SetBankDetails("TESTPLPW", "Bank Testowy S.A.", null, null, null)));

        Assert.Equal(["NrRB", "SWIFT", "NazwaBanku"], ChildNames(BankAccount(root)));
        Assert.Equal("PL" + Nrb, BankAccount(root).Element(Ns + "NrRB")!.Value);
        Assert.Equal("TESTPLPW", BankAccount(root).Element(Ns + "SWIFT")!.Value);
        Assert.Equal("Bank Testowy S.A.", BankAccount(root).Element(Ns + "NazwaBanku")!.Value);
    }

    [Fact]
    public void Foreign_ForeignAccountInProfile_IsUsedWithItsSwift()
    {
        var root = WriteValid(ForeignInvoice("GB", null, VatRate.NotTaxable, configureProfile: FullBankDetails));

        Assert.Equal(MadeUpIban, BankAccount(root).Element(Ns + "NrRB")!.Value);
        Assert.Equal("TESTGB2L", BankAccount(root).Element(Ns + "SWIFT")!.Value);
        Assert.Equal("West Test Bank", BankAccount(root).Element(Ns + "NazwaBanku")!.Value);
    }

    [Fact]
    public void Foreign_SplitPaymentRequest_StaysOff()
    {
        var invoice = Issued(i =>
        {
            i.SetBuyer(BuyerKind.Foreign, null, "Example Buyer Ltd", "1 Test Street", null);
            i.SetForeignBuyer("GB", null);
            i.AddLine("Consulting", "h", 1, 50000m, VatRate.NotTaxable);
            i.SetPayment(PaymentMethod.Transfer, splitPayment: true);
        });

        Assert.Equal("2", Adnotacja(WriteValid(invoice), "P_18A"));
    }

    [Fact]
    public void Foreign_MatchesFa3ShapeOfAnInvoiceFromAnotherTool()
    {
        var root = WriteValid(ForeignInvoice("GB", null, VatRate.NotTaxable, configureProfile: p => p.SetBankDetails("TESTPLPW", "Bank Testowy S.A.", null, null, null)));

        Assert.Equal(["DaneIdentyfikacyjne", "Adres", "JST", "GV"], ChildNames(Buyer(root)));
        Assert.Equal(["BrakID", "Nazwa"], ChildNames(BuyerIds(root)));
        Assert.Equal(["KodKraju", "AdresL1", "AdresL2"], ChildNames(Buyer(root).Element(Ns + "Adres")!));
        Assert.Equal(["KodWaluty", "P_1", "P_2", "P_13_8", "P_15", "Adnotacje", "RodzajFaktury", "FaWiersz", "Platnosc"], ChildNames(root.Element(Ns + "Fa")!));
        Assert.Equal(["NrWierszaFa", "P_7", "P_8A", "P_8B", "P_9A", "P_11", "P_12", "KursWaluty"], ChildNames(FirstLine(root)));
        Assert.Equal(["TerminPlatnosci", "FormaPlatnosci", "RachunekBankowy"], ChildNames(root.Element(Ns + "Fa")!.Element(Ns + "Platnosc")!));
    }

    [Fact]
    public void Foreign_UnknownCountry_FailsSchemaValidation()
    {
        var result = Writer.Write(ForeignInvoice("QQ", null, VatRate.NotTaxable), Now);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void EuCountries_MatchTheXsdListWithoutPlAndXi()
    {
        var schema = typeof(Fa3InvoiceWriter).Assembly.GetManifestResourceStream("OpenKSeF.Sync.Issuing.Schemas.schemat_FA(3)_v1-0E.xsd")!;
        XNamespace xsd = "http://www.w3.org/2001/XMLSchema";
        var codes = XDocument.Load(schema).Root!.Elements(xsd + "simpleType")
            .Single(t => t.Attribute("name")!.Value == "TKodyKrajowUE")
            .Descendants(xsd + "enumeration").Select(e => e.Attribute("value")!.Value)
            .Where(c => c is not "PL" and not "XI")
            .ToList();

        Assert.Equal(26, codes.Count);
        Assert.All(codes, c => Assert.True(EuCountries.Contains(EuCountries.FromVatPrefix(c)), c));
        Assert.False(EuCountries.Contains("XI"));
        Assert.False(EuCountries.Contains("PL"));
        Assert.False(EuCountries.Contains("GB"));
    }

    private const string CompanyGolden = """
        <?xml version="1.0" encoding="utf-8"?>
        <Faktura xmlns="http://crd.gov.pl/wzor/2025/06/25/13775/">
          <Naglowek>
            <KodFormularza kodSystemowy="FA (3)" wersjaSchemy="1-0E">FA</KodFormularza>
            <WariantFormularza>3</WariantFormularza>
            <DataWytworzeniaFa>2026-11-03T10:00:00Z</DataWytworzeniaFa>
            <SystemInfo>OpenKSeF</SystemInfo>
          </Naglowek>
          <Podmiot1>
            <DaneIdentyfikacyjne>
              <NIP>5260250274</NIP>
              <Nazwa>Sprzedawca sp. z o.o.</Nazwa>
            </DaneIdentyfikacyjne>
            <Adres>
              <KodKraju>PL</KodKraju>
              <AdresL1>ul. Prosta 1</AdresL1>
              <AdresL2>00-001 Warszawa</AdresL2>
            </Adres>
          </Podmiot1>
          <Podmiot2>
            <DaneIdentyfikacyjne>
              <NIP>5261040828</NIP>
              <Nazwa>Nabywca sp. z o.o.</Nazwa>
            </DaneIdentyfikacyjne>
            <Adres>
              <KodKraju>PL</KodKraju>
              <AdresL1>ul. Długa 2</AdresL1>
              <AdresL2>00-002 Warszawa</AdresL2>
            </Adres>
            <JST>2</JST>
            <GV>2</GV>
          </Podmiot2>
          <Fa>
            <KodWaluty>PLN</KodWaluty>
            <P_1>2026-11-03</P_1>
            <P_2>FV/1/11/2026</P_2>
            <P_6>2026-10-29</P_6>
            <P_13_1>1500.00</P_13_1>
            <P_14_1>345.00</P_14_1>
            <P_13_2>50.00</P_13_2>
            <P_14_2>4.00</P_14_2>
            <P_13_3>39.98</P_13_3>
            <P_14_3>2.00</P_14_3>
            <P_13_6_1>500.00</P_13_6_1>
            <P_13_7>800.00</P_13_7>
            <P_15>3240.98</P_15>
            <Adnotacje>
              <P_16>1</P_16>
              <P_17>2</P_17>
              <P_18>2</P_18>
              <P_18A>1</P_18A>
              <Zwolnienie>
                <P_19>1</P_19>
                <P_19A>art. 113 ust. 1 ustawy o VAT</P_19A>
              </Zwolnienie>
              <NoweSrodkiTransportu>
                <P_22N>1</P_22N>
              </NoweSrodkiTransportu>
              <P_23>2</P_23>
              <PMarzy>
                <P_PMarzyN>1</P_PMarzyN>
              </PMarzy>
            </Adnotacje>
            <RodzajFaktury>VAT</RodzajFaktury>
            <FaWiersz>
              <NrWierszaFa>1</NrWierszaFa>
              <P_7>Usługa</P_7>
              <P_8A>godz.</P_8A>
              <P_8B>10</P_8B>
              <P_9A>150.00</P_9A>
              <P_11>1500.00</P_11>
              <P_12>23</P_12>
            </FaWiersz>
            <FaWiersz>
              <NrWierszaFa>2</NrWierszaFa>
              <P_7>Towar</P_7>
              <P_8A>szt.</P_8A>
              <P_8B>1.5</P_8B>
              <P_9A>33.33</P_9A>
              <P_11>50.00</P_11>
              <P_12>8</P_12>
            </FaWiersz>
            <FaWiersz>
              <NrWierszaFa>3</NrWierszaFa>
              <P_7>Książka</P_7>
              <P_8A>szt.</P_8A>
              <P_8B>2</P_8B>
              <P_9A>19.99</P_9A>
              <P_11>39.98</P_11>
              <P_12>5</P_12>
            </FaWiersz>
            <FaWiersz>
              <NrWierszaFa>4</NrWierszaFa>
              <P_7>Eksport krajowy</P_7>
              <P_8A>szt.</P_8A>
              <P_8B>1</P_8B>
              <P_9A>500.00</P_9A>
              <P_11>500.00</P_11>
              <P_12>0 KR</P_12>
            </FaWiersz>
            <FaWiersz>
              <NrWierszaFa>5</NrWierszaFa>
              <P_7>Szkolenie</P_7>
              <P_8A>szt.</P_8A>
              <P_8B>1</P_8B>
              <P_9A>800.00</P_9A>
              <P_11>800.00</P_11>
              <P_12>zw</P_12>
            </FaWiersz>
            <Platnosc>
              <TerminPlatnosci>
                <Termin>2026-11-17</Termin>
              </TerminPlatnosci>
              <FormaPlatnosci>6</FormaPlatnosci>
              <RachunekBankowy>
                <NrRB>61109010140000071219812874</NrRB>
              </RachunekBankowy>
            </Platnosc>
          </Fa>
        </Faktura>
        """;

    private const string PersonGolden = """
        <?xml version="1.0" encoding="utf-8"?>
        <Faktura xmlns="http://crd.gov.pl/wzor/2025/06/25/13775/">
          <Naglowek>
            <KodFormularza kodSystemowy="FA (3)" wersjaSchemy="1-0E">FA</KodFormularza>
            <WariantFormularza>3</WariantFormularza>
            <DataWytworzeniaFa>2026-11-03T10:00:00Z</DataWytworzeniaFa>
            <SystemInfo>OpenKSeF</SystemInfo>
          </Naglowek>
          <Podmiot1>
            <DaneIdentyfikacyjne>
              <NIP>5260250274</NIP>
              <Nazwa>Sprzedawca sp. z o.o.</Nazwa>
            </DaneIdentyfikacyjne>
            <Adres>
              <KodKraju>PL</KodKraju>
              <AdresL1>ul. Prosta 1</AdresL1>
              <AdresL2>00-001 Warszawa</AdresL2>
            </Adres>
          </Podmiot1>
          <Podmiot2>
            <DaneIdentyfikacyjne>
              <BrakID>1</BrakID>
              <Nazwa>Jan Kowalski</Nazwa>
            </DaneIdentyfikacyjne>
            <Adres>
              <KodKraju>PL</KodKraju>
              <AdresL1>ul. Krótka 3</AdresL1>
            </Adres>
            <JST>2</JST>
            <GV>2</GV>
          </Podmiot2>
          <Fa>
            <KodWaluty>PLN</KodWaluty>
            <P_1>2026-11-03</P_1>
            <P_2>FV/1/11/2026</P_2>
            <P_13_1>100.00</P_13_1>
            <P_14_1>23.00</P_14_1>
            <P_15>123.00</P_15>
            <Adnotacje>
              <P_16>2</P_16>
              <P_17>2</P_17>
              <P_18>2</P_18>
              <P_18A>2</P_18A>
              <Zwolnienie>
                <P_19N>1</P_19N>
              </Zwolnienie>
              <NoweSrodkiTransportu>
                <P_22N>1</P_22N>
              </NoweSrodkiTransportu>
              <P_23>2</P_23>
              <PMarzy>
                <P_PMarzyN>1</P_PMarzyN>
              </PMarzy>
            </Adnotacje>
            <RodzajFaktury>VAT</RodzajFaktury>
            <FaWiersz>
              <NrWierszaFa>1</NrWierszaFa>
              <P_7>Usługa</P_7>
              <P_8A>szt.</P_8A>
              <P_8B>1</P_8B>
              <P_9A>100.00</P_9A>
              <P_11>100.00</P_11>
              <P_12>23</P_12>
            </FaWiersz>
            <Platnosc>
              <TerminPlatnosci>
                <Termin>2026-11-17</Termin>
              </TerminPlatnosci>
              <FormaPlatnosci>1</FormaPlatnosci>
            </Platnosc>
          </Fa>
        </Faktura>
        """;
}
