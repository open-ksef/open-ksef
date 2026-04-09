using OpenKSeF.Domain.DTOs;
using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Domain.Services;

/// <summary>
/// Anti-corruption boundary between the legacy synchronized read-side EF entities
/// (<see cref="InvoiceHeader"/>, <see cref="InvoiceLine"/>) and clean domain contracts.
///
/// All callers that need invoice data should depend on this interface rather than
/// referencing <see cref="InvoiceHeader"/> or <see cref="InvoiceLine"/> directly.
/// </summary>
#pragma warning disable CS0618 // referenced legacy types intentionally
public interface ISyncedInvoiceMapper
{
    /// <summary>
    /// Maps a <see cref="InvoiceHeader"/> to <see cref="InvoiceDto"/>.
    /// </summary>
    /// <param name="header">The legacy EF entity.</param>
    /// <param name="includeLines">
    /// When <c>true</c> (default) the <see cref="InvoiceDto.Lines"/> collection is populated
    /// from <see cref="InvoiceHeader.Lines"/>. Pass <c>false</c> for list-view projections
    /// where lines have not been loaded.
    /// </param>
    InvoiceDto ToDto(InvoiceHeader header, bool includeLines = true);

    /// <summary>Maps a single <see cref="InvoiceLine"/> to <see cref="InvoiceLineDto"/>.</summary>
    InvoiceLineDto ToLineDto(InvoiceLine line);
}
#pragma warning restore CS0618
