using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKSeF.Sync;

namespace OpenKSeF.Domain.Tests.Services;

public class KSeFInvoiceXmlParserTests
{
    private readonly KSeFInvoiceXmlParser _sut = new(NullLogger<KSeFInvoiceXmlParser>.Instance);

    private const string Fa2Namespace = "http://crd.gov.pl/wzor/2023/06/29/12648/";
    private const string Fa3Namespace = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    [Fact]
    public void ExtractBankAccount_Fa2_ReturnsBankAccount()
    {
        var xml = BuildInvoiceXml(Fa2Namespace, "12345678901234567890123456");
        var result = _sut.ExtractBankAccount(xml);
        Assert.Equal("12345678901234567890123456", result);
    }

    [Fact]
    public void ExtractBankAccount_Fa3_ReturnsBankAccount()
    {
        var xml = BuildInvoiceXml(Fa3Namespace, "PL61109010140000071219812874");
        var result = _sut.ExtractBankAccount(xml);
        Assert.Equal("PL61109010140000071219812874", result);
    }

    [Fact]
    public void ExtractBankAccount_NoPlatnosc_ReturnsNull()
    {
        var xml = BuildInvoiceXmlNoPlatnosc(Fa3Namespace);
        var result = _sut.ExtractBankAccount(xml);
        Assert.Null(result);
    }

    [Fact]
    public void ExtractBankAccount_NoRachunekBankowy_ReturnsNull()
    {
        var xml = BuildInvoiceXmlNoRachunekBankowy(Fa3Namespace);
        var result = _sut.ExtractBankAccount(xml);
        Assert.Null(result);
    }

    [Fact]
    public void ExtractBankAccount_MalformedXml_ReturnsNull()
    {
        var result = _sut.ExtractBankAccount("<not-xml");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractBankAccount_MultipleRachunekBankowy_ReturnsFirst()
    {
        var xml = BuildInvoiceXmlMultipleAccounts(Fa3Namespace, "11111111111111111111111111", "22222222222222222222222222");
        var result = _sut.ExtractBankAccount(xml);
        Assert.Equal("11111111111111111111111111", result);
    }

    [Fact]
    public void ExtractBankAccount_ByteArray_Works()
    {
        var xml = BuildInvoiceXml(Fa3Namespace, "12345678901234567890123456");
        var bytes = Encoding.UTF8.GetBytes(xml);
        var result = _sut.ExtractBankAccount(bytes);
        Assert.Equal("12345678901234567890123456", result);
    }

    [Fact]
    public void ExtractBankAccount_EmptyNrRB_ReturnsNull()
    {
        var xml = BuildInvoiceXml(Fa3Namespace, "   ");
        var result = _sut.ExtractBankAccount(xml);
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
