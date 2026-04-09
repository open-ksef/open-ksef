namespace OpenKSeF.Invoices.Domain.Validation;

/// <summary>
/// Marker interface for technical KSeF payload validation rules.
/// Implementations validate the serialized payload before it is transmitted to KSeF.
/// </summary>
public interface IKsefTechnicalValidationRule<in T> : IValidationRule<T> { }
