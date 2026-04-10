using System.Xml;
using System.Xml.Schema;
using OpenKSeF.Invoices.Domain.Integration;

namespace OpenKSeF.Invoices.Infrastructure.Validation;

/// <summary>
/// Validates KSeF invoice XML payloads.
/// When an XSD schema file path is provided the payload is validated against the
/// official FA(2) schema; otherwise a well-formedness and mandatory-element check
/// is performed as a fallback.
/// </summary>
public sealed class KsefXmlSchemaValidator : IKsefXmlSchemaValidator
{
    private static readonly string KsefNamespace = "http://crd.gov.pl/wzor/2023/06/29/12648/";

    private readonly string? _xsdPath;

    /// <param name="xsdPath">
    /// Optional path to the FA(2) XSD file (schemat_FA(2)_v1-0E.xsd).
    /// When null a structural well-formedness check is used instead.
    /// </param>
    public KsefXmlSchemaValidator(string? xsdPath = null)
    {
        _xsdPath = xsdPath;
    }

    public bool IsValid(string xml, out IReadOnlyList<string> errors)
    {
        var errorList = new List<string>();

        if (string.IsNullOrWhiteSpace(xml))
        {
            errorList.Add("Payload XML is empty.");
            errors = errorList;
            return false;
        }

        if (_xsdPath is not null && File.Exists(_xsdPath))
        {
            ValidateWithXsd(xml, _xsdPath, errorList);
        }
        else
        {
            ValidateStructurally(xml, errorList);
        }

        errors = errorList;
        return errorList.Count == 0;
    }

    private static void ValidateWithXsd(string xml, string xsdPath, List<string> errors)
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(KsefNamespace, xsdPath);

        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = schemas
        };
        settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            while (reader.Read()) { }
        }
        catch (XmlException ex)
        {
            errors.Add($"XML parse error: {ex.Message}");
        }
    }

    private static void ValidateStructurally(string xml, List<string> errors)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("fa", KsefNamespace);

            CheckRequired(doc, ns, "//fa:Podmiot1//fa:NIP", "Seller NIP (Podmiot1/NIP)", errors);
            CheckRequired(doc, ns, "//fa:Fa/fa:P_1", "Issue date (Fa/P_1)", errors);
            CheckRequired(doc, ns, "//fa:Fa/fa:P_2", "Document number (Fa/P_2)", errors);
            CheckRequired(doc, ns, "//fa:Fa/fa:RodzajFaktury", "Invoice type (Fa/RodzajFaktury)", errors);
        }
        catch (XmlException ex)
        {
            errors.Add($"XML parse error: {ex.Message}");
        }
    }

    private static void CheckRequired(XmlDocument doc, XmlNamespaceManager ns, string xpath, string label, List<string> errors)
    {
        var node = doc.SelectSingleNode(xpath, ns);
        if (node is null || string.IsNullOrWhiteSpace(node.InnerText))
            errors.Add($"Required field missing or empty: {label}");
    }
}
