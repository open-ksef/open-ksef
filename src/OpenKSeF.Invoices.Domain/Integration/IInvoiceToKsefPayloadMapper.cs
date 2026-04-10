namespace OpenKSeF.Invoices.Domain.Integration;

using OpenKSeF.Invoices.Domain.Aggregates;

/// <summary>
/// Maps a domain <see cref="Invoice"/> aggregate to a <see cref="KsefInvoicePayload"/>
/// suitable for transmission to the KSeF API.
/// Implementations live in the infrastructure layer; the domain depends only on this interface.
/// </summary>
public interface IInvoiceToKsefPayloadMapper
{
    /// <summary>
    /// Attempts to map the invoice to a KSeF payload.
    /// Returns <c>null</c> if mapping fails (e.g. required fields are missing).
    /// </summary>
    KsefInvoicePayload? TryMap(Invoice invoice);
}
