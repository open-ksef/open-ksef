using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Sync.Issuing;

namespace OpenKSeF.Domain.Tests.Issuing;

/// <summary>Byte equality of writer output with committed golden files; UPDATE_GOLDEN=1 rewrites them in the source tree.</summary>
public class Fa3GoldenTests
{
    private static readonly DateOnly Day = new(2026, 11, 3);
    private static readonly DateTime Now = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Fa3InvoiceWriter Writer = new();
    private const string MadeUpIban = "GB82WEST12345698765432";

    public static TheoryData<string> VatVariants => new(VatInvoices.Keys);

    private static readonly Dictionary<string, Func<IssuedInvoice>> VatInvoices = new()
    {
        ["vat-b2b-23"] = () => Fa3InvoiceWriterTests.Issued(i => i.ReplaceLines([("Usługa programistyczna", "godz.", 10, 150m, VatRate.Rate23)])),
        ["vat-person"] = () => Fa3InvoiceWriterTests.Issued(i =>
        {
            i.SetBuyer(BuyerKind.Person, null, "Jan Kowalski", "ul. Krótka 3", "00-003 Warszawa");
            i.ReplaceLines([("Usługa", null, 1, 100m, VatRate.Rate23)]);
            i.SetPayment(PaymentMethod.Cash, false);
        }, bankAccount: null),
        ["vat-multi-rate"] = () => Fa3InvoiceWriterTests.Issued(i => i.ReplaceLines([
            ("A", null, 2, 100.01m, VatRate.Rate23),
            ("B", null, 3, 33.33m, VatRate.Rate8),
            ("C", null, 1, 19.99m, VatRate.Rate5),
            ("D", null, 1, 500m, VatRate.ZeroDomestic)
        ])),
        ["vat-exempt-act"] = () => Fa3InvoiceWriterTests.Issued(i =>
        {
            i.ReplaceLines([("Szkolenie", null, 1, 800m, VatRate.Exempt)]);
            i.SetExemptionBasis(ExemptionBasisKind.Act, "art. 113 ust. 1 ustawy o VAT");
        }),
        ["vat-mpp"] = () => Fa3InvoiceWriterTests.Issued(i =>
        {
            i.ReplaceLines([("Usługa", null, 1, 20000m, VatRate.Rate23)]);
            i.SetPayment(PaymentMethod.Transfer, splitPayment: true);
        }),
        ["vat-cash-accounting"] = () => Fa3InvoiceWriterTests.Issued(i => i.ReplaceLines([("Usługa", null, 1, 100m, VatRate.Rate23)]), cashAccounting: true),
        ["vat-cash-payment"] = () => Fa3InvoiceWriterTests.Issued(i =>
        {
            i.ReplaceLines([("Usługa", null, 1, 100m, VatRate.Rate23)]);
            i.SetPayment(PaymentMethod.Cash, false);
        }),
        ["vat-card-payment"] = () => Fa3InvoiceWriterTests.Issued(i =>
        {
            i.ReplaceLines([("Usługa", null, 1, 100m, VatRate.Rate23)]);
            i.SetPayment(PaymentMethod.Card, false);
        }),
        ["vat-sale-date"] = () => Fa3InvoiceWriterTests.Issued(i =>
        {
            i.ReplaceLines([("Usługa", null, 1, 100m, VatRate.Rate23)]);
            i.SetDates(Day, Day.AddDays(-5), Day.AddDays(14));
        }),
        ["vat-full-bank-details"] = () => Fa3InvoiceWriterTests.Issued(i => i.ReplaceLines([("Usługa", null, 1, 100m, VatRate.Rate23)]),
            configureProfile: p => p.SetBankDetails("TESTPLPW", "Bank Testowy S.A.", MadeUpIban, "TESTGB2L", "West Test Bank")),
        ["vat-foreign-gbp-np1"] = () => Foreign("GB", null, VatRate.NotTaxable, "GBP", 5.2133m),
        ["vat-foreign-gbp-np1-taxid"] = () => Foreign("GB", "GB-TEST-000111", VatRate.NotTaxable, "GBP", 5.2133m),
        ["vat-foreign-eur-np2"] = () => Foreign("DE", "DE 123 456 789", VatRate.NotTaxableArt100, "EUR", 4.2m),
        ["vat-foreign-pln"] = () => Foreign("GB", null, VatRate.NotTaxable, "PLN", null),
        ["vat-foreign-full-bank-details"] = () => Foreign("GB", null, VatRate.NotTaxable, "GBP", 5.2133m,
            p => p.SetBankDetails("TESTPLPW", "Bank Testowy S.A.", MadeUpIban, "TESTGB2L", "West Test Bank")),
    };

    private static IssuedInvoice Foreign(string country, string? foreignId, VatRate rate, string currency, decimal? exchangeRate, Action<SellerProfile>? profile = null) =>
        Fa3InvoiceWriterTests.Issued(i =>
        {
            i.SetBuyer(BuyerKind.Foreign, null, "Example Buyer Ltd", "1 Test Street", "Testville");
            i.SetForeignBuyer(country, foreignId);
            i.SetCurrency(currency, exchangeRate, null);
            i.ReplaceLines([("Consulting / Konsultacje", "h", 10, 50m, rate)]);
        }, configureProfile: profile);

    [Theory]
    [MemberData(nameof(VatVariants))]
    public void Vat_XmlEqualsGoldenBytes(string name) => AssertGolden(name, Writer.Write(VatInvoices[name](), Now));

    internal static void AssertGolden(string name, Abstractions.Fa3WriteResult result)
    {
        Assert.Empty(result.SchemaErrors);
        var actual = LinuxNewLines(result.Xml);
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            File.WriteAllBytes(Path.Combine(SourceDir(), $"{name}.xml"), actual);
            return;
        }

        var expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Issuing", "Golden", $"{name}.xml"));
        Assert.True(expected.AsSpan().SequenceEqual(actual), $"{name}.xml differs from writer output:\n{System.Text.Encoding.UTF8.GetString(actual)}");
    }

    // XmlWriter indents with Environment.NewLine; production runs on Linux, so LF bytes are the reference.
    private static byte[] LinuxNewLines(byte[] xml) =>
        xml.Where((b, i) => !(b == (byte)'\r' && i + 1 < xml.Length && xml[i + 1] == (byte)'\n')).ToArray();

    private static string SourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "OpenKSeF.Domain.Tests.csproj")))
            dir = dir.Parent!;
        return Path.Combine(dir.FullName, "Issuing", "Golden");
    }
}

