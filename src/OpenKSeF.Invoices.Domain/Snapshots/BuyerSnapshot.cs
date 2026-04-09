using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Snapshots;

/// <summary>
/// Immutable snapshot of buyer identity at document issuance time.
/// NIP may be absent for B2C/consumer buyers.
/// </summary>
public sealed record BuyerSnapshot(
    PartyName Name,
    BuyerKind Kind,
    Nip? Nip = null,
    PostalAddress? Address = null,
    string? Email = null);
