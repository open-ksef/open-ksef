namespace OpenKSeF.Invoices.Domain.Projection;

using OpenKSeF.Invoices.Domain.Aggregates;

/// <summary>
/// Marker interface that identifies invoice read-model projectors.
/// Concrete projection methods are defined on implementations for each target DTO type,
/// ensuring no controller accesses aggregate internals without going through a projector.
/// </summary>
public interface IInvoiceReadModelProjector<out TDto> where TDto : class
{
    TDto Project(Invoice invoice);
}
