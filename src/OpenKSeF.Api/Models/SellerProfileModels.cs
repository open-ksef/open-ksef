namespace OpenKSeF.Api.Models;

public record SellerProfileRequest(
    string? LegalName,
    string? AddressLine1,
    string? AddressLine2,
    string? BankAccount,
    bool CashAccounting,
    string? NumberingPattern,
    int? NextNumber = null,
    string? NextNumberPeriod = null);

public record SellerProfileResponse(
    string? LegalName,
    string? AddressLine1,
    string? AddressLine2,
    string? BankAccount,
    bool CashAccounting,
    string NumberingPattern,
    int NextNumber,
    string NextNumberPeriod,
    string Nip,
    bool IsComplete,
    string NextNumberPreview,
    string KsefEnvironment);
