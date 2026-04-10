namespace OpenKSeF.Invoices.Domain.ValueObjects;

public sealed record PostalAddress(
    string Street,
    string City,
    string PostalCode,
    string CountryCode = "PL");
