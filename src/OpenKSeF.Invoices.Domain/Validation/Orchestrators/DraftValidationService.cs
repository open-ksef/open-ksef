using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;

namespace OpenKSeF.Invoices.Domain.Validation.Orchestrators;

/// <summary>
/// Runs all domain validation rules applicable to the <see cref="ValidationStage.Draft"/> stage.
/// Collects warnings and soft errors that guide the user without blocking editing.
/// </summary>
public sealed class DraftValidationService
{
    private readonly IEnumerable<IDomainValidationRule<Invoice>> _invoiceRules;
    private readonly IEnumerable<IDomainValidationRule<InvoiceLine>> _lineRules;

    public DraftValidationService(
        IEnumerable<IDomainValidationRule<Invoice>> invoiceRules,
        IEnumerable<IDomainValidationRule<InvoiceLine>> lineRules)
    {
        _invoiceRules = invoiceRules;
        _lineRules = lineRules;
    }

    public ValidationResult Validate(Invoice invoice, ValidationContext context)
    {
        var messages = new List<ValidationMessage>();

        foreach (var rule in _invoiceRules)
            if (rule.AppliesTo(context, invoice))
                messages.AddRange(rule.Validate(context, invoice));

        foreach (var line in invoice.LineItems)
            foreach (var rule in _lineRules)
                if (rule.AppliesTo(context, line))
                    messages.AddRange(rule.Validate(context, line));

        return new ValidationResult(messages);
    }
}
