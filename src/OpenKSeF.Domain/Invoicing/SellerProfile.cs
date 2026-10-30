namespace OpenKSeF.Domain.Invoicing;

public sealed class SellerProfile
{
    public Guid TenantId { get; private set; }
    public string? LegalName { get; private set; }
    public string? AddressLine1 { get; private set; }
    public string? AddressLine2 { get; private set; }
    public string? BankAccount { get; private set; }
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

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
