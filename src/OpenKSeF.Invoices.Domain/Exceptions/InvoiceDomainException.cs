using OpenKSeF.Invoices.Domain.Validation;

namespace OpenKSeF.Invoices.Domain.Exceptions;

/// <summary>Thrown when a domain invariant or state transition rule is violated.</summary>
public sealed class InvoiceDomainException : InvalidOperationException
{
    public string? RuleCode { get; }
    public ValidationStage? Stage { get; }
    public ValidationResult? ValidationResult { get; }

    public InvoiceDomainException(
        string message,
        string? ruleCode = null,
        ValidationStage? stage = null,
        ValidationResult? validationResult = null)
        : base(message)
    {
        RuleCode = ruleCode;
        Stage = stage;
        ValidationResult = validationResult;
    }
}
