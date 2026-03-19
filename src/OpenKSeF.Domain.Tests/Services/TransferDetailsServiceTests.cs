using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests.Services;

public class TransferDetailsServiceTests
{
    private readonly TransferDetailsService _sut = new();

    [Fact]
    public void BuildTransferData_Title_PrefersInvoiceNumber()
    {
        var invoice = MakeInvoice(invoiceNumber: "FV/2026/03/001");
        var result = _sut.BuildTransferData(invoice);
        Assert.Equal("Faktura FV/2026/03/001", result.Title);
    }

    [Fact]
    public void BuildTransferData_Title_FallsBackToKSeFNumber()
    {
        var invoice = MakeInvoice(invoiceNumber: null);
        var result = _sut.BuildTransferData(invoice);
        Assert.Equal("Faktura 9999999999-20260301-ABC123-FF", result.Title);
    }

    [Fact]
    public void BuildTransferData_RecipientAccount_FromVendorBankAccount()
    {
        var invoice = MakeInvoice(vendorBankAccount: "12345678901234567890123456");
        var result = _sut.BuildTransferData(invoice);
        Assert.Equal("12345678901234567890123456", result.RecipientAccount);
    }

    [Fact]
    public void BuildTransferData_RecipientAccount_NullWhenNoBankAccount()
    {
        var invoice = MakeInvoice(vendorBankAccount: null);
        var result = _sut.BuildTransferData(invoice);
        Assert.Null(result.RecipientAccount);
    }

    [Fact]
    public void BuildTransferText_IncludesBankAccountWhenPresent()
    {
        var invoice = MakeInvoice(vendorBankAccount: "12345678901234567890123456");
        var result = _sut.BuildTransferText(invoice);
        Assert.Contains("Nr rachunku: 12345678901234567890123456", result);
    }

    [Fact]
    public void BuildTransferText_OmitsBankAccountWhenNull()
    {
        var invoice = MakeInvoice(vendorBankAccount: null);
        var result = _sut.BuildTransferText(invoice);
        Assert.DoesNotContain("Nr rachunku", result);
    }

    [Fact]
    public void BuildTransferText_Title_UsesShortInvoiceNumber()
    {
        var invoice = MakeInvoice(invoiceNumber: "FV/2026/001");
        var result = _sut.BuildTransferText(invoice);
        Assert.Contains("Tytul: Faktura FV/2026/001", result);
    }

    private static InvoiceHeader MakeInvoice(
        string? invoiceNumber = "FV/2026/03/001",
        string? vendorBankAccount = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            KSeFInvoiceNumber = "9999999999-20260301-ABC123-FF",
            KSeFReferenceNumber = "ref-123",
            InvoiceNumber = invoiceNumber,
            VendorName = "Test Vendor Sp. z o.o.",
            VendorNip = "1234567890",
            AmountGross = 123.45m,
            Currency = "PLN",
            IssueDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            VendorBankAccount = vendorBankAccount
        };
}
