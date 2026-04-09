using OpenKSeF.Invoices.Domain.Tests.TestSupport;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Integration;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;
using OpenKSeF.Invoices.Domain.ValueObjects;
using InvoiceLine = OpenKSeF.Invoices.Domain.Entities.InvoiceLine;

namespace OpenKSeF.Invoices.Domain.Tests.ValidationTests;

public class ValidationPipelineTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    private static Invoice MakeVatInvoice()
    {
        var inv = Invoice.Draft(InvoiceId.New(), Tenant, DocumentKind.VatInvoice,
            Seller, Buyer, Pln, DateTime.UtcNow, KsefSubmissionRequirement.Required);
        inv.AddLine(InvoiceLine.Create(1, "Service", 1m,
            new Money(100m, Pln), PricingMode.Net, VatRate.OfPercentage(new Percentage(23))));
        inv.RecalculateTotals();
        return inv;
    }

    private static ValidationContext MakeContext(ValidationStage stage) =>
        new(stage, Tenant, DateTime.UtcNow, TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: false, IsNumberAssigned: false,
            Items: new Dictionary<string, object?>());

    // ── DraftValidationService ────────────────────────────────────────────────

    [Fact]
    public void DraftValidation_AlwaysApplicableRule_IsInvoked()
    {
        var rule = new AlwaysErrorInvoiceRule("INV-TEST-001", ValidationStage.Draft);
        var svc = new DraftValidationService([rule], []);
        var ctx = MakeContext(ValidationStage.Draft);

        var result = svc.Validate(MakeVatInvoice(), ctx);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Messages, m => m.Code == "INV-TEST-001");
    }

    [Fact]
    public void DraftValidation_NeverApplicableRule_IsSkipped()
    {
        var rule = new NeverAppliesInvoiceRule("INV-TEST-002");
        var svc = new DraftValidationService([rule], []);
        var ctx = MakeContext(ValidationStage.Draft);

        var result = svc.Validate(MakeVatInvoice(), ctx);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Messages);
    }

    [Fact]
    public void DraftValidation_LineRule_IsAppliedToEachLine()
    {
        var lineRule = new AlwaysErrorLineRule("INV-TEST-003", ValidationStage.Draft);
        var svc = new DraftValidationService([], [lineRule]);
        var ctx = MakeContext(ValidationStage.Draft);

        var inv = MakeVatInvoice();
        inv.AddLine(InvoiceLine.Create(2, "Service 2", 1m,
            new Money(50m, Pln), PricingMode.Net, VatRate.OfPercentage(new Percentage(8))));
        inv.RecalculateTotals();

        var result = svc.Validate(inv, ctx);

        // 2 lines → 2 error messages from the line rule
        Assert.Equal(2, result.Messages.Count(m => m.Code == "INV-TEST-003"));
    }

    [Fact]
    public void DraftValidation_MultipleRules_AllMessagesCollected()
    {
        var rule1 = new AlwaysErrorInvoiceRule("INV-TEST-004", ValidationStage.Draft);
        var rule2 = new AlwaysWarningInvoiceRule("INV-TEST-005", ValidationStage.Draft);
        var svc = new DraftValidationService([rule1, rule2], []);
        var ctx = MakeContext(ValidationStage.Draft);

        var result = svc.Validate(MakeVatInvoice(), ctx);

        Assert.Equal(2, result.Messages.Count);
        Assert.True(result.HasErrors); // rule1 emits an error
    }

    // ── ApprovalValidationService ─────────────────────────────────────────────

    [Fact]
    public void ApprovalValidation_Rule_IsInvoked()
    {
        var rule = new AlwaysErrorInvoiceRule("INV-TEST-006", ValidationStage.Approve);
        var svc = new ApprovalValidationService([rule], []);
        var ctx = MakeContext(ValidationStage.Approve);

        var result = svc.Validate(MakeVatInvoice(), ctx);

        Assert.True(result.HasErrors);
    }

    [Fact]
    public void ApprovalValidation_NoErrors_ReturnsEmpty()
    {
        var svc = new ApprovalValidationService([], []);
        var ctx = MakeContext(ValidationStage.Approve);

        var result = svc.Validate(MakeVatInvoice(), ctx);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Messages);
    }

    // ── KsefSubmissionValidationService ──────────────────────────────────────

    [Fact]
    public void KsefSubmissionValidation_DomainRule_IsInvoked()
    {
        var rule = new AlwaysErrorInvoiceRule("INV-TEST-007", ValidationStage.SendToKsef);
        var svc = new KsefSubmissionValidationService([rule], [], []);
        var ctx = MakeContext(ValidationStage.SendToKsef);

        var result = svc.Validate(MakeVatInvoice(), null, ctx);

        Assert.True(result.HasErrors);
    }

    [Fact]
    public void KsefSubmissionValidation_TechnicalRule_IsInvokedWhenPayloadProvided()
    {
        var techRule = new AlwaysErrorPayloadRule("INV-TEST-008", ValidationStage.SendToKsef);
        var svc = new KsefSubmissionValidationService([], [], [techRule]);
        var ctx = MakeContext(ValidationStage.SendToKsef);
        var payload = new KsefInvoicePayload("<xml/>", "FV/001", "1234567890");

        var result = svc.Validate(MakeVatInvoice(), payload, ctx);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Messages, m => m.Code == "INV-TEST-008");
    }

    [Fact]
    public void KsefSubmissionValidation_TechnicalRule_SkippedWhenNoPayload()
    {
        var techRule = new AlwaysErrorPayloadRule("INV-TEST-009", ValidationStage.SendToKsef);
        var svc = new KsefSubmissionValidationService([], [], [techRule]);
        var ctx = MakeContext(ValidationStage.SendToKsef);

        var result = svc.Validate(MakeVatInvoice(), payload: null, ctx);

        Assert.False(result.HasErrors);
    }

    // ── Helpers (stub rules) ──────────────────────────────────────────────────

    private sealed class AlwaysErrorInvoiceRule(string code, ValidationStage stage)
        : IDomainValidationRule<Invoice>
    {
        public string Code => code;
        public bool AppliesTo(ValidationContext ctx, Invoice target) => true;
        public IEnumerable<ValidationMessage> Validate(ValidationContext ctx, Invoice target)
            => [new(Code, ValidationSeverity.Error, stage, "Error", "Error tech")];
    }

    private sealed class AlwaysWarningInvoiceRule(string code, ValidationStage stage)
        : IDomainValidationRule<Invoice>
    {
        public string Code => code;
        public bool AppliesTo(ValidationContext ctx, Invoice target) => true;
        public IEnumerable<ValidationMessage> Validate(ValidationContext ctx, Invoice target)
            => [new(Code, ValidationSeverity.Warning, stage, "Warning", "Warning tech")];
    }

    private sealed class NeverAppliesInvoiceRule(string code)
        : IDomainValidationRule<Invoice>
    {
        public string Code => code;
        public bool AppliesTo(ValidationContext ctx, Invoice target) => false;
        public IEnumerable<ValidationMessage> Validate(ValidationContext ctx, Invoice target)
            => [];
    }

    private sealed class AlwaysErrorLineRule(string code, ValidationStage stage)
        : IDomainValidationRule<InvoiceLine>
    {
        public string Code => code;
        public bool AppliesTo(ValidationContext ctx, InvoiceLine target) => true;
        public IEnumerable<ValidationMessage> Validate(ValidationContext ctx, InvoiceLine target)
            => [new(Code, ValidationSeverity.Error, stage, "Line error", "Line tech")];
    }

    private sealed class AlwaysErrorPayloadRule(string code, ValidationStage stage)
        : IKsefTechnicalValidationRule<KsefInvoicePayload>
    {
        public string Code => code;
        public bool AppliesTo(ValidationContext ctx, KsefInvoicePayload target) => true;
        public IEnumerable<ValidationMessage> Validate(ValidationContext ctx, KsefInvoicePayload target)
            => [new(Code, ValidationSeverity.Error, stage, "Payload error", "Payload tech")];
    }
}
