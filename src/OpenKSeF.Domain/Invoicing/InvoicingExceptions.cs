namespace OpenKSeF.Domain.Invoicing;

public record InvoiceCheck(string Code, string? Field, string Message);

public sealed class InvoiceStateException(string message, string? code = null) : InvalidOperationException(message)
{
    public string? Code { get; } = code;
}

public sealed class InvoiceValidationException(IReadOnlyList<InvoiceCheck> errors)
    : Exception(string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}")))
{
    public IReadOnlyList<InvoiceCheck> Errors { get; } = errors;
}
