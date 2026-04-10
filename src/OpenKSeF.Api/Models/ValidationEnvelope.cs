namespace OpenKSeF.Api.Models;

public sealed record ValidationEnvelope(
    string Stage,
    IReadOnlyList<ValidationEnvelopeMessage> Messages);

public sealed record ValidationEnvelopeMessage(
    string Code,
    string Severity,
    string? Field,
    string MessagePl,
    string MessageTechnical);
