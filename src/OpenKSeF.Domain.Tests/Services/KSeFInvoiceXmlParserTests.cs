using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKSeF.Domain.DTOs;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Sync;

namespace OpenKSeF.Domain.Tests.Services;

public class KSeFInvoiceXmlParserTests
{
    private readonly KSeFInvoiceXmlParser _sut = new(NullLogger<KSeFInvoiceXmlParser>.Instance);

    private const string Fa2Namespace = "http://crd.gov.pl/wzor/2023/06/29/12648/";
    private const string Fa3Namespace = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    private string? Account(string xml) => _sut.ExtractInvoiceDetails(Encoding.UTF8.GetBytes(xml)).BankAccount;

    private const string ValidNrb = "PL61 1090 1014 0000 0712 1981 2874";
    private const string FactorNrb = "94000000001234567890123456";

    private InvoiceXmlDetails Details(string ns, string fa, string extra = "") =>
        _sut.ExtractInvoiceDetails(Encoding.UTF8.GetBytes($"""<Faktura xmlns="{ns}"><Podmiot1/>{extra}<Fa>{fa}</Fa></Faktura>"""));

    [Fact]
    public void ExtractInvoiceDetails_ForeignIbanFirst_PicksFirstValidPlNrb()
    {
        var details = Details(Fa3Namespace, $"<Platnosc><RachunekBankowy><NrRB>DE12000000000000000001</NrRB></RachunekBankowy><RachunekBankowy><NrRB>{ValidNrb}</NrRB></RachunekBankowy></Platnosc>");

        Assert.Equal(ValidNrb, details.BankAccount);
    }

    [Fact]
    public void ExtractInvoiceDetails_Factor_FirstValidAccount_Podmiot3Rola1Only()
    {
        var details = Details(
            Fa3Namespace,
            $"<Platnosc><RachunekBankowy><NrRB>{ValidNrb}</NrRB></RachunekBankowy><RachunekBankowyFaktora><NrRB>PL11000000001234567890123456</NrRB></RachunekBankowyFaktora><RachunekBankowyFaktora><NrRB>{FactorNrb}</NrRB></RachunekBankowyFaktora></Platnosc>",
            "<Podmiot3><DaneIdentyfikacyjne><NIP>1234567890</NIP><Nazwa>Odbiorca Oddzial</Nazwa></DaneIdentyfikacyjne><Rola>2</Rola></Podmiot3>"
            + "<Podmiot3><DaneIdentyfikacyjne><NIP> 1111111111 </NIP><Nazwa>Faktor Testowy SA</Nazwa></DaneIdentyfikacyjne><Rola>1</Rola></Podmiot3>");

        Assert.Equal(ValidNrb, details.BankAccount);
        Assert.Equal(FactorNrb, details.FactorBankAccount);
        Assert.Equal("1111111111", details.FactorNip);
        Assert.Equal("Faktor Testowy SA", details.FactorName);
    }

    [Fact]
    public void ExtractInvoiceDetails_Podmiot3WithoutFactorAccount_NoFactor()
    {
        var details = Details(
            Fa3Namespace,
            $"<Platnosc><RachunekBankowy><NrRB>{ValidNrb}</NrRB></RachunekBankowy></Platnosc>",
            "<Podmiot3><DaneIdentyfikacyjne><NIP>1111111111</NIP><Nazwa>Faktor Testowy SA</Nazwa></DaneIdentyfikacyjne><Rola>1</Rola></Podmiot3>");

        Assert.Null(details.FactorBankAccount);
        Assert.Null(details.FactorNip);
        Assert.Null(details.FactorName);
    }

    [Fact]
    public void ExtractInvoiceDetails_PaymentFlags()
    {
        var details = Details(Fa3Namespace, "<P_15>1000.00</P_15><Adnotacje><P_16>2</P_16><P_17>2</P_17><P_18>2</P_18><P_18A>1</P_18A></Adnotacje><Platnosc><Zaplacono>1</Zaplacono><DataZaplaty>2026-03-05</DataZaplaty></Platnosc>");

        Assert.True(details.SplitPayment);
        Assert.Equal(new DateOnly(2026, 3, 5), details.SellerPaidOn);
        Assert.Null(details.PartialPayment);
        Assert.Equal(SettlementState.None, details.Settlement);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<DataZaplaty>05.03.2026</DataZaplaty>")]
    public void ExtractInvoiceDetails_PaidWithoutReadableDate_StaysPaid(string date)
    {
        Assert.Equal(DateOnly.MinValue, Details(Fa3Namespace, $"<Platnosc><Zaplacono>1</Zaplacono>{date}</Platnosc>").SellerPaidOn);
    }

    [Theory]
    [InlineData("2", false)]
    [InlineData("1", true)]
    public void ExtractInvoiceDetails_P18A(string value, bool expected)
    {
        Assert.Equal(expected, Details(Fa3Namespace, $"<Adnotacje><P_18A>{value}</P_18A></Adnotacje>").SplitPayment);
    }

    [Theory]
    [InlineData(Fa3Namespace, "2", (short)2)]
    [InlineData(Fa3Namespace, "1", (short)1)]
    [InlineData(Fa2Namespace, "1", (short)1)]
    public void ExtractInvoiceDetails_PartialPayment(string ns, string value, short expected)
    {
        var details = Details(ns, $"<Platnosc><ZnacznikZaplatyCzesciowej>{value}</ZnacznikZaplatyCzesciowej><ZaplataCzesciowa><KwotaZaplatyCzesciowej>100</KwotaZaplatyCzesciowej><DataZaplatyCzesciowej>2026-03-01</DataZaplatyCzesciowej></ZaplataCzesciowa></Platnosc>");

        Assert.Equal(expected, details.PartialPayment);
        Assert.Null(details.SellerPaidOn);
    }

    public static TheoryData<string, SettlementState, decimal?> RozliczenieCases => new()
    {
        { "<P_15>1000.00</P_15><Rozliczenie><SumaOdliczen>200.00</SumaOdliczen><DoZaplaty>800.00</DoZaplaty></Rozliczenie>", SettlementState.Confirmed, 800m },
        { "<P_15>1000.00</P_15><Rozliczenie><Obciazenia><Kwota>50.00</Kwota><Powod>Odsetki</Powod></Obciazenia><DoZaplaty>1050.00</DoZaplaty></Rozliczenie>", SettlementState.Confirmed, 1050m },
        { "<P_15>1000.00</P_15><Rozliczenie><Odliczenia><Kwota>100.00</Kwota><Powod>Zaliczka</Powod></Odliczenia><Odliczenia><Kwota>50.00</Kwota><Powod>Rabat</Powod></Odliczenia><DoZaplaty>850.00</DoZaplaty></Rozliczenie>", SettlementState.Confirmed, 850m },
        { "<P_15>1000.00</P_15><Rozliczenie><DoRozliczenia>300.00</DoRozliczenia></Rozliczenie>", SettlementState.Overpaid, 300m },
        { "<P_15>1000.00</P_15><Rozliczenie><SumaOdliczen>1000.00</SumaOdliczen><DoZaplaty>0.00</DoZaplaty></Rozliczenie>", SettlementState.NothingDue, 0m },
        { "<P_15>1000.00</P_15><Rozliczenie><DoZaplaty>-10.00</DoZaplaty></Rozliczenie>", SettlementState.NothingDue, -10m },
        { "<P_15>1000.00</P_15><Rozliczenie><DoZaplaty>800.00</DoZaplaty></Rozliczenie>", SettlementState.Unconfirmed, 800m },
        { "<P_15>1000.00</P_15><Rozliczenie><Odliczenia><Kwota>100.00</Kwota><Powod>Zaliczka</Powod></Odliczenia></Rozliczenie>", SettlementState.Unconfirmed, null },
        { "<P_15>1000.00</P_15><Rozliczenie/>", SettlementState.None, null },
        { "<P_15>1000.00</P_15>", SettlementState.None, null },
    };

    [Theory]
    [MemberData(nameof(RozliczenieCases))]
    public void ExtractInvoiceDetails_Rozliczenie_SameInFa2AndFa3(string fa, SettlementState state, decimal? amount)
    {
        var fa2 = Details(Fa2Namespace, fa);
        var fa3 = Details(Fa3Namespace, fa);

        Assert.Equal(state, fa3.Settlement);
        Assert.Equal(amount, fa3.SettlementAmount);
        Assert.Equal(fa3 with { Lines = null }, fa2 with { Lines = null });
    }

    [Fact]
    public void ExtractInvoiceDetails_SameContentInFa2AndFa3_IdenticalDetails()
    {
        var fa = $"<P_15>1000.00</P_15><Adnotacje><P_18A>1</P_18A></Adnotacje><Rozliczenie><SumaOdliczen>200.00</SumaOdliczen><DoZaplaty>800.00</DoZaplaty></Rozliczenie><Platnosc><Zaplacono>1</Zaplacono><DataZaplaty>2026-03-05</DataZaplaty><TerminPlatnosci><Termin>2026-03-15</Termin></TerminPlatnosci><RachunekBankowy><NrRB>{ValidNrb}</NrRB></RachunekBankowy><RachunekBankowyFaktora><NrRB>{FactorNrb}</NrRB></RachunekBankowyFaktora></Platnosc>";
        var podmiot3 = "<Podmiot3><DaneIdentyfikacyjne><NIP>1111111111</NIP><Nazwa>Faktor Testowy SA</Nazwa></DaneIdentyfikacyjne><Rola>1</Rola></Podmiot3>";

        var fa2 = Details(Fa2Namespace, fa, podmiot3);
        var fa3 = Details(Fa3Namespace, fa, podmiot3);

        Assert.Equal(fa3 with { Lines = null }, fa2 with { Lines = null });
        Assert.Equal(SettlementState.Confirmed, fa3.Settlement);
        Assert.Equal(FactorNrb, fa3.FactorBankAccount);
    }

    [Fact]
    public void ExtractBankAccount_Fa2_ReturnsBankAccount()
    {
        var xml = BuildInvoiceXml(Fa2Namespace, "12345678901234567890123456");
        var result = Account(xml);
        Assert.Equal("12345678901234567890123456", result);
    }

    [Fact]
    public void ExtractBankAccount_Fa3_ReturnsBankAccount()
    {
        var xml = BuildInvoiceXml(Fa3Namespace, "PL61109010140000071219812874");
        var result = Account(xml);
        Assert.Equal("PL61109010140000071219812874", result);
    }

    [Fact]
    public void ExtractBankAccount_NoPlatnosc_ReturnsNull()
    {
        var xml = BuildInvoiceXmlNoPlatnosc(Fa3Namespace);
        var result = Account(xml);
        Assert.Null(result);
    }

    [Fact]
    public void ExtractBankAccount_NoRachunekBankowy_ReturnsNull()
    {
        var xml = BuildInvoiceXmlNoRachunekBankowy(Fa3Namespace);
        var result = Account(xml);
        Assert.Null(result);
    }

    [Fact]
    public void ExtractBankAccount_MalformedXml_ReturnsNull()
    {
        var result = Account("<not-xml");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractBankAccount_MultipleInvalidAccounts_ReturnsFirst()
    {
        var xml = BuildInvoiceXmlMultipleAccounts(Fa3Namespace, "11111111111111111111111111", "22222222222222222222222222");
        var result = Account(xml);
        Assert.Equal("11111111111111111111111111", result);
    }

    [Fact]
    public void ExtractBankAccount_EmptyNrRB_ReturnsNull()
    {
        var xml = BuildInvoiceXml(Fa3Namespace, "   ");
        var result = Account(xml);
        Assert.Null(result);
    }

    private static string BuildInvoiceXml(string ns, string nrRB) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Faktura xmlns="{ns}">
          <Naglowek>
            <KodFormularza kodSystemowy="FA (3)" wersjaSchemy="1-0E">FA</KodFormularza>
          </Naglowek>
          <Fa>
            <KodWaluty>PLN</KodWaluty>
            <P_1>2026-03-01</P_1>
            <P_2>FV/2026/03/001</P_2>
            <Platnosc>
              <TerminPlatnosci>
                <Termin>2026-03-15</Termin>
              </TerminPlatnosci>
              <FormaPlatnosci>6</FormaPlatnosci>
              <RachunekBankowy>
                <NrRB>{nrRB}</NrRB>
              </RachunekBankowy>
            </Platnosc>
          </Fa>
        </Faktura>
        """;

    public static TheoryData<string, string, string?> DueDateCases => new()
    {
        { Fa2Namespace, "<TerminPlatnosci><Termin>2026-03-15</Termin></TerminPlatnosci>", "2026-03-15" },
        { Fa3Namespace, "<TerminPlatnosci><Termin>2026-03-15</Termin></TerminPlatnosci>", "2026-03-15" },
        { Fa2Namespace, "<TerminPlatnosci><Termin>2026-03-15</Termin><TerminOpis>30 dni</TerminOpis></TerminPlatnosci>", "2026-03-15" },
        { Fa3Namespace, "<TerminPlatnosci><Termin>2026-04-30</Termin></TerminPlatnosci><TerminPlatnosci><Termin>2026-03-15</Termin></TerminPlatnosci><TerminPlatnosci><Termin>2026-05-01</Termin></TerminPlatnosci>", "2026-03-15" },
        { Fa3Namespace, "<TerminPlatnosci><TerminOpis><Ilosc>30</Ilosc><Jednostka>dni</Jednostka><ZdarzeniePoczatkowe>doreczenie</ZdarzeniePoczatkowe></TerminOpis></TerminPlatnosci>", null },
        { Fa3Namespace, "<TerminPlatnosci><TerminOpis><Ilosc>30</Ilosc><Jednostka>dni</Jednostka><ZdarzeniePoczatkowe>doreczenie</ZdarzeniePoczatkowe></TerminOpis></TerminPlatnosci><TerminPlatnosci><Termin>2026-03-15</Termin></TerminPlatnosci>", "2026-03-15" },
        { Fa3Namespace, "<TerminPlatnosci/>", null },
        { Fa3Namespace, "<TerminPlatnosci><Termin>not-a-date</Termin></TerminPlatnosci>", null },
        { Fa3Namespace, "", null },
    };

    [Theory]
    [MemberData(nameof(DueDateCases))]
    public void ExtractInvoiceDetails_DueDate(string ns, string terms, string? expected)
    {
        var xml = $"""<Faktura xmlns="{ns}"><Fa><Platnosc>{terms}<FormaPlatnosci>6</FormaPlatnosci></Platnosc></Fa></Faktura>""";

        var dueDate = _sut.ExtractInvoiceDetails(Encoding.UTF8.GetBytes(xml)).DueDate;

        Assert.Equal(expected is null ? null : DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), dueDate);
    }

    [Fact]
    public void ExtractInvoiceDetails_NoPlatnoscOrMalformed_NoDueDate()
    {
        Assert.Null(_sut.ExtractInvoiceDetails(Encoding.UTF8.GetBytes(BuildInvoiceXmlNoPlatnosc(Fa3Namespace))).DueDate);
        Assert.Null(_sut.ExtractInvoiceDetails(Encoding.UTF8.GetBytes("<not-xml")).DueDate);
    }

    [Theory]
    [InlineData("<not-xml")]
    [InlineData("<Other>no namespace</Other>")]
    [InlineData("<Faktura xmlns=\"http://crd.gov.pl/wzor/2025/06/25/13775/\"/>")]
    public void ExtractInvoiceDetails_NotAnInvoice_ReturnsNullLines(string xml)
    {
        Assert.Null(_sut.ExtractInvoiceDetails(Encoding.UTF8.GetBytes(xml)).Lines);
    }

    [Fact]
    public void ExtractInvoiceDetails_InvoiceWithoutLines_ReturnsEmptyLines()
    {
        var xml = "<Faktura xmlns=\"http://crd.gov.pl/wzor/2025/06/25/13775/\"><Fa/></Faktura>";

        Assert.Empty(_sut.ExtractInvoiceDetails(Encoding.UTF8.GetBytes(xml)).Lines!);
    }

    private static string BuildInvoiceXmlNoPlatnosc(string ns) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Faktura xmlns="{ns}">
          <Fa>
            <KodWaluty>PLN</KodWaluty>
            <P_1>2026-03-01</P_1>
            <P_2>FV/2026/03/001</P_2>
          </Fa>
        </Faktura>
        """;

    private static string BuildInvoiceXmlNoRachunekBankowy(string ns) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Faktura xmlns="{ns}">
          <Fa>
            <KodWaluty>PLN</KodWaluty>
            <Platnosc>
              <FormaPlatnosci>6</FormaPlatnosci>
            </Platnosc>
          </Fa>
        </Faktura>
        """;

    private static string BuildInvoiceXmlMultipleAccounts(string ns, string first, string second) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Faktura xmlns="{ns}">
          <Fa>
            <Platnosc>
              <FormaPlatnosci>6</FormaPlatnosci>
              <RachunekBankowy>
                <NrRB>{first}</NrRB>
              </RachunekBankowy>
              <RachunekBankowy>
                <NrRB>{second}</NrRB>
              </RachunekBankowy>
            </Platnosc>
          </Fa>
        </Faktura>
        """;
}
