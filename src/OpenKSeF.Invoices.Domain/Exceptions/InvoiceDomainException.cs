namespace OpenKSeF.Invoices.Domain.Exceptions;

/// <summary>Thrown when a domain invariant or state transition rule is violated.</summary>
public sealed class InvoiceDomainException : InvalidOperationException
{
    public string? RuleCode { get; }

    public InvoiceDomainException(string message, string? ruleCode = null)
        : base(message)
    {
        RuleCode = ruleCode;
    }
}
