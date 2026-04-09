namespace OpenKSeF.Invoices.Domain.Validation;

/// <summary>
/// Validation rules that guard state transitions (e.g. Draft → Approved, Approved → Submitted).
/// Evaluated by <see cref="Orchestrators.ApprovalValidationService"/> before committing a transition.
/// </summary>
public interface IStateTransitionRule<in T> : IValidationRule<T> { }
