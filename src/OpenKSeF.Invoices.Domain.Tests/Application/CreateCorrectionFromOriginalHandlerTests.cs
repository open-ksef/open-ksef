using OpenKSeF.Invoices.Application.Commands.CreateCorrectionFromOriginal;
using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Tests.TestSupport;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.ApplicationTests;

public class CreateCorrectionFromOriginalHandlerTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void Handle_CreatesCorrectionInvoiceWithReferenceAndBeforeAfterLines()
    {
        var original = MakeAcceptedOriginalInvoice();
        var handler = new CreateCorrectionFromOriginalHandler(new DefaultCorrectionPolicy());

        var correction = handler.Handle(
            original,
            new CreateCorrectionFromOriginalCommand(
                original.TenantId.Value,
                new DateTime(2026, 04, 11),
                CorrectionReasonKind.ValueChange,
                "Price correction"));

        Assert.Equal(DocumentKind.CorrectionInvoice, correction.Kind);
        Assert.Equal(DocumentStatus.Draft, correction.Status);
        Assert.Equal(original.Id, correction.CorrectionReference!.OriginalInvoiceId);
        Assert.Equal(original.Id, correction.CorrectionReference.EffectiveRootOriginalInvoiceId);
        Assert.Equal(2, correction.LineItems.Count);
        Assert.Contains(correction.LineItems, l => l.CorrectionRole == CorrectionRole.BeforeCorrection);
        Assert.Contains(correction.LineItems, l => l.CorrectionRole == CorrectionRole.AfterCorrection);
    }

    [Fact]
    public void Handle_Throws_WhenPolicyBlocksCorrection()
    {
        var original = MakeDraftOriginalInvoice();
        var handler = new CreateCorrectionFromOriginalHandler(new DefaultCorrectionPolicy());

        Assert.Throws<InvoiceDomainException>(() =>
            handler.Handle(
                original,
                new CreateCorrectionFromOriginalCommand(
                    original.TenantId.Value,
                    new DateTime(2026, 04, 11),
                    CorrectionReasonKind.ValueChange,
                    "Blocked")));
    }

    private static Invoice MakeDraftOriginalInvoice()
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
        return invoice;
    }

    private static Invoice MakeAcceptedOriginalInvoice()
    {
        var invoice = MakeDraftOriginalInvoice();
        invoice.Approve(new DateTime(2026, 04, 10, 10, 0, 0, DateTimeKind.Utc));
        invoice.SubmitToKsef(new DateTime(2026, 04, 10, 11, 0, 0, DateTimeKind.Utc));
        invoice.AcceptByKsef(new KsefIdentifiers("KSEF-COR-1", "REF-COR-1"), new DateTime(2026, 04, 10, 12, 0, 0, DateTimeKind.Utc));
        return invoice;
    }
}
