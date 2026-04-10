namespace OpenKSeF.Invoices.Domain.Validation;

public sealed record ValidationResult(IReadOnlyList<ValidationMessage> Messages)
{
    public bool HasErrors => Messages.Any(m => m.Severity == ValidationSeverity.Error);

    public static ValidationResult Empty { get; } = new(Array.Empty<ValidationMessage>());
}
