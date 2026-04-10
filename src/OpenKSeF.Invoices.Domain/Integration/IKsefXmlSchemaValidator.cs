namespace OpenKSeF.Invoices.Domain.Integration;

/// <summary>
/// Validates a KSeF invoice XML payload against the FA(2)/FA(3) schema.
/// Implementations live in the infrastructure layer.
/// </summary>
public interface IKsefXmlSchemaValidator
{
    /// <summary>
    /// Returns true if <paramref name="xml"/> is schema-valid; false otherwise.
    /// </summary>
    bool IsValid(string xml, out IReadOnlyList<string> errors);
}
