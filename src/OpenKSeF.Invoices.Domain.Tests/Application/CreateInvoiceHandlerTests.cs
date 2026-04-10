using OpenKSeF.Invoices.Application.Commands.CreateInvoice;
using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Tests.ApplicationTests;

public class CreateInvoiceHandlerTests
{
    [Fact]
    public void Handle_CreatesDraftInvoiceFromCommand()
    {
        var handler = new CreateInvoiceHandler();
        var command = new CreateInvoiceCommand(
            Guid.NewGuid(),
            DocumentKind.VatInvoice,
            "Seller",
            "1234567890",
            "Buyer",
            BuyerKind.Business,
            "9876543210",
            "PLN",
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Required,
            "FV/2026/0001",
            "EXT-1");

        var invoice = handler.Handle(command);

        Assert.Equal(DocumentKind.VatInvoice, invoice.Kind);
        Assert.Equal(DocumentStatus.Draft, invoice.Status);
        Assert.Equal("Seller", invoice.Seller.Name.Value);
        Assert.Equal("1234567890", invoice.Seller.Nip.Value);
        Assert.Equal("Buyer", invoice.Buyer.Name.Value);
        Assert.Equal(BuyerKind.Business, invoice.BuyerKind);
        Assert.Equal("9876543210", invoice.Buyer.Nip!.Value);
        Assert.Equal("PLN", invoice.Currency.Value);
        Assert.Equal("FV/2026/0001", invoice.DocumentNumber!.Value);
        Assert.Equal("EXT-1", invoice.ExternalReference);
    }

    [Fact]
    public void Handle_AllowsBuyerWithoutNip()
    {
        var handler = new CreateInvoiceHandler();
        var command = new CreateInvoiceCommand(
            Guid.NewGuid(),
            DocumentKind.Proforma,
            "Seller",
            "1234567890",
            "Consumer",
            BuyerKind.Consumer,
            null,
            "PLN",
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Forbidden);

        var invoice = handler.Handle(command);

        Assert.Null(invoice.Buyer.Nip);
        Assert.Equal(KsefSubmissionRequirement.Forbidden, invoice.KsefSubmissionRequirement);
    }
}
