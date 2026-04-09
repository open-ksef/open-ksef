using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Policies;

/// <summary>
/// Governs rules around correction document creation and what may be corrected.
/// </summary>
public interface ICorrectionPolicy
{
    /// <summary>Returns true if the given original invoice can be corrected.</summary>
    bool CanCorrect(Invoice original);
}
