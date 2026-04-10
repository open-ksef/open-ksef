using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Contracts.Commands;

public sealed record CreateCorrectionFromOriginalCommand(
    Guid TenantId,
    DateTime IssueDate,
    CorrectionReasonKind ReasonKind,
    string ReasonDescription);
