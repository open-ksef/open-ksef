using OpenKSeF.Invoices.Domain.Policies;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Validation;

/// <summary>
/// Carries all contextual information a validation rule needs to make its decision:
/// the current stage, tenant, timestamp, resolved policy snapshot, and any arbitrary metadata.
/// </summary>
public sealed record ValidationContext(
    ValidationStage Stage,
    TenantId TenantId,
    DateTime Now,
    IPolicySnapshot Policies,
    bool IsKsefSubmissionRequested,
    bool IsNumberAssigned,
    IReadOnlyDictionary<string, object?> Items);
