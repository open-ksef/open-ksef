using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Snapshots;

/// <summary>
/// Immutable snapshot of seller identity at document issuance time.
/// Carries all data needed for printing and KSeF submission.
/// </summary>
public sealed record SellerSnapshot(
    PartyName Name,
    Nip Nip,
    PostalAddress? Address = null,
    BankAccountNumber? BankAccount = null,
    string? Email = null);
