using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Policies;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;
using OpenKSeF.Invoices.Domain.Validation.Rules;
using OpenKSeF.Invoices.Domain.ValueObjects;
using InvoiceLine = OpenKSeF.Invoices.Domain.Entities.InvoiceLine;

namespace OpenKSeF.Invoices.Domain.Tests.ValidationTests;

public class NumberingValidationRuleTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void InvVal030_Approve_ReturnsError_WhenNumberMissingAndAssignedOnApproval()
    {
        var invoice = MakeInvoice(documentNumber: null);
        var service = CreateApprovalService();

        var result = service.Validate(
            invoice,
            MakeContext(
                ValidationStage.Approve,
                items: new Dictionary<string, object?> { ["NumberingPolicy"] = new AssignOnApprovalPolicy() }));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-030", message.Code);
        Assert.Equal("DocumentNumber", message.Path);
    }

    [Fact]
    public void InvVal030_Approve_ReturnsNoMessage_WhenNumberExistsAndAssignedOnApproval()
    {
        var invoice = MakeInvoice(new DocumentNumber("FV/2026/0001"));
        var service = CreateApprovalService();

        var result = service.Validate(
            invoice,
            MakeContext(
                ValidationStage.Approve,
                items: new Dictionary<string, object?> { ["NumberingPolicy"] = new AssignOnApprovalPolicy() }));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-030");
    }

    [Fact]
    public void InvVal031_Approve_ReturnsError_WhenDocumentNumberIsDuplicate()
    {
        var invoice = MakeInvoice(new DocumentNumber("FV/2026/0001"));
        var duplicatePolicy = new DuplicateForNumberPolicy("FV/2026/0001");
        var service = CreateApprovalService();

        var result = service.Validate(
            invoice,
            MakeContext(
                ValidationStage.Approve,
                items: new Dictionary<string, object?> { ["DocumentUniquenessPolicy"] = duplicatePolicy }));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-031", message.Code);
    }

    [Fact]
    public void InvVal031_Approve_ReturnsNoMessage_WhenDocumentNumberIsUnique()
    {
        var invoice = MakeInvoice(new DocumentNumber("FV/2026/0001"));
        var service = CreateApprovalService();

        var result = service.Validate(
            invoice,
            MakeContext(
                ValidationStage.Approve,
                items: new Dictionary<string, object?> { ["DocumentUniquenessPolicy"] = new DuplicateForNumberPolicy("OTHER") }));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-031");
    }

    [Fact]
    public void InvVal032_Draft_ReturnsWarning_WhenNumberDoesNotMatchConfiguredPattern()
    {
        var invoice = MakeInvoice(new DocumentNumber("BAD-001"));
        var service = CreateDraftService();

        var result = service.Validate(
            invoice,
            MakeContext(ValidationStage.Draft, numbering: new NumberingPolicy("FV/{YEAR}/{SEQ:0000}")));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-032", message.Code);
        Assert.Equal(ValidationSeverity.Warning, message.Severity);
    }

    [Fact]
    public void InvVal032_Draft_ReturnsNoMessage_WhenNumberMatchesConfiguredPattern()
    {
        var invoice = MakeInvoice(new DocumentNumber("FV/2026/0001"));
        var service = CreateDraftService();

        var result = service.Validate(
            invoice,
            MakeContext(ValidationStage.Draft, numbering: new NumberingPolicy("FV/{YEAR}/{SEQ:0000}")));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-032");
    }

    private static DraftValidationService CreateDraftService() =>
        new([new DocumentNumberPatternRule()], []);

    private static ApprovalValidationService CreateApprovalService() =>
        new(
            [
                new DocumentNumberRequiredOnApprovalRule(),
                new DocumentNumberMustBeUniqueRule()
            ],
            []);

    private static ValidationContext MakeContext(
        ValidationStage stage,
        NumberingPolicy? numbering = null,
        IReadOnlyDictionary<string, object?>? items = null) =>
        new(
            stage,
            Tenant,
            DateTime.UtcNow,
            new PolicySnapshotForTests(numbering ?? new NumberingPolicy()),
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: false,
            Items: items ?? new Dictionary<string, object?>());

    private static Invoice MakeInvoice(DocumentNumber? documentNumber)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.VatInvoice,
            Seller,
            Buyer,
            Pln,
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Required,
            documentNumber: documentNumber);

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

    private sealed class PolicySnapshotForTests(NumberingPolicy numbering) : IPolicySnapshot
    {
        public NumberingPolicy Numbering { get; } = numbering;
        public KsefPolicy Ksef { get; } = new();
        public VatPolicy Vat { get; } = new();
        public EditPolicy Edit { get; } = new();
        public ValidationPolicy Validation { get; } = new();
        public CurrencyPolicy Currency { get; } = new();
    }

    private sealed class AssignOnApprovalPolicy : INumberingPolicy
    {
        public bool AssignOnApproval => true;
        public DocumentNumber AssignNumber(Invoice invoice) => new("FV/2026/0001");
    }

    private sealed class DuplicateForNumberPolicy(string duplicateNumber) : IDocumentUniquenessPolicy
    {
        public bool IsDuplicate(TenantId tenantId, DocumentNumber number) => number.Value == duplicateNumber;
    }
}
