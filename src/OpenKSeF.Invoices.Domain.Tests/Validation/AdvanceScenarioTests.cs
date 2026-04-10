using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Tests.TestSupport;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;
using OpenKSeF.Invoices.Domain.Validation.Rules;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.ValidationTests;

public class AdvanceScenarioTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void Adv001_ValidAdvanceInvoice_ApprovalSucceeds()
    {
        var invoice = MakeAdvanceInvoice(500m);

        var result = CreateApprovalService().Validate(invoice, MakeContext());

        Assert.Empty(result.Messages);
    }

    [Fact]
    public void Adv002_ZeroValueAdvanceInvoice_BlocksApprovalWithInvVal070()
    {
        var invoice = MakeAdvanceInvoice(0m);

        var result = CreateApprovalService().Validate(invoice, MakeContext());

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-070");
    }

    private static ApprovalValidationService CreateApprovalService() =>
        new(
            [
                new AdvanceInvoiceAmountMustBePositiveRule()
            ],
            []);

    private static ValidationContext MakeContext() =>
        new(
            ValidationStage.Approve,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: true,
            Items: new Dictionary<string, object?>());

    private static Invoice MakeAdvanceInvoice(decimal grossAmount)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.AdvanceInvoice,
            Seller,
            Buyer,
            Pln,
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Required);

        if (grossAmount <= 0m)
        {
            invoice.AddLine(InvoiceLine.Create(
                1,
                "Advance",
                0m,
                Money.Zero(Pln),
                PricingMode.Net,
                VatRate.Zero));
            invoice.RecalculateTotals();
            return invoice;
        }

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Advance",
            1m,
            new Money(grossAmount / 1.23m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }
}
