namespace OpenKSeF.Domain.Invoicing;

public sealed class SellerProfile
{
    public Guid TenantId { get; private set; }
    public string? LegalName { get; private set; }
    public string? AddressLine1 { get; private set; }
    public string? AddressLine2 { get; private set; }
    public string? BankAccount { get; private set; }
    public string? BankSwift { get; private set; }
    public string? BankName { get; private set; }
    public string? ForeignBankAccount { get; private set; }
    public string? ForeignBankSwift { get; private set; }
    public string? ForeignBankName { get; private set; }
    public bool CashAccounting { get; private set; }
    public string NumberingPattern { get; private set; } = InvoiceNumbering.DefaultPattern;

    public bool IsComplete => !string.IsNullOrWhiteSpace(LegalName) && !string.IsNullOrWhiteSpace(AddressLine1);

    private SellerProfile() { }

    public SellerProfile(Guid tenantId) => TenantId = tenantId;

    public void Update(string? legalName, string? addressLine1, string? addressLine2, string? bankAccount, bool cashAccounting, string numberingPattern)
    {
        LegalName = Clean(legalName);
        AddressLine1 = Clean(addressLine1);
        AddressLine2 = Clean(addressLine2);
        BankAccount = string.IsNullOrWhiteSpace(bankAccount) ? null : Nrb.Normalize(bankAccount);
        CashAccounting = cashAccounting;
        NumberingPattern = numberingPattern.Trim();
    }

    /// <summary>Null keeps the stored value, so older clients cannot wipe it; an empty string clears it.</summary>
    public void SetBankDetails(string? bankSwift, string? bankName, string? foreignBankAccount, string? foreignBankSwift, string? foreignBankName)
    {
        BankSwift = bankSwift is null ? BankSwift : Clean(bankSwift)?.ToUpperInvariant();
        BankName = bankName is null ? BankName : Clean(bankName);
        ForeignBankAccount = foreignBankAccount is null ? ForeignBankAccount : Clean(foreignBankAccount) is { } account ? Nrb.NormalizeIban(account) : null;
        ForeignBankSwift = foreignBankSwift is null ? ForeignBankSwift : Clean(foreignBankSwift)?.ToUpperInvariant();
        ForeignBankName = foreignBankName is null ? ForeignBankName : Clean(foreignBankName);
    }

    public SellerPaymentAccount PaymentAccountFor(BuyerKind kind)
    {
        if (kind != BuyerKind.Foreign)
            return new(BankAccount, null, null, null);

        return ForeignBankAccount is not null
            ? new(null, ForeignBankAccount, ForeignBankSwift, ForeignBankName)
            : new(BankAccount, BankAccount is null ? null : "PL" + BankAccount, BankSwift, BankName);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record SellerPaymentAccount(string? BankAccount, string? Iban, string? Swift, string? BankName);
