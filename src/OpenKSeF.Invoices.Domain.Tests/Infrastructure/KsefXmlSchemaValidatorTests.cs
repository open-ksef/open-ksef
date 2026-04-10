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
using OpenKSeF.Invoices.Infrastructure.Mapping;
using OpenKSeF.Invoices.Infrastructure.Validation;

namespace OpenKSeF.Invoices.Domain.Tests.InfrastructureTests;

public class KsefXmlSchemaValidatorTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller Sp. z o.o."), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer SA"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void IsValid_ReturnsTrue_ForWellFormedPayloadWithRequiredFields()
    {
        var invoice = MakeApprovedInvoice("FV/2026/010");
        var mapper = new InvoiceToKsefPayloadMapper();
        var payload = mapper.TryMap(invoice)!;

        var validator = new KsefXmlSchemaValidator();
        var result = validator.IsValid(payload.InvoiceXml, out var errors);

        Assert.True(result, $"Expected valid but got errors: {string.Join("; ", errors)}");
    }

    [Fact]
    public void IsValid_ReturnsFalse_ForEmptyXml()
    {
        var validator = new KsefXmlSchemaValidator();
        var result = validator.IsValid("", out var errors);

        Assert.False(result);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void IsValid_ReturnsFalse_ForMalformedXml()
    {
        var validator = new KsefXmlSchemaValidator();
        var result = validator.IsValid("<unclosed", out var errors);

        Assert.False(result);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void IsValid_ReturnsFalse_ForXmlMissingMandatoryKsefElements()
    {
        const string xml = "<Faktura xmlns=\"http://crd.gov.pl/wzor/2023/06/29/12648/\"><Naglowek/></Faktura>";
        var validator = new KsefXmlSchemaValidator();
        var result = validator.IsValid(xml, out var errors);

        Assert.False(result);
        Assert.NotEmpty(errors);
    }

    /// <summary>
    /// Verifies that the full send-to-KSeF pipeline blocks a payload that fails
    /// schema validation when a real validator is provided in context.
    /// </summary>
    [Fact]
    public void Pipeline_BlocksSubmission_WhenSchemaValidationFails()
    {
        var invoice = MakeApprovedInvoice("FV/2026/011");
        var badPayload = new KsefInvoicePayload("<bad/>", "FV/2026/011", "1234567890");
        var validator = new KsefXmlSchemaValidator();

        var service = new KsefSubmissionValidationService(
            [new KsefPayloadMappingMustSucceedRule()],
            [],
            [new KsefPayloadSchemaMustBeValidRule()]);

        var context = new ValidationContext(
            ValidationStage.SendToKsef,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: true,
            IsNumberAssigned: true,
            Items: new Dictionary<string, object?> { ["KsefXmlSchemaValidator"] = validator });

        var result = service.Validate(invoice, badPayload, context);

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-111");
    }

    /// <summary>
    /// Verifies that the full send-to-KSeF pipeline passes when the mapper produces
    /// valid XML and the real schema validator confirms it.
    /// </summary>
    [Fact]
    public void Pipeline_AllowsSubmission_WhenSchemaValidationPasses()
    {
        var invoice = MakeApprovedInvoice("FV/2026/012");
        var mapper = new InvoiceToKsefPayloadMapper();
        var payload = mapper.TryMap(invoice)!;
        var validator = new KsefXmlSchemaValidator();

        var service = new KsefSubmissionValidationService(
            [new KsefPayloadMappingMustSucceedRule()],
            [],
            [new KsefPayloadSchemaMustBeValidRule()]);

        var context = new ValidationContext(
            ValidationStage.SendToKsef,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: true,
            IsNumberAssigned: true,
            Items: new Dictionary<string, object?> { ["KsefXmlSchemaValidator"] = validator });

        var result = service.Validate(invoice, payload, context);

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-111");
    }

    private static Invoice MakeApprovedInvoice(string number)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.VatInvoice, Seller, Buyer,
            Pln, new DateTime(2026, 4, 10), KsefSubmissionRequirement.Required,
            documentNumber: new DocumentNumber(number));

        invoice.AddLine(InvoiceLine.Create(
            1, "Service", 1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        invoice.Approve(new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc));
        return invoice;
    }
}
