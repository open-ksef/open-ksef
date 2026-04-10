using System.Text.Json.Serialization;

namespace OpenKSeF.Invoices.Contracts.Dtos.Requests;

/// <summary>
/// Serializes enums as canonical PascalCase strings and rejects integer payloads.
/// This keeps request DTO JSON aligned with the UI specification.
/// </summary>
public sealed class InvoicePascalCaseEnumJsonConverter : JsonStringEnumConverter
{
    public InvoicePascalCaseEnumJsonConverter()
        : base(namingPolicy: null, allowIntegerValues: false)
    {
    }
}
