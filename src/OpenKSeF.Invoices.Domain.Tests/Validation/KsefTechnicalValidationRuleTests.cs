using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Integration;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Tests.TestSupport;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;
using OpenKSeF.Invoices.Domain.Validation.Rules;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.ValidationTests;

public class KsefTechnicalValidationRuleTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void InvVal110_SendToKsef_ReturnsError_WhenPayloadMappingFailed()
    {
        var invoice = MakeInvoice();
        var service = CreateSubmissionService();

        var result = service.Validate(
            invoice,
            payload: null,
            MakeSendContext(new Dictionary<string, object?> { ["KsefPayloadMappingFailed"] = true }));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-110", message.Code);
    }

    [Fact]
    public void InvVal111_SendToKsef_ReturnsError_WhenSchemaValidationFails()
    {
        var invoice = MakeInvoice();
        var payload = new KsefInvoicePayload("<bad/>", "FV/1", "1234567890");
        var service = CreateSubmissionService();

        var result = service.Validate(
            invoice,
            payload,
            MakeSendContext(new Dictionary<string, object?> { ["KsefSchemaValid"] = false }));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-111", message.Code);
    }

    [Fact]
    public void InvVal112_Draft_ReturnsWarning_WhenLocalOnlyFieldsArePresent()
    {
        var invoice = MakeInvoice();
        invoice.SetCommercialData(internalNotes: "local only");
        var service = CreateDraftService();

        var result = service.Validate(
            invoice,
            MakeDraftContext(new Dictionary<string, object?> { ["HasLocalOnlyFields"] = true }));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-112", message.Code);
        Assert.Equal(ValidationSeverity.Warning, message.Severity);
    }

    [Fact]
    public void KsefTechnicalRules_ReturnNoMessages_WhenPayloadAndMappingAreValid()
    {
        var invoice = MakeInvoice();
        var payload = new KsefInvoicePayload("<Invoice/>", "FV/1", "1234567890");
        var submission = CreateSubmissionService();
        var draft = CreateDraftService();

        var sendResult = submission.Validate(
            invoice,
            payload,
            MakeSendContext(new Dictionary<string, object?> { ["KsefSchemaValid"] = true }));

        var draftResult = draft.Validate(
            invoice,
            MakeDraftContext(new Dictionary<string, object?> { ["HasLocalOnlyFields"] = false }));

        Assert.Empty(sendResult.Messages);
        Assert.Empty(draftResult.Messages);
    }

    private static KsefSubmissionValidationService CreateSubmissionService() =>
        new(
            [
                new KsefPayloadMappingMustSucceedRule()
            ],
            [],
            [
                new KsefPayloadSchemaMustBeValidRule()
            ]);

    private static DraftValidationService CreateDraftService() =>
        new(
            [
                new LocalOnlyFieldsOmittedFromKsefPayloadRule()
            ],
            []);

    private static ValidationContext MakeSendContext(IReadOnlyDictionary<string, object?> items) =>
        new(
            ValidationStage.SendToKsef,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: true,
            IsNumberAssigned: true,
            Items: items);

    private static ValidationContext MakeDraftContext(IReadOnlyDictionary<string, object?> items) =>
        new(
            ValidationStage.Draft,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: true,
            Items: items);

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
}
