using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Policies;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Tests.TestSupport;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;
using OpenKSeF.Invoices.Domain.Validation.Rules;
using OpenKSeF.Invoices.Domain.ValueObjects;
using InvoiceLine = OpenKSeF.Invoices.Domain.Entities.InvoiceLine;

namespace OpenKSeF.Invoices.Domain.Tests.ValidationTests;

public class DateValidationRuleTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void InvVal020_Approve_ReturnsError_WhenIssueDateIsMissing()
    {
        var invoice = MakeInvoice();
        invoice.SetIssueDates(default, saleDate: null, dueDate: null);
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-020", message.Code);
        Assert.Equal("IssueDate", message.Path);
    }

    [Fact]
    public void InvVal020_Approve_ReturnsNoMessage_WhenIssueDateIsPresent()
    {
        var invoice = MakeInvoice();
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-020");
    }

    [Fact]
    public void InvVal021_Draft_ReturnsWarning_WhenDueDateIsEarlierThanIssueDate()
    {
        var invoice = MakeInvoice();
        invoice.SetIssueDates(new DateTime(2026, 04, 10), dueDate: new DateTime(2026, 04, 09));
        var service = CreateDraftService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Draft));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-021", message.Code);
        Assert.Equal(ValidationSeverity.Warning, message.Severity);
    }

    [Fact]
    public void InvVal021_Draft_ReturnsNoMessage_WhenDueDateIsSameOrLaterThanIssueDate()
    {
        var invoice = MakeInvoice();
        invoice.SetIssueDates(new DateTime(2026, 04, 10), dueDate: new DateTime(2026, 04, 10));
        var service = CreateDraftService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Draft));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-021");
    }

    [Fact]
    public void InvVal022_Approve_ReturnsError_WhenSaleDateRequiredByPolicyAndMissing()
    {
        var invoice = MakeInvoice();
        invoice.SetIssueDates(new DateTime(2026, 04, 10), saleDate: null, dueDate: null);
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve, new ValidationPolicy(SaleDateRequired: true)));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-022", message.Code);
        Assert.Equal("SaleDate", message.Path);
    }

    [Fact]
    public void InvVal022_Approve_ReturnsNoMessage_WhenSaleDateRequiredByPolicyAndPresent()
    {
        var invoice = MakeInvoice();
        invoice.SetIssueDates(new DateTime(2026, 04, 10), saleDate: new DateTime(2026, 04, 10), dueDate: null);
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve, new ValidationPolicy(SaleDateRequired: true)));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-022");
    }

    [Fact]
    public void InvVal022_Approve_ReturnsNoMessage_WhenSalePeriodProvidedInContext()
    {
        var invoice = MakeInvoice();
        invoice.SetIssueDates(new DateTime(2026, 04, 10), saleDate: null, dueDate: null);
        var service = CreateApprovalService();

        var result = service.Validate(
            invoice,
            MakeContext(
                ValidationStage.Approve,
                new ValidationPolicy(SaleDateRequired: true),
                new Dictionary<string, object?> { ["SalePeriodStart"] = new DateOnly(2026, 04, 01) }));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-022");
    }

    private static DraftValidationService CreateDraftService() =>
        new(
            [
                new DueDateCannotBeEarlierThanIssueDateRule()
            ],
            []);

    private static ApprovalValidationService CreateApprovalService() =>
        new(
            [
                new IssueDateRequiredRule(),
                new SaleDateOrPeriodRequiredRule()
            ],
            []);

    private static ValidationContext MakeContext(
        ValidationStage stage,
        ValidationPolicy? validationPolicy = null,
        IReadOnlyDictionary<string, object?>? items = null) =>
        new(
            stage,
            Tenant,
            DateTime.UtcNow,
            new PolicySnapshotForTests(validationPolicy ?? new ValidationPolicy()),
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: false,
            Items: items ?? new Dictionary<string, object?>());

    private static Invoice MakeInvoice()
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.VatInvoice,
            Seller,
            Buyer,
            Pln,
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Required);

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Service",
            1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }

    private sealed class PolicySnapshotForTests(ValidationPolicy validation) : IPolicySnapshot
    {
        public NumberingPolicy Numbering { get; } = new();
        public KsefPolicy Ksef { get; } = new();
        public VatPolicy Vat { get; } = new();
        public EditPolicy Edit { get; } = new();
        public ValidationPolicy Validation { get; } = validation;
        public CurrencyPolicy Currency { get; } = new();
    }
}
