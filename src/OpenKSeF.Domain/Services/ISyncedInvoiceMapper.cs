using OpenKSeF.Domain.DTOs;
using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Domain.Services;

/// <summary>
/// Anti-corruption boundary between the synchronized read-side EF entities
/// (<see cref="SyncedInvoice"/>, <see cref="SyncedInvoiceLine"/>) and clean domain contracts.
///
/// All callers that need invoice data should depend on this interface rather than
/// referencing <see cref="SyncedInvoice"/> or <see cref="SyncedInvoiceLine"/> directly.
/// </summary>
public interface ISyncedInvoiceMapper
{
    /// <summary>
    /// Maps a <see cref="SyncedInvoice"/> to <see cref="InvoiceDto"/>.
    /// </summary>
    /// <param name="invoice">The synced EF entity.</param>
    /// <param name="includeLines">
    /// When <c>true</c> (default) the <see cref="InvoiceDto.Lines"/> collection is populated
    /// from <see cref="SyncedInvoice.Lines"/>. Pass <c>false</c> for list-view projections
    /// where lines have not been loaded.
    /// </param>
    InvoiceDto ToDto(SyncedInvoice invoice, bool includeLines = true);

    /// <summary>Maps a single <see cref="SyncedInvoiceLine"/> to <see cref="InvoiceLineDto"/>.</summary>
    InvoiceLineDto ToLineDto(SyncedInvoiceLine line);
}
