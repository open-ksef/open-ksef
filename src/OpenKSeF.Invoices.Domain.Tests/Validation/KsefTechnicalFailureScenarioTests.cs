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

public class KsefTechnicalFailureScenarioTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void Ktf001_MappingFailureDueToUnsupportedVatCombination_BlocksWithInvVal110()
    {
        var invoice = MakeInvoice();

        var result = CreateSubmissionService().Validate(
            invoice,
            payload: null,
            MakeSendContext(new Dictionary<string, object?>
            {
                ["KsefPayloadMappingFailed"] = true,
                ["KsefConfigAvailable"] = true
            }));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-110");
    }

    [Fact]
    public void Ktf002_PayloadSchemaValidationFailure_BlocksWithInvVal111()
    {
        var invoice = MakeInvoice();
        var payload = new KsefInvoicePayload("<bad/>", "FV/1", "1234567890");

        var result = CreateSubmissionService().Validate(
            invoice,
            payload,
            MakeSendContext(new Dictionary<string, object?>
            {
                ["KsefSchemaValid"] = false,
                ["KsefConfigAvailable"] = true
            }));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-111");
    }

    [Fact]
    public void Ktf003_MissingKsefCredentials_BlocksWithInvVal092()
    {
        var invoice = MakeInvoice();
        var payload = new KsefInvoicePayload("<Invoice/>", "FV/1", "1234567890");

        var result = CreateSubmissionService().Validate(
            invoice,
            payload,
            MakeSendContext(new Dictionary<string, object?> { ["KsefConfigAvailable"] = false }));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-092");
    }

    private static KsefSubmissionValidationService CreateSubmissionService() =>
        new(
            [
                new KsefPayloadMappingMustSucceedRule(),
                new KsefConfigurationMustBeAvailableRule()
            ],
            [],
            [
                new KsefPayloadSchemaMustBeValidRule()
            ]);

    private static ValidationContext MakeSendContext(IReadOnlyDictionary<string, object?> items) =>
        new(
            ValidationStage.SendToKsef,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: true,
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
