namespace OpenKSeF.Api.Models;

public record SellerProfileRequest(
    string? LegalName,
    string? AddressLine1,
    string? AddressLine2,
    string? BankAccount,
    bool CashAccounting,
    string? NumberingPattern,
    int? NextNumber = null,
    string? NextNumberPeriod = null,
    string? BankSwift = null,
    string? BankName = null,
    string? ForeignBankAccount = null,
    string? ForeignBankSwift = null,
    string? ForeignBankName = null);

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
    string KsefEnvironment,
    string? BankSwift = null,
    string? BankName = null,
    string? ForeignBankAccount = null,
    string? ForeignBankSwift = null,
    string? ForeignBankName = null);
