using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Events;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Tests.TestSupport;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;
using OpenKSeF.Invoices.Domain.Validation.Rules;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.ValidationTests;

public class ImmutabilityScenarioTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void Imm001_ContentEditAfterKsefSuccess_IsBlockedWithInvVal101()
    {
        var invoice = MakeAcceptedInvoice();

        var result = CreateApprovalService().Validate(invoice, MakeApproveContext("ApprovedToDraft"));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-101");
        Assert.Throws<InvoiceDomainException>(() => invoice.SetCommercialData(publicNotes: "blocked"));
    }

    [Fact]
    public void Imm002_ResendAcceptedDocumentAsOriginal_IsBlockedWithInvVal093()
    {
        var invoice = MakeAcceptedInvoice();

        var result = CreateSubmissionService().Validate(
            invoice,
            payload: null,
            MakeSendContext(new Dictionary<string, object?> { ["KsefConfigAvailable"] = true }));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-093");
    }

    [Fact]
    public void Imm003_DuplicatePrintAfterKsefSuccess_RecordsMetadataWithoutChangingContent()
    {
        var invoice = MakeAcceptedInvoice();
        var totalsBefore = invoice.Totals;
        var notesBefore = invoice.PublicNotes;

        invoice.RecordDuplicateIssue(new DateTime(2026, 04, 10, 13, 0, 0, DateTimeKind.Utc), "operator");

        var duplicate = Assert.Single(invoice.DuplicateIssuances);
        Assert.Equal(new DateTime(2026, 04, 10, 13, 0, 0, DateTimeKind.Utc), duplicate.IssuedAt);
        Assert.Equal("operator", duplicate.IssuedBy);
        Assert.Equal(totalsBefore, invoice.Totals);
        Assert.Equal(notesBefore, invoice.PublicNotes);
        Assert.Contains(invoice.DomainEvents, e => e is InvoiceDuplicateIssued);
    }

    private static ApprovalValidationService CreateApprovalService() =>
        new([], [], [new AcceptedKsefDocumentCannotBeEditedRule()]);

    private static KsefSubmissionValidationService CreateSubmissionService() =>
        new([new AcceptedKsefDocumentCannotBeResentRule()], [], []);

    private static ValidationContext MakeApproveContext(string transition) =>
        new(
            ValidationStage.Approve,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: true,
            Items: new Dictionary<string, object?> { ["RequestedTransition"] = transition });

    private static ValidationContext MakeSendContext(IReadOnlyDictionary<string, object?> items) =>
        new(
            ValidationStage.SendToKsef,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: true,
            IsNumberAssigned: true,
            Items: items);

    private static Invoice MakeAcceptedInvoice()
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.VatInvoice,
            Seller,
            Buyer,
            Pln,
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Required);

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Service",
            1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.SetCommercialData(publicNotes: "original content");
        invoice.RecalculateTotals();
        invoice.Approve(new DateTime(2026, 04, 10, 10, 0, 0, DateTimeKind.Utc));
        invoice.SubmitToKsef(new DateTime(2026, 04, 10, 11, 0, 0, DateTimeKind.Utc));
        invoice.AcceptByKsef(
            new KsefIdentifiers("KSEF-IMM-1", "REF-IMM-1"),
            new DateTime(2026, 04, 10, 12, 0, 0, DateTimeKind.Utc));
        return invoice;
    }
}
