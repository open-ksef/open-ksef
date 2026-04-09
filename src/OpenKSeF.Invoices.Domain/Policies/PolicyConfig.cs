namespace OpenKSeF.Invoices.Domain.Policies;

/// <summary>Numbering configuration: format template and series management.</summary>
public sealed record NumberingPolicy(string FormatTemplate = "FV/{YEAR}/{SEQ:0000}");

/// <summary>KSeF integration toggle and environment selection.</summary>
public sealed record KsefPolicy(bool IsEnabled = true, bool UseTestEnvironment = false);

/// <summary>VAT enforcement: strict (only allowed rates) or permissive.</summary>
public sealed record VatPolicy(bool StrictRateEnforcement = true);

/// <summary>Edit permissions: whether approved invoices can be reopened.</summary>
public sealed record EditPolicy(bool AllowReopenApproved = false);

/// <summary>Validation behavior flags: whether warnings block approval.</summary>
public sealed record ValidationPolicy(bool WarningsBlockApproval = false);

/// <summary>Currency restrictions: default and allowed currencies.</summary>
public sealed record CurrencyPolicy(string DefaultCurrency = "PLN");
