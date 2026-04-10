using OpenKSeF.Invoices.Application.Commands.RecordKsefAcceptance;
using OpenKSeF.Invoices.Application.Commands.RecordKsefRejection;
using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Events;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.ApplicationTests;

public class RecordKsefResultHandlerTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    // ── Acceptance ───────────────────────────────────────────────────────────

    [Fact]
    public void Acceptance_TransitionsToAcceptedByKsefAndStoresIdentifiers()
    {
        var invoice = MakeSubmittedInvoice();
        var handler = new RecordKsefAcceptanceHandler();
        var acceptedAt = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc);

        handler.Handle(invoice, new RecordKsefAcceptanceCommand(
            invoice.Id.Value, "KSEF-ST005-1", "REF-ST005-1", acceptedAt));

        Assert.Equal(DocumentStatus.AcceptedByKsef, invoice.Status);
        Assert.Equal("KSEF-ST005-1", invoice.KsefIdentifiers!.KsefDocumentNumber);
        Assert.Equal("REF-ST005-1", invoice.KsefIdentifiers.KsefReferenceNumber);
        Assert.Equal(KsefSubmissionState.Accepted, invoice.KsefSubmissionState);
    }

    [Fact]
    public void Acceptance_RaisesInvoiceAcceptedByKsefDomainEvent()
    {
        var invoice = MakeSubmittedInvoice();
        var handler = new RecordKsefAcceptanceHandler();

        handler.Handle(invoice, new RecordKsefAcceptanceCommand(
            invoice.Id.Value, "KSEF-EVT-1", "REF-EVT-1",
            new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc)));

        Assert.Contains(invoice.DomainEvents, e => e is InvoiceAcceptedByKsef);
    }

    [Fact]
    public void Acceptance_LocksAggregate_SubsequentMutationThrows()
    {
        var invoice = MakeSubmittedInvoice();
        new RecordKsefAcceptanceHandler().Handle(invoice, new RecordKsefAcceptanceCommand(
            invoice.Id.Value, "KSEF-LOCK-1", "REF-LOCK-1",
            new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc)));

        Assert.Throws<InvoiceDomainException>(() => invoice.SetCommercialData(publicNotes: "blocked"));
    }

    [Fact]
    public void Acceptance_Throws_WhenInvoiceNotInSubmittedState()
    {
        var invoice = MakeDraftInvoice();
        var handler = new RecordKsefAcceptanceHandler();

        Assert.Throws<InvoiceDomainException>(() =>
            handler.Handle(invoice, new RecordKsefAcceptanceCommand(
                invoice.Id.Value, "KSEF-ERR-1", "REF-ERR-1",
                DateTime.UtcNow)));
    }

    // ── Rejection ────────────────────────────────────────────────────────────

    [Fact]
    public void Rejection_TransitionsToRejectedByKsefAndStoresReason()
    {
        var invoice = MakeSubmittedInvoice();
        var handler = new RecordKsefRejectionHandler();

        handler.Handle(invoice, new RecordKsefRejectionCommand(
            invoice.Id.Value, "Schema validation failed", DateTime.UtcNow));

        Assert.Equal(DocumentStatus.RejectedByKsef, invoice.Status);
        Assert.Equal("Schema validation failed", invoice.KsefRejectionReason);
        Assert.Equal(KsefSubmissionState.Rejected, invoice.KsefSubmissionState);
    }

    [Fact]
    public void Rejection_AggregateRemainsEditable_AfterRejection()
    {
        var invoice = MakeSubmittedInvoice();
        new RecordKsefRejectionHandler().Handle(invoice, new RecordKsefRejectionCommand(
            invoice.Id.Value, "Schema error", DateTime.UtcNow));

        invoice.SetCommercialData(publicNotes: "corrected after rejection");

        Assert.Equal("corrected after rejection", invoice.PublicNotes);
    }

    [Fact]
    public void Rejection_AggregateCanBeReapproved_AfterRejection()
    {
        var invoice = MakeSubmittedInvoice();
        new RecordKsefRejectionHandler().Handle(invoice, new RecordKsefRejectionCommand(
            invoice.Id.Value, "Schema error", DateTime.UtcNow));

        invoice.Approve(new DateTime(2026, 4, 10, 13, 0, 0, DateTimeKind.Utc));

        Assert.Equal(DocumentStatus.Approved, invoice.Status);
    }

    [Fact]
    public void Rejection_RaisesInvoiceRejectedByKsefDomainEvent()
    {
        var invoice = MakeSubmittedInvoice();

        new RecordKsefRejectionHandler().Handle(invoice, new RecordKsefRejectionCommand(
            invoice.Id.Value, "Error", DateTime.UtcNow));

        Assert.Contains(invoice.DomainEvents, e => e is InvoiceRejectedByKsef);
    }

    [Fact]
    public void Rejection_Throws_WhenInvoiceNotInSubmittedState()
    {
        var invoice = MakeDraftInvoice();

        Assert.Throws<InvoiceDomainException>(() =>
            new RecordKsefRejectionHandler().Handle(
                invoice,
                new RecordKsefRejectionCommand(invoice.Id.Value, "Error", DateTime.UtcNow)));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Invoice MakeDraftInvoice()
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.VatInvoice, Seller, Buyer,
            Pln, new DateTime(2026, 4, 10), KsefSubmissionRequirement.Required,
            documentNumber: new DocumentNumber("FV/2026/100"));
        invoice.AddLine(InvoiceLine.Create(
            1, "Item", 1m, new Money(100m, Pln), PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }

    private static Invoice MakeSubmittedInvoice()
    {
        var invoice = MakeDraftInvoice();
        invoice.Approve(new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc));
        invoice.SubmitToKsef(new DateTime(2026, 4, 10, 11, 0, 0, DateTimeKind.Utc));
        return invoice;
    }
}
