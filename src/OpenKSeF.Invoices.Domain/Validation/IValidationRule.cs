namespace OpenKSeF.Invoices.Domain.Validation;

/// <summary>Base contract for all validation rules.</summary>
public interface IValidationRule<in T>
{
    /// <summary>Stable machine-readable rule code, e.g. "INV-VAL-001".</summary>
    string Code { get; }

    /// <summary>Returns true when this rule applies to the given target in the given context.</summary>
    bool AppliesTo(ValidationContext context, T target);

    /// <summary>Evaluates the rule and yields zero or more validation messages.</summary>
    IEnumerable<ValidationMessage> Validate(ValidationContext context, T target);
}
