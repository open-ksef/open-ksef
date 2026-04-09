using System.Reflection;
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

public class CurrencyValidationRuleTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void InvVal040_Approve_ReturnsError_WhenCurrencyIsMissing()
    {
        var invoice = MakeInvoice(CurrencyCode.Pln);
        SetCurrency(invoice, null);
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-040", message.Code);
        Assert.Equal("Currency", message.Path);
    }

    [Fact]
    public void InvVal040_Approve_ReturnsNoMessage_WhenCurrencyIsPresent()
    {
        var invoice = MakeInvoice(CurrencyCode.Pln);
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-040");
    }

    [Fact]
    public void InvVal041_Approve_ReturnsError_WhenTenantIsPlnOnlyAndCurrencyIsForeign()
    {
        var invoice = MakeInvoice(new CurrencyCode("EUR"));
        var service = CreateApprovalService();

        var result = service.Validate(
            invoice,
            MakeContext(ValidationStage.Approve, currency: new CurrencyPolicy("PLN")));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-041", message.Code);
    }

    [Fact]
    public void InvVal041_Approve_ReturnsNoMessage_WhenTenantDefaultCurrencyMatchesDocument()
    {
        var invoice = MakeInvoice(CurrencyCode.Pln);
        var service = CreateApprovalService();

        var result = service.Validate(
            invoice,
            MakeContext(ValidationStage.Approve, currency: new CurrencyPolicy("PLN")));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-041");
    }

    [Fact]
    public void InvVal042_Draft_ReturnsWarning_WhenForeignCurrencyHasNoExchangeRateMetadata()
    {
        var invoice = MakeInvoice(new CurrencyCode("EUR"));
        var service = CreateDraftService();

        var result = service.Validate(
            invoice,
            MakeContext(ValidationStage.Draft, currency: new CurrencyPolicy("PLN")));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-042", message.Code);
        Assert.Equal(ValidationSeverity.Warning, message.Severity);
    }

    [Fact]
    public void InvVal042_Draft_ReturnsNoMessage_WhenForeignCurrencyHasExchangeRateMetadata()
    {
        var invoice = MakeInvoice(new CurrencyCode("EUR"));
        var service = CreateDraftService();

        var result = service.Validate(
            invoice,
            MakeContext(
                ValidationStage.Draft,
                currency: new CurrencyPolicy("PLN"),
                items: new Dictionary<string, object?> { ["ExchangeRate"] = 4.25m }));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-042");
    }

    private static DraftValidationService CreateDraftService() =>
        new([new ForeignCurrencyRequiresExchangeRateMetadataRule()], []);

    private static ApprovalValidationService CreateApprovalService() =>
        new(
            [
                new CurrencyCodeRequiredRule(),
                new ForeignCurrencyBlockedByPolicyRule()
            ],
            []);

    private static ValidationContext MakeContext(
        ValidationStage stage,
        CurrencyPolicy? currency = null,
        IReadOnlyDictionary<string, object?>? items = null) =>
        new(
            stage,
            Tenant,
            DateTime.UtcNow,
            new PolicySnapshotForTests(currency ?? new CurrencyPolicy()),
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: false,
            Items: items ?? new Dictionary<string, object?>());

    private static Invoice MakeInvoice(CurrencyCode currency)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.VatInvoice,
            Seller,
            Buyer,
            currency,
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Required);

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Service",
            1m,
            new Money(100m, currency),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }

    private static void SetCurrency(Invoice invoice, CurrencyCode? currency)
    {
        var property = typeof(Invoice).GetProperty(nameof(Invoice.Currency), BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property!.SetValue(invoice, currency);
    }

    private sealed class PolicySnapshotForTests(CurrencyPolicy currency) : IPolicySnapshot
    {
        public NumberingPolicy Numbering { get; } = new();
        public KsefPolicy Ksef { get; } = new();
        public VatPolicy Vat { get; } = new();
        public EditPolicy Edit { get; } = new();
        public ValidationPolicy Validation { get; } = new();
        public CurrencyPolicy Currency { get; } = currency;
    }
}
