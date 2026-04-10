using OpenKSeF.Invoices.Application.Commands.ReopenInvoice;
using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Tests.TestSupport;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.ApplicationTests;

public class ReopenInvoiceHandlerTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void Handle_ReopensApprovedInvoice_WhenPolicyAllows()
    {
        var invoice = MakeApprovedInvoice();
        var handler = new ReopenInvoiceHandler(new AlwaysAllowReopenPolicy());

        handler.Handle(invoice, new ReopenInvoiceCommand(invoice.Id.Value, new DateTime(2026, 04, 10, 13, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(DocumentStatus.Draft, invoice.Status);
        Assert.Null(invoice.ApprovedAt);
    }

    [Fact]
    public void Handle_Throws_WhenPolicyForbidsReopen()
    {
        var invoice = MakeApprovedInvoice();
        var handler = new ReopenInvoiceHandler(new NeverAllowReopenPolicy());

        Assert.Throws<InvoiceDomainException>(() =>
            handler.Handle(invoice, new ReopenInvoiceCommand(invoice.Id.Value, DateTime.UtcNow)));
    }

    private static Invoice MakeApprovedInvoice()
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.VatInvoice,
            Seller,
            Buyer,
            CurrencyCode.Pln,
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Required,
            documentNumber: new DocumentNumber("FV/2026/0001"));

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Service",
            1m,
            new Money(100m, CurrencyCode.Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        invoice.Approve(new DateTime(2026, 04, 10, 12, 0, 0, DateTimeKind.Utc));
        return invoice;
    }
}
