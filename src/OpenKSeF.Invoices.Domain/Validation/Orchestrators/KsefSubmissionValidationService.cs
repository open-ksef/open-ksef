using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Integration;

namespace OpenKSeF.Invoices.Domain.Validation.Orchestrators;

/// <summary>
/// Runs all domain rules for <see cref="ValidationStage.SendToKsef"/> and, when a payload is provided,
/// also runs the technical KSeF payload validation rules.
/// Any <see cref="ValidationSeverity.Error"/> blocks submission.
/// </summary>
public sealed class KsefSubmissionValidationService
{
    private readonly IEnumerable<IDomainValidationRule<Invoice>> _invoiceRules;
    private readonly IEnumerable<IDomainValidationRule<InvoiceLine>> _lineRules;
    private readonly IEnumerable<IKsefTechnicalValidationRule<KsefInvoicePayload>> _technicalRules;

    public KsefSubmissionValidationService(
        IEnumerable<IDomainValidationRule<Invoice>> invoiceRules,
        IEnumerable<IDomainValidationRule<InvoiceLine>> lineRules,
        IEnumerable<IKsefTechnicalValidationRule<KsefInvoicePayload>> technicalRules)
    {
        _invoiceRules = invoiceRules;
        _lineRules = lineRules;
        _technicalRules = technicalRules;
    }

    /// <summary>
    /// Validates the invoice and optionally the serialized KSeF payload.
    /// Pass <c>null</c> for <paramref name="payload"/> to skip technical validation.
    /// </summary>
    public ValidationResult Validate(Invoice invoice, KsefInvoicePayload? payload, ValidationContext context)
    {
        var messages = new List<ValidationMessage>();

        foreach (var rule in _invoiceRules)
            if (rule.AppliesTo(context, invoice))
                messages.AddRange(rule.Validate(context, invoice));

        foreach (var line in invoice.LineItems)
            foreach (var rule in _lineRules)
                if (rule.AppliesTo(context, line))
                    messages.AddRange(rule.Validate(context, line));

        if (payload is not null)
            foreach (var rule in _technicalRules)
                if (rule.AppliesTo(context, payload))
                    messages.AddRange(rule.Validate(context, payload));

        return new ValidationResult(messages);
    }
}
