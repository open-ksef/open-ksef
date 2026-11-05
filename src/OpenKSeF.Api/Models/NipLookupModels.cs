namespace OpenKSeF.Api.Models;

public record NipLookupResponse(
    string Nip,
    string Name,
    string? AddressLine1,
    string? AddressLine2,
    string VatStatus,
    IReadOnlyList<string> BankAccounts);
