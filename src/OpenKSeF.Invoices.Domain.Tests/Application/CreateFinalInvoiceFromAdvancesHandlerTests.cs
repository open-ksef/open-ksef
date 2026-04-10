using OpenKSeF.Invoices.Application.Commands.CreateFinalInvoiceFromAdvances;
using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.ApplicationTests;

public class CreateFinalInvoiceFromAdvancesHandlerTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller Co"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer Co"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void Handle_CreatesFinalInvoiceReferencingBothAdvances()
    {
        var adv1 = MakeApprovedAdvanceInvoice("ADV/2026/001");
        var adv2 = MakeApprovedAdvanceInvoice("ADV/2026/002");

        var command = new CreateFinalInvoiceFromAdvancesCommand(
            Tenant.Value,
            new DateTime(2026, 4, 20),
            [
                new AdvanceSettlementEntry(adv1.Id.Value, "ADV/2026/001", 300m),
                new AdvanceSettlementEntry(adv2.Id.Value, "ADV/2026/002", 400m)
            ]);

        var handler = new CreateFinalInvoiceFromAdvancesHandler();
        var final = handler.Handle([adv1, adv2], command);

        Assert.Equal(DocumentKind.FinalInvoice, final.Kind);
        Assert.Equal(DocumentStatus.Draft, final.Status);
        Assert.Equal(2, final.AdvanceDocumentIds.Count);
        Assert.Contains(adv1.Id, final.AdvanceDocumentIds);
        Assert.Contains(adv2.Id, final.AdvanceDocumentIds);
        Assert.Equal(2, final.SettledAdvanceAllocations.Count);
    }

    [Fact]
    public void Handle_NormalizesAllocationAmountsFromCommand()
    {
        var adv = MakeApprovedAdvanceInvoice("ADV/2026/003");
        var command = new CreateFinalInvoiceFromAdvancesCommand(
            Tenant.Value,
            new DateTime(2026, 4, 20),
            [new AdvanceSettlementEntry(adv.Id.Value, "ADV/2026/003", 500m)]);

        var final = new CreateFinalInvoiceFromAdvancesHandler().Handle([adv], command);

        var allocation = Assert.Single(final.SettledAdvanceAllocations);
        Assert.Equal(500m, allocation.SettledAmount.Amount);
        Assert.Equal(Pln, allocation.SettledAmount.Currency);
    }

    [Fact]
    public void Handle_UsesAdvanceDocumentNumberFromAggregate_WhenPresent()
    {
        var adv = MakeApprovedAdvanceInvoice("ADV/ORIG/001");
        var command = new CreateFinalInvoiceFromAdvancesCommand(
            Tenant.Value,
            new DateTime(2026, 4, 20),
            [new AdvanceSettlementEntry(adv.Id.Value, "STALE-NUMBER", 100m)]);

        var final = new CreateFinalInvoiceFromAdvancesHandler().Handle([adv], command);

        var allocation = Assert.Single(final.SettledAdvanceAllocations);
        Assert.Equal("ADV/ORIG/001", allocation.AdvanceDocumentNumber.Value);
    }

    [Fact]
    public void Handle_InheritsSellerBuyerAndCurrencyFromFirstAdvance()
    {
        var adv = MakeApprovedAdvanceInvoice("ADV/2026/010");
        var command = new CreateFinalInvoiceFromAdvancesCommand(
            Tenant.Value,
            new DateTime(2026, 4, 20),
            [new AdvanceSettlementEntry(adv.Id.Value, "ADV/2026/010", 100m)]);

        var final = new CreateFinalInvoiceFromAdvancesHandler().Handle([adv], command);

        Assert.Equal(Seller.Nip?.Value, final.Seller.Nip?.Value);
        Assert.Equal(Buyer.Nip?.Value, final.Buyer.Nip?.Value);
        Assert.Equal(Pln, final.Currency);
    }

    [Fact]
    public void Handle_Throws_WhenNoAdvancesProvided()
    {
        var command = new CreateFinalInvoiceFromAdvancesCommand(
            Tenant.Value,
            new DateTime(2026, 4, 20),
            []);

        Assert.Throws<InvoiceDomainException>(() =>
            new CreateFinalInvoiceFromAdvancesHandler().Handle([], command));
    }

    [Fact]
    public void Handle_Throws_WhenAdvancesHaveDifferentBuyers()
    {
        var adv1 = MakeApprovedAdvanceInvoice("ADV/2026/020");
        var differentBuyer = new BuyerSnapshot(new PartyName("Other Buyer"), BuyerKind.Business, new Nip("1111111111"));
        var adv2 = MakeAdvanceInvoiceWithBuyer(differentBuyer, "ADV/2026/021");

        var command = new CreateFinalInvoiceFromAdvancesCommand(
            Tenant.Value,
            new DateTime(2026, 4, 20),
            [
                new AdvanceSettlementEntry(adv1.Id.Value, "ADV/2026/020", 100m),
                new AdvanceSettlementEntry(adv2.Id.Value, "ADV/2026/021", 100m)
            ]);

        Assert.Throws<InvoiceDomainException>(() =>
            new CreateFinalInvoiceFromAdvancesHandler().Handle([adv1, adv2], command));
    }

    [Fact]
    public void Handle_Throws_WhenAdvancesHaveDifferentCurrencies()
    {
        var adv1 = MakeApprovedAdvanceInvoice("ADV/2026/030");
        var adv2 = MakeAdvanceInvoiceWithCurrency(new CurrencyCode("EUR"), "ADV/2026/031");

        var command = new CreateFinalInvoiceFromAdvancesCommand(
            Tenant.Value,
            new DateTime(2026, 4, 20),
            [
                new AdvanceSettlementEntry(adv1.Id.Value, "ADV/2026/030", 100m),
                new AdvanceSettlementEntry(adv2.Id.Value, "ADV/2026/031", 100m)
            ]);

        Assert.Throws<InvoiceDomainException>(() =>
            new CreateFinalInvoiceFromAdvancesHandler().Handle([adv1, adv2], command));
    }

    [Fact]
    public void Handle_Throws_WhenAdvanceTenantDiffersFromCommandTenant()
    {
        var foreignTenant = new TenantId(Guid.NewGuid());
        var adv = MakeAdvanceInvoiceWithTenant(foreignTenant, "ADV/2026/040");
        var command = new CreateFinalInvoiceFromAdvancesCommand(
            Tenant.Value,
            new DateTime(2026, 4, 20),
            [new AdvanceSettlementEntry(adv.Id.Value, "ADV/2026/040", 100m)]);

        Assert.Throws<InvoiceDomainException>(() =>
            new CreateFinalInvoiceFromAdvancesHandler().Handle([adv], command));
    }

    private static Invoice MakeApprovedAdvanceInvoice(string number)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.AdvanceInvoice,
            Seller,
            Buyer,
            Pln,
            new DateTime(2026, 4, 10),
            KsefSubmissionRequirement.Required,
            documentNumber: new DocumentNumber(number));

        invoice.AddLine(InvoiceLine.Create(
            1, "Advance", 1m,
            new Money(400m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        invoice.Approve(new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc));
        return invoice;
    }

    private static Invoice MakeAdvanceInvoiceWithBuyer(BuyerSnapshot buyer, string number)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.AdvanceInvoice,
            Seller,
            buyer,
            Pln,
            new DateTime(2026, 4, 10),
            KsefSubmissionRequirement.Required,
            documentNumber: new DocumentNumber(number));

        invoice.AddLine(InvoiceLine.Create(
            1, "Advance", 1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        invoice.Approve(new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc));
        return invoice;
    }

    private static Invoice MakeAdvanceInvoiceWithCurrency(CurrencyCode currency, string number)
    {
        return MakeAdvanceInvoice(Tenant, Seller, Buyer, currency, number);
    }

    private static Invoice MakeAdvanceInvoiceWithTenant(TenantId tenant, string number)
    {
        return MakeAdvanceInvoice(tenant, Seller, Buyer, Pln, number);
    }

    private static Invoice MakeAdvanceInvoice(
        TenantId tenant,
        SellerSnapshot seller,
        BuyerSnapshot buyer,
        CurrencyCode currency,
        string number)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            tenant,
            DocumentKind.AdvanceInvoice,
            seller,
            buyer,
            currency,
            new DateTime(2026, 4, 10),
            KsefSubmissionRequirement.Required,
            documentNumber: new DocumentNumber(number));

        invoice.AddLine(InvoiceLine.Create(
            1, "Advance", 1m,
            new Money(100m, currency),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        invoice.Approve(new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc));
        return invoice;
    }
}
