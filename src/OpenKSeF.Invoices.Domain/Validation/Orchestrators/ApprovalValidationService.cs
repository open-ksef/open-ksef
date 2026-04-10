using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;

namespace OpenKSeF.Invoices.Domain.Validation.Orchestrators;

/// <summary>
/// Runs all domain and state-transition rules for the <see cref="ValidationStage.Approve"/> stage.
/// Any <see cref="ValidationSeverity.Error"/> result blocks the transition to Approved.
/// </summary>
public sealed class ApprovalValidationService
{
    private readonly IEnumerable<IDomainValidationRule<Invoice>> _invoiceRules;
    private readonly IEnumerable<IDomainValidationRule<InvoiceLine>> _lineRules;
    private readonly IEnumerable<IStateTransitionRule<Invoice>> _transitionRules;

    public ApprovalValidationService(
        IEnumerable<IDomainValidationRule<Invoice>> invoiceRules,
        IEnumerable<IDomainValidationRule<InvoiceLine>> lineRules,
        IEnumerable<IStateTransitionRule<Invoice>>? transitionRules = null)
    {
        _invoiceRules = invoiceRules;
        _lineRules = lineRules;
        _transitionRules = transitionRules ?? [];
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

        foreach (var rule in _transitionRules)
            if (rule.AppliesTo(context, invoice))
                messages.AddRange(rule.Validate(context, invoice));

        return new ValidationResult(messages);
    }
}
