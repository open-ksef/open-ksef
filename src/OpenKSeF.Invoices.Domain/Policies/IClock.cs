namespace OpenKSeF.Invoices.Domain.Policies;

/// <summary>
/// Abstraction over the system clock.
/// Domain code must use this instead of <see cref="DateTime.UtcNow"/> directly
/// to allow deterministic time injection in tests.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
