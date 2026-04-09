namespace OpenKSeF.Invoices.Domain.Validation;

public sealed record ValidationMessage(
    string Code,
    ValidationSeverity Severity,
    ValidationStage Stage,
    string UserMessage,
    string TechnicalMessage,
    string? Path = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);
