using System.Text.Json;
using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Domain.Tests.Invoicing;

public class IssuedInvoiceTests
{
    private const string ValidBuyerNip = "5261040828";
    private const string ValidNrb = "61109010140000071219812874";
    private static readonly DateOnly Today = new(2026, 11, 3);
    private static readonly DateTime Now = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);

    private static IssuedInvoice NewDraft()
    {
        var invoice = IssuedInvoice.Draft(Guid.NewGuid(), Guid.NewGuid(), Today, Now);
        invoice.SetBuyer(BuyerKind.Company, ValidBuyerNip, "Nabywca sp. z o.o.", "ul. Długa 2", "00-002 Warszawa");
        return invoice;
    }

    private static IssuedInvoice ValidDraft()
    {
        var invoice = NewDraft();
        invoice.AddLine("Usługa", null, 1, 100m, VatRate.Rate23);
        return invoice;
    }

    private static SellerProfile CompleteProfile(string? bankAccount = ValidNrb)
    {
        var profile = new SellerProfile(Guid.NewGuid());
        profile.Update("OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", bankAccount, false, InvoiceNumbering.DefaultPattern);
        return profile;
    }

    private static IReadOnlyList<string> ErrorCodes(IssuedInvoice invoice, SellerProfile? profile) =>
        IssueChecks.Errors(invoice, profile).Select(e => e.Code).ToList();

    [Fact]
    public void Draft_HasDefaults()
    {
        var invoice = IssuedInvoice.Draft(Guid.NewGuid(), Guid.NewGuid(), Today, Now);

        Assert.Equal(IssuedInvoiceStatus.Draft, invoice.Status);
        Assert.Null(invoice.Number);
        Assert.Equal(Today, invoice.SaleDate);
        Assert.Equal(Today.AddDays(14), invoice.DueDate);
        Assert.Equal(PaymentMethod.Transfer, invoice.PaymentMethod);
        Assert.Equal(BuyerKind.Company, invoice.BuyerKind);
    }

    [Fact]
    public void AddLine_CalculatesNetAndDisplayVat_DefaultUnit()
    {
        var invoice = NewDraft();
        invoice.AddLine(" Usługa ", null, 3, 19.99m, VatRate.Rate8);

        var line = Assert.Single(invoice.Lines);
        Assert.Equal(1, line.LineNo);
        Assert.Equal("Usługa", line.Name);
        Assert.Equal("szt.", line.Unit);
        Assert.Equal(59.97m, line.NetAmount);
        Assert.Equal(4.80m, line.VatAmount);
    }

    [Fact]
    public void Totals_ThreeTimes033At23_UsesPerRateRounding()
    {
        var invoice = NewDraft();
        for (var i = 0; i < 3; i++)
            invoice.AddLine("x", null, 1, 0.33m, VatRate.Rate23);

        Assert.Equal(0.99m, invoice.TotalNet);
        Assert.Equal(0.23m, invoice.TotalVat);
        Assert.Equal(1.22m, invoice.TotalGross);
        Assert.Equal(0.24m, invoice.Lines.Sum(l => l.VatAmount));
    }

    public static IEnumerable<object[]> Vectors()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Invoicing", "TotalsVectors.json"));
        foreach (var vector in JsonDocument.Parse(json).RootElement.EnumerateArray())
            yield return [vector.GetProperty("name").GetString()!, vector.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Totals_MatchSharedVectors(string name, string vectorJson)
    {
        var vector = JsonDocument.Parse(vectorJson).RootElement;
        var invoice = NewDraft();
        invoice.ReplaceLines(vector.GetProperty("lines").EnumerateArray().Select(l => (
            (string?)"x", (string?)null, l.GetProperty("quantity").GetDecimal(), l.GetProperty("unitNetPrice").GetDecimal(),
            VatRates.Parse(l.GetProperty("vatRate").GetString()!))));

        var expectedLines = vector.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(expectedLines.Count, invoice.Lines.Count);
        for (var i = 0; i < expectedLines.Count; i++)
        {
            Assert.Equal(expectedLines[i].GetProperty("netAmount").GetDecimal(), invoice.Lines[i].NetAmount);
            Assert.Equal(expectedLines[i].GetProperty("vatAmount").GetDecimal(), invoice.Lines[i].VatAmount);
        }

        var expectedSummary = vector.GetProperty("vatSummary").EnumerateArray()
            .Select(s => (s.GetProperty("vatRate").GetString(), s.GetProperty("net").GetDecimal(), s.GetProperty("vat").GetDecimal()))
            .ToList();
        Assert.Equal(expectedSummary, invoice.VatSummary().Select(s => ((string?)s.VatRate.Code(), s.Net, s.Vat)).ToList());
        Assert.Equal(vector.GetProperty("totalNet").GetDecimal(), invoice.TotalNet);
        Assert.Equal(vector.GetProperty("totalVat").GetDecimal(), invoice.TotalVat);
        Assert.Equal(vector.GetProperty("totalGross").GetDecimal(), invoice.TotalGross);
        Assert.NotEmpty(name);
    }

    [Fact]
    public void ReplaceLines_RenumbersAndRecalculates()
    {
        var invoice = ValidDraft();
        invoice.ReplaceLines([("A", "h", 2, 50m, VatRate.Rate8), ("B", null, 1, 10m, VatRate.Exempt)]);

        Assert.Equal([1, 2], invoice.Lines.Select(l => l.LineNo));
        Assert.Equal(110m, invoice.TotalNet);
        Assert.Equal(8m, invoice.TotalVat);
    }

    [Fact]
    public void Issue_ValidDraft_SnapshotsSellerAssignsNumberAndQueues()
    {
        var invoice = ValidDraft();
        var profile = CompleteProfile();
        var issuedAt = Now.AddMinutes(5);

        invoice.Issue(profile, "1234563218", "FV/1/11/2026", issuedAt);

        Assert.Equal(IssuedInvoiceStatus.Queued, invoice.Status);
        Assert.Equal("FV/1/11/2026", invoice.Number);
        Assert.Equal("1234563218", invoice.SellerNip);
        Assert.Equal("OpenKSeF Test A sp. z o.o.", invoice.SellerName);
        Assert.Equal(ValidNrb, invoice.SellerBankAccount);
        Assert.Equal(issuedAt, invoice.IssuedAt);

        profile.Update("Changed", "Other 1", null, null, true, InvoiceNumbering.DefaultPattern);
        Assert.Equal("OpenKSeF Test A sp. z o.o.", invoice.SellerName);
    }

    [Fact]
    public void Issue_WithoutNumberForNewDraft_Throws()
    {
        var invoice = ValidDraft();

        Assert.Throws<InvoiceStateException>(() => invoice.Issue(CompleteProfile(), "1234563218", null, Now));
        Assert.Equal(IssuedInvoiceStatus.Draft, invoice.Status);
    }

    [Fact]
    public void Issue_InvalidDraft_ThrowsWithErrorsAndStaysDraft()
    {
        var invoice = NewDraft();

        var ex = Assert.Throws<InvoiceValidationException>(() => invoice.Issue(CompleteProfile(), "1234563218", "FV/1/11/2026", Now));

        Assert.Contains(ex.Errors, e => e.Code == "INV-VAL-002");
        Assert.Equal(IssuedInvoiceStatus.Draft, invoice.Status);
        Assert.Null(invoice.Number);
    }

    [Fact]
    public void Mutations_AfterIssue_Throw()
    {
        var invoice = ValidDraft();
        invoice.Issue(CompleteProfile(), "1234563218", "FV/1/11/2026", Now);

        Assert.Throws<InvoiceStateException>(() => invoice.AddLine("x", null, 1, 1m, VatRate.Rate23));
        Assert.Throws<InvoiceStateException>(() => invoice.ReplaceLines([]));
        Assert.Throws<InvoiceStateException>(() => invoice.SetBuyer(BuyerKind.Person, null, "a", "b", null));
        Assert.Throws<InvoiceStateException>(() => invoice.SetDates(Today, Today, Today));
        Assert.Throws<InvoiceStateException>(() => invoice.SetPayment(PaymentMethod.Cash, false));
        Assert.Throws<InvoiceStateException>(() => invoice.SetExemptionBasis(null, null));
        Assert.Throws<InvoiceStateException>(() => invoice.Issue(CompleteProfile(), "1234563218", "FV/2/11/2026", Now));
        Assert.Single(invoice.Lines);
        Assert.Equal("FV/1/11/2026", invoice.Number);
    }

    [Fact]
    public void ChecksForIssue_SellerProfile()
    {
        var invoice = ValidDraft();

        Assert.Equal(["INV-VAL-010", "INV-VAL-011", "INV-VAL-012"], ErrorCodes(invoice, null));
        Assert.Equal(["INV-VAL-012"], ErrorCodes(invoice, CompleteProfile(bankAccount: null)));

        invoice.SetPayment(PaymentMethod.Cash, false);
        Assert.Empty(ErrorCodes(invoice, CompleteProfile(bankAccount: null)));
    }

    [Fact]
    public void ChecksForIssue_CompanyBuyerNeedsValidNip()
    {
        var invoice = ValidDraft();
        invoice.SetBuyer(BuyerKind.Company, "1234567890", "B", "L1", null);

        var error = Assert.Single(IssueChecks.Errors(invoice, CompleteProfile()));
        Assert.Equal(("INV-VAL-013", "buyer.nip", "Dla nabywcy firmy wymagany jest poprawny NIP"), (error.Code, error.Field, error.Message));
        Assert.Contains(IssueChecks.Warnings(invoice), w => w.Code == "INV-W-NIP");

        invoice.SetBuyer(BuyerKind.Company, null, "B", "L1", null);
        Assert.Equal(["INV-VAL-013"], ErrorCodes(invoice, CompleteProfile()));
    }

    [Fact]
    public void ChecksForIssue_PersonBuyerNeedsNameAndAddressAndNoNip()
    {
        var invoice = ValidDraft();

        invoice.SetBuyer(BuyerKind.Person, null, " ", null, null);
        Assert.Equal(["INV-VAL-015", "INV-VAL-016"], ErrorCodes(invoice, CompleteProfile()));

        invoice.SetBuyer(BuyerKind.Person, ValidBuyerNip, "Jan Kowalski", "ul. Polna 3", "00-003 Warszawa");
        Assert.Equal(["INV-VAL-014"], ErrorCodes(invoice, CompleteProfile()));

        invoice.SetBuyer(BuyerKind.Person, null, "Jan Kowalski", "ul. Polna 3", "00-003 Warszawa");
        Assert.Empty(ErrorCodes(invoice, CompleteProfile()));
    }

    [Fact]
    public void ChecksForIssue_DueDateNotBeforeIssueDate()
    {
        var invoice = ValidDraft();

        invoice.SetDates(Today, Today, Today.AddDays(-1));
        Assert.Equal(["INV-VAL-021"], ErrorCodes(invoice, CompleteProfile()));

        invoice.SetDates(Today, Today, Today);
        Assert.Empty(ErrorCodes(invoice, CompleteProfile()));
    }

    [Fact]
    public void ChecksForIssue_LineRules()
    {
        var invoice = NewDraft();
        invoice.ReplaceLines([("", null, 0, -1m, VatRate.Rate22)]);

        Assert.Equal(["INV-VAL-050", "INV-VAL-051", "INV-VAL-052", "INV-VAL-060"], ErrorCodes(invoice, CompleteProfile()));
        Assert.Equal("lines[0].vatRate", IssueChecks.Errors(invoice, CompleteProfile()).Last().Field);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(ExemptionBasisKind.Act, null, true)]
    [InlineData(ExemptionBasisKind.Act, "  ", true)]
    [InlineData(ExemptionBasisKind.Act, "art. 113 ust. 1 ustawy o VAT", false)]
    [InlineData(ExemptionBasisKind.Directive, "art. 132 ust. 1 lit. i dyrektywy 2006/112/WE", false)]
    [InlineData(ExemptionBasisKind.Other, "inna podstawa", false)]
    public void ChecksForIssue_ExemptLineNeedsOneBasis(ExemptionBasisKind? kind, string? text, bool blocked)
    {
        var invoice = NewDraft();
        invoice.AddLine("Szkolenie", null, 1, 100m, VatRate.Exempt);
        invoice.SetExemptionBasis(kind, text);

        Assert.Equal(blocked, ErrorCodes(invoice, CompleteProfile()).Contains("INV-VAL-061"));
    }

    [Fact]
    public void ChecksForIssue_ExemptionBasisNotRequiredWithoutExemptLine()
    {
        Assert.Empty(ErrorCodes(ValidDraft(), CompleteProfile()));
    }

    [Theory]
    [InlineData(15000.00, false, false)]
    [InlineData(15000.01, false, true)]
    [InlineData(20000.00, true, false)]
    public void Warnings_SplitPaymentAboveThreshold(decimal gross, bool splitPayment, bool warned)
    {
        var invoice = NewDraft();
        invoice.AddLine("x", null, 1, gross, VatRate.ZeroDomestic);
        invoice.SetPayment(PaymentMethod.Transfer, splitPayment);

        Assert.Equal(warned, IssueChecks.Warnings(invoice).Any(w => w.Code == "INV-W-MPP"));
    }

    [Fact]
    public void RejectedInvoice_IsEditableAndKeepsNumberOnReissue()
    {
        var invoice = ValidDraft();
        invoice.Issue(CompleteProfile(), "1234563218", "FV/7/11/2026", Now);
        typeof(IssuedInvoice).GetProperty(nameof(IssuedInvoice.Status))!.SetValue(invoice, IssuedInvoiceStatus.Rejected);

        invoice.AddLine("Poprawka", null, 1, 1m, VatRate.Rate23);
        invoice.Issue(CompleteProfile(), "1234563218", "FV/99/11/2026", Now);

        Assert.Equal("FV/7/11/2026", invoice.Number);
        Assert.Equal(IssuedInvoiceStatus.Queued, invoice.Status);
    }
}
