using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Sync.Issuing;

/// <summary>Writes FA(3) 1-0E XML for an issued invoice and validates the exact bytes against the embedded XSD set.</summary>
public sealed class Fa3InvoiceWriter : IFa3InvoiceWriter
{
    public const string Namespace = "http://crd.gov.pl/wzor/2025/06/25/13775/";
    private const string MainSchema = "schemat_FA(3)_v1-0E.xsd";
    private const string ResourcePrefix = "OpenKSeF.Sync.Issuing.Schemas.";

    private static readonly XNamespace Ns = Namespace;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] BucketOrder = ["1", "2", "3", "4", "5", "6_1", "6_2", "6_3", "7", "8", "9", "10"];

    private readonly XmlSchemaSet _schemas = LoadSchemas();
    private readonly Lock _validationLock = new();

    public Fa3WriteResult Write(IssuedInvoice invoice, DateTime generatedAtUtc)
    {
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), Faktura(invoice, generatedAtUtc));
        var xml = ToBytes(document);
        return new Fa3WriteResult(xml, Validate(xml));
    }

    private static XElement Faktura(IssuedInvoice invoice, DateTime generatedAtUtc) => new(Ns + "Faktura",
        new XElement(Ns + "Naglowek",
            new XElement(Ns + "KodFormularza", new XAttribute("kodSystemowy", "FA (3)"), new XAttribute("wersjaSchemy", "1-0E"), "FA"),
            new XElement(Ns + "WariantFormularza", 3),
            new XElement(Ns + "DataWytworzeniaFa", generatedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", Inv)),
            new XElement(Ns + "SystemInfo", "OpenKSeF")),
        new XElement(Ns + "Podmiot1",
            new XElement(Ns + "DaneIdentyfikacyjne",
                new XElement(Ns + "NIP", invoice.SellerNip),
                new XElement(Ns + "Nazwa", invoice.SellerName)),
            Address(invoice.SellerAddressLine1, invoice.SellerAddressLine2)),
        new XElement(Ns + "Podmiot2",
            new XElement(Ns + "DaneIdentyfikacyjne",
                invoice.BuyerKind == BuyerKind.Company
                    ? new XElement(Ns + "NIP", invoice.BuyerNip)
                    : new XElement(Ns + "BrakID", 1),
                new XElement(Ns + "Nazwa", invoice.BuyerName)),
            invoice.BuyerAddressLine1 is null ? null : Address(invoice.BuyerAddressLine1, invoice.BuyerAddressLine2),
            new XElement(Ns + "JST", 2),
            new XElement(Ns + "GV", 2)),
        Fa(invoice));

    private static XElement Address(string? line1, string? line2) => new(Ns + "Adres",
        new XElement(Ns + "KodKraju", "PL"),
        new XElement(Ns + "AdresL1", line1),
        line2 is null ? null : new XElement(Ns + "AdresL2", line2));

    private static XElement Fa(IssuedInvoice invoice) => new(Ns + "Fa",
        new XElement(Ns + "KodWaluty", IssuedInvoice.Currency),
        new XElement(Ns + "P_1", Date(invoice.IssueDate)),
        new XElement(Ns + "P_2", invoice.Number),
        invoice.SaleDate == invoice.IssueDate ? null : new XElement(Ns + "P_6", Date(invoice.SaleDate)),
        Buckets(invoice),
        new XElement(Ns + "P_15", Money(invoice.TotalGross)),
        Adnotacje(invoice),
        new XElement(Ns + "RodzajFaktury", "VAT"),
        invoice.Lines.Select(Line),
        Platnosc(invoice));

    private static IEnumerable<XElement> Buckets(IssuedInvoice invoice)
    {
        var byBucket = invoice.VatSummary()
            .GroupBy(s => s.VatRate.Bucket())
            .ToDictionary(g => g.Key, g => (Net: g.Sum(s => s.Net), Vat: g.Sum(s => s.Vat), HasVat: g.First().VatRate.HasVatAmount()));

        foreach (var bucket in BucketOrder)
        {
            if (!byBucket.TryGetValue(bucket, out var sums))
                continue;
            yield return new XElement(Ns + $"P_13_{bucket}", Money(sums.Net));
            if (sums.HasVat)
                yield return new XElement(Ns + $"P_14_{bucket}", Money(sums.Vat));
        }
    }

    private static XElement Adnotacje(IssuedInvoice invoice) => new(Ns + "Adnotacje",
        new XElement(Ns + "P_16", invoice.SellerCashAccounting ? 1 : 2),
        new XElement(Ns + "P_17", 2),
        new XElement(Ns + "P_18", 2),
        new XElement(Ns + "P_18A", invoice.SplitPayment ? 1 : 2),
        new XElement(Ns + "Zwolnienie", invoice.Lines.Any(l => l.VatRate == VatRate.Exempt)
            ? new object[] { new XElement(Ns + "P_19", 1), new XElement(Ns + ExemptionElement(invoice.ExemptionBasisKind), invoice.ExemptionBasisText) }
            : new XElement(Ns + "P_19N", 1)),
        new XElement(Ns + "NoweSrodkiTransportu", new XElement(Ns + "P_22N", 1)),
        new XElement(Ns + "P_23", 2),
        new XElement(Ns + "PMarzy", new XElement(Ns + "P_PMarzyN", 1)));

    private static string ExemptionElement(ExemptionBasisKind? kind) => kind switch
    {
        ExemptionBasisKind.Act => "P_19A",
        ExemptionBasisKind.Directive => "P_19B",
        _ => "P_19C"
    };

    private static XElement Line(IssuedInvoiceLine line) => new(Ns + "FaWiersz",
        new XElement(Ns + "NrWierszaFa", line.LineNo),
        new XElement(Ns + "P_7", line.Name),
        new XElement(Ns + "P_8A", line.Unit),
        new XElement(Ns + "P_8B", line.Quantity.ToString("0.######", Inv)),
        new XElement(Ns + "P_9A", Money(line.UnitNetPrice)),
        new XElement(Ns + "P_11", Money(line.NetAmount)),
        new XElement(Ns + "P_12", line.VatRate.Code()));

    private static XElement Platnosc(IssuedInvoice invoice) => new(Ns + "Platnosc",
        new XElement(Ns + "TerminPlatnosci", new XElement(Ns + "Termin", Date(invoice.DueDate))),
        new XElement(Ns + "FormaPlatnosci", invoice.PaymentMethod switch
        {
            PaymentMethod.Cash => 1,
            PaymentMethod.Card => 2,
            _ => 6
        }),
        invoice.SellerBankAccount is null ? null : new XElement(Ns + "RachunekBankowy", new XElement(Ns + "NrRB", invoice.SellerBankAccount)));

    private static string Money(decimal value) => value.ToString("0.00", Inv);

    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", Inv);

    private static byte[] ToBytes(XDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
            document.Save(writer);
        return stream.ToArray();
    }

    private IReadOnlyList<string> Validate(byte[] xml)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = _schemas,
            ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings | XmlSchemaValidationFlags.ProcessIdentityConstraints,
            XmlResolver = null,
            DtdProcessing = DtdProcessing.Prohibit
        };
        settings.ValidationEventHandler += (_, e) => errors.Add($"{e.Exception.LineNumber}:{e.Exception.LinePosition} {e.Message}");

        lock (_validationLock)
        {
            using var reader = XmlReader.Create(new MemoryStream(xml), settings);
            while (reader.Read()) { }
        }

        return errors;
    }

    private static XmlSchemaSet LoadSchemas()
    {
        var resolver = new EmbeddedSchemaResolver();
        var set = new XmlSchemaSet { XmlResolver = resolver };
        using var stream = EmbeddedSchemaResolver.Open(MainSchema);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { XmlResolver = resolver, DtdProcessing = DtdProcessing.Prohibit }, Namespace + MainSchema);
        set.Add(Namespace, reader);
        set.Compile();
        return set;
    }

    private sealed class EmbeddedSchemaResolver : XmlResolver
    {
        public static Stream Open(string fileName) =>
            typeof(Fa3InvoiceWriter).Assembly.GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new XmlSchemaException($"Schema '{fileName}' is not embedded; refusing to fetch it.");

        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn) =>
            Open(Path.GetFileName(Uri.UnescapeDataString(absoluteUri.AbsolutePath)));
    }
}
