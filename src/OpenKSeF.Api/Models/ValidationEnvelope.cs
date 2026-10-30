namespace OpenKSeF.Api.Models;

public record ValidationIssue(string Code, string? Field, string Message);

public record ValidationEnvelope(
    IReadOnlyList<ValidationIssue> Errors,
    IReadOnlyList<ValidationIssue> Warnings);
