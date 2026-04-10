using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Tests.TestSupport;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;
using OpenKSeF.Invoices.Domain.Validation.Rules;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.ValidationTests;

public class StateTransitionScenarioTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot BusinessBuyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));
    private static readonly BuyerSnapshot ConsumerBuyer =
        new(new PartyName("Consumer"), BuyerKind.Consumer);

    [Fact]
    public void St001_DraftToApproved_ChangesStatusToApproved()
    {
        var invoice = MakeInvoice(BusinessBuyer);

        invoice.Approve(new DateTime(2026, 04, 10, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(DocumentStatus.Approved, invoice.Status);
    }

    [Fact]
    public void St002_ApprovedBackToDraft_WhenPolicyAllows_ReturnsToDraft()
    {
        var invoice = MakeInvoice(ConsumerBuyer);
        invoice.Approve(new DateTime(2026, 04, 10, 10, 0, 0, DateTimeKind.Utc));

        invoice.Reopen(new DateTime(2026, 04, 10, 11, 0, 0, DateTimeKind.Utc), allowReopen: true);

        Assert.Equal(DocumentStatus.Draft, invoice.Status);
    }

    [Fact]
    public void St003_ApprovedBackToDraft_WhenPolicyForbids_BlockedWithInvVal102()
    {
        var invoice = MakeInvoice(BusinessBuyer);
        invoice.Approve(new DateTime(2026, 04, 10, 10, 0, 0, DateTimeKind.Utc));

        var result = CreateApprovalService().Validate(invoice, MakeApproveContext("ApprovedToDraft", allowReopen: false));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-102");
    }

    [Fact]
    public void St004_InvalidSendFromDraft_BlockedWithInvVal100()
    {
        var invoice = MakeInvoice(BusinessBuyer);

        var result = CreateSubmissionService().Validate(invoice, payload: null, MakeSendContext("DraftToSubmitted"));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-100");
    }

    [Fact]
    public void St005_SubmittedToAcceptedByKsef_StoresIdentifiersAndBecomesImmutable()
    {
        var invoice = MakeInvoice(BusinessBuyer);
        invoice.Approve(new DateTime(2026, 04, 10, 10, 0, 0, DateTimeKind.Utc));
        invoice.SubmitToKsef(new DateTime(2026, 04, 10, 11, 0, 0, DateTimeKind.Utc));

        var identifiers = new KsefIdentifiers("KSEF-2026-1", "REF-2026-1");
        invoice.AcceptByKsef(identifiers, new DateTime(2026, 04, 10, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(DocumentStatus.AcceptedByKsef, invoice.Status);
        Assert.Equal(identifiers, invoice.KsefIdentifiers);
        Assert.Throws<InvoiceDomainException>(() => invoice.SetCommercialData(publicNotes: "blocked"));
    }

    [Fact]
    public void St006_SubmittedToRejectedByKsef_StoresRejectionAndRemainsEditable()
    {
        var invoice = MakeInvoice(BusinessBuyer);
        invoice.Approve(new DateTime(2026, 04, 10, 10, 0, 0, DateTimeKind.Utc));
        invoice.SubmitToKsef(new DateTime(2026, 04, 10, 11, 0, 0, DateTimeKind.Utc));

        invoice.RejectByKsef("Schema validation failed", new DateTime(2026, 04, 10, 12, 0, 0, DateTimeKind.Utc));
        invoice.SetCommercialData(publicNotes: "editable after rejection");

        Assert.Equal(DocumentStatus.RejectedByKsef, invoice.Status);
        Assert.Equal("Schema validation failed", invoice.KsefRejectionReason);
        Assert.Equal("editable after rejection", invoice.PublicNotes);
    }

    private static ApprovalValidationService CreateApprovalService() =>
        new(
            [],
            [],
            [
                new InvalidStateTransitionRule(),
                new AcceptedKsefDocumentCannotBeEditedRule(),
                new ApprovedDocumentCannotReturnToDraftWhenPolicyForbidsRule()
            ]);

    private static KsefSubmissionValidationService CreateSubmissionService() =>
        new([new InvalidStateTransitionRule()], [], []);

    private static ValidationContext MakeApproveContext(string transition, bool? allowReopen = null) =>
        new(
            ValidationStage.Approve,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: true,
            Items: new Dictionary<string, object?>
            {
                ["RequestedTransition"] = transition,
                ["AllowReopenApproved"] = allowReopen
            });

    private static ValidationContext MakeSendContext(string transition) =>
        new(
            ValidationStage.SendToKsef,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: true,
            IsNumberAssigned: true,
            Items: new Dictionary<string, object?> { ["RequestedTransition"] = transition });

    private static Invoice MakeInvoice(BuyerSnapshot buyer)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.VatInvoice,
            Seller,
            buyer,
            Pln,
            new DateTime(2026, 04, 10),
            buyer.Kind == BuyerKind.Business
                ? KsefSubmissionRequirement.Required
                : KsefSubmissionRequirement.Optional);

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Service",
            1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }
}
