namespace OpenKSeF.Invoices.Domain.Validation;

/// <summary>
/// Marker interface for domain validation rules targeting Invoice aggregate or InvoiceLine entity.
/// Implementations belong in the domain layer and must not reference infrastructure.
/// </summary>
public interface IDomainValidationRule<in T> : IValidationRule<T> { }
