using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Events;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.ValueObjects;
using InvoiceLine = OpenKSeF.Invoices.Domain.Entities.InvoiceLine;

namespace OpenKSeF.Invoices.Domain.Tests.InvoiceTests;

public class InvoiceStateTransitionTests
{
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly TenantId Tenant = new(Guid.NewGuid());

    private static Domain.Aggregates.Invoice MakeVatInvoice()
    {
        var inv = Domain.Aggregates.Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.VatInvoice,
            new SellerSnapshot(new PartyName("Seller Sp. z o.o."), new Nip("1234567890")),
            new BuyerSnapshot(new PartyName("Buyer S.A."), BuyerKind.Business, new Nip("9876543210")),
            Pln, DateTime.UtcNow,
            KsefSubmissionRequirement.Required);

        var line = InvoiceLine.Create(1, "Service", 1m, new Money(100m, Pln), PricingMode.Net, VatRate.OfPercentage(new Percentage(23)));
        inv.AddLine(line);
        inv.RecalculateTotals();
        return inv;
    }

    [Fact]
    public void Draft_CreatesInvoiceWithDraftStatus()
    {
        var inv = MakeVatInvoice();
        Assert.Equal(DocumentStatus.Draft, inv.Status);
    }

    [Fact]
    public void Draft_EmitsDraftedEvent()
    {
        var inv = MakeVatInvoice();
        Assert.Contains(inv.DomainEvents, e => e is InvoiceDrafted);
    }

    [Fact]
    public void Approve_FromDraft_TransitionsToApproved()
    {
        var inv = MakeVatInvoice();
        inv.Approve(DateTime.UtcNow);

        Assert.Equal(DocumentStatus.Approved, inv.Status);
    }

    [Fact]
    public void Approve_FromDraft_EmitsApprovedEvent()
    {
        var inv = MakeVatInvoice();
        inv.Approve(DateTime.UtcNow);

        Assert.Contains(inv.DomainEvents, e => e is InvoiceApproved);
    }

    [Fact]
    public void Approve_FromApproved_Throws()
    {
        var inv = MakeVatInvoice();
        inv.Approve(DateTime.UtcNow);

        Assert.Throws<InvoiceDomainException>(() => inv.Approve(DateTime.UtcNow));
    }

    [Fact]
    public void Approve_FromAcceptedByKsef_Throws()
    {
        var inv = MakeVatInvoice();
        inv.Approve(DateTime.UtcNow);
        inv.SubmitToKsef(DateTime.UtcNow);
        inv.AcceptByKsef(new KsefIdentifiers("KS/2026/001", "REF-001"), DateTime.UtcNow);

        Assert.Throws<InvoiceDomainException>(() => inv.Approve(DateTime.UtcNow));
    }

    [Fact]
    public void SubmitToKsef_FromApproved_TransitionsToSubmitted()
    {
        var inv = MakeVatInvoice();
        inv.Approve(DateTime.UtcNow);
        inv.SubmitToKsef(DateTime.UtcNow);

        Assert.Equal(DocumentStatus.SubmittedToKsef, inv.Status);
    }

    [Fact]
    public void SubmitToKsef_FromDraft_Throws()
    {
        var inv = MakeVatInvoice();

        Assert.Throws<InvoiceDomainException>(() => inv.SubmitToKsef(DateTime.UtcNow));
    }

    [Fact]
    public void SubmitToKsef_Proforma_Throws()
    {
        var inv = Domain.Aggregates.Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.Proforma,
            new SellerSnapshot(new PartyName("Seller"), new Nip("1234567890")),
            new BuyerSnapshot(new PartyName("Buyer"), BuyerKind.Business),
            Pln, DateTime.UtcNow,
            KsefSubmissionRequirement.Forbidden);

        inv.AddLine(InvoiceLine.Create(1, "Item", 1m, new Money(100m, Pln), PricingMode.Net, VatRate.Zero));
        inv.RecalculateTotals();
        inv.Approve(DateTime.UtcNow);

        Assert.Throws<InvoiceDomainException>(() => inv.SubmitToKsef(DateTime.UtcNow));
    }

    [Fact]
    public void AcceptByKsef_FromSubmitted_TransitionsToAccepted()
    {
        var inv = MakeVatInvoice();
        inv.Approve(DateTime.UtcNow);
        inv.SubmitToKsef(DateTime.UtcNow);
        inv.AcceptByKsef(new KsefIdentifiers("KS/2026/001", "REF-001"), DateTime.UtcNow);

        Assert.Equal(DocumentStatus.AcceptedByKsef, inv.Status);
    }

    [Fact]
    public void AcceptByKsef_AssignsKsefIdentifiers()
    {
        var inv = MakeVatInvoice();
        inv.Approve(DateTime.UtcNow);
        inv.SubmitToKsef(DateTime.UtcNow);
        var ids = new KsefIdentifiers("KS/2026/001", "REF-001");
        inv.AcceptByKsef(ids, DateTime.UtcNow);

        Assert.Equal(ids, inv.KsefIdentifiers);
    }

    [Fact]
    public void AcceptByKsef_EmitsAcceptedEvent()
    {
        var inv = MakeVatInvoice();
        inv.Approve(DateTime.UtcNow);
        inv.SubmitToKsef(DateTime.UtcNow);
        inv.AcceptByKsef(new KsefIdentifiers("KS/2026/001", "REF-001"), DateTime.UtcNow);

        Assert.Contains(inv.DomainEvents, e => e is InvoiceAcceptedByKsef);
    }

    [Fact]
    public void RejectByKsef_FromSubmitted_TransitionsToRejected()
    {
        var inv = MakeVatInvoice();
        inv.Approve(DateTime.UtcNow);
        inv.SubmitToKsef(DateTime.UtcNow);
        inv.RejectByKsef("Schema validation failed", DateTime.UtcNow);

        Assert.Equal(DocumentStatus.RejectedByKsef, inv.Status);
    }

    [Fact]
    public void RejectByKsef_EmitsRejectedEvent()
    {
        var inv = MakeVatInvoice();
        inv.Approve(DateTime.UtcNow);
        inv.SubmitToKsef(DateTime.UtcNow);
        inv.RejectByKsef("Error", DateTime.UtcNow);

        Assert.Contains(inv.DomainEvents, e => e is InvoiceRejectedByKsef);
    }

    [Fact]
    public void Reopen_FromApproved_WithPermission_TransitionsToDraft()
    {
        var inv = MakeVatInvoice();
        inv.Approve(DateTime.UtcNow);
        inv.Reopen(DateTime.UtcNow, allowReopen: true);

        Assert.Equal(DocumentStatus.Draft, inv.Status);
    }

    [Fact]
    public void Reopen_FromApproved_WithoutPermission_Throws()
    {
        var inv = MakeVatInvoice();
        inv.Approve(DateTime.UtcNow);

        Assert.Throws<InvoiceDomainException>(() => inv.Reopen(DateTime.UtcNow, allowReopen: false));
    }

    [Fact]
    public void Reopen_FromAcceptedByKsef_Throws()
    {
        var inv = MakeVatInvoice();
        inv.Approve(DateTime.UtcNow);
        inv.SubmitToKsef(DateTime.UtcNow);
        inv.AcceptByKsef(new KsefIdentifiers("KS/2026/001", "REF-001"), DateTime.UtcNow);

        Assert.Throws<InvoiceDomainException>(() => inv.Reopen(DateTime.UtcNow, allowReopen: true));
    }

    [Fact]
    public void ReApprove_FromRejectedByKsef_TransitionsToApproved()
    {
        var inv = MakeVatInvoice();
        inv.Approve(DateTime.UtcNow);
        inv.SubmitToKsef(DateTime.UtcNow);
        inv.RejectByKsef("Error", DateTime.UtcNow);
        inv.Approve(DateTime.UtcNow);

        Assert.Equal(DocumentStatus.Approved, inv.Status);
    }
}
