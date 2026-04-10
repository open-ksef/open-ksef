using OpenKSeF.Invoices.Application.Commands.UpdateInvoiceDraft;
using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.ApplicationTests;

public class UpdateInvoiceDraftHandlerTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void Handle_UpdatesMutableDraftFields()
    {
        var invoice = MakeDraftInvoice();
        var handler = new UpdateInvoiceDraftHandler();
        var command = new UpdateInvoiceDraftCommand(
            invoice.Id.Value,
            new DateTime(2026, 04, 11),
            new DateTime(2026, 04, 10),
            new DateTime(2026, 04, 25),
            "FV/2026/0002",
            "Transfer",
            "Public note",
            "Internal note");

        handler.Handle(invoice, command);

        Assert.Equal(new DateTime(2026, 04, 11), invoice.IssueDate);
        Assert.Equal(new DateTime(2026, 04, 10), invoice.SaleDate);
        Assert.Equal(new DateTime(2026, 04, 25), invoice.DueDate);
        Assert.Equal("FV/2026/0002", invoice.DocumentNumber!.Value);
        Assert.Equal("Transfer", invoice.PaymentMethod);
        Assert.Equal("Public note", invoice.PublicNotes);
        Assert.Equal("Internal note", invoice.InternalNotes);
    }

    [Fact]
    public void Handle_Throws_WhenInvoiceIsNotDraft()
    {
        var invoice = MakeDraftInvoice();
        invoice.Approve(DateTime.UtcNow);
        var handler = new UpdateInvoiceDraftHandler();
        var command = new UpdateInvoiceDraftCommand(invoice.Id.Value, new DateTime(2026, 04, 11));

        Assert.Throws<InvoiceDomainException>(() => handler.Handle(invoice, command));
    }

    private static Invoice MakeDraftInvoice()
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.VatInvoice,
            Seller,
            Buyer,
            CurrencyCode.Pln,
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Required);

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Service",
            1m,
            new Money(100m, CurrencyCode.Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }
}
