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

public class StateTransitionValidationRuleTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void InvVal100_SendToKsef_ReturnsError_WhenTransitionIsInvalid()
    {
        var invoice = MakeInvoice();
        var service = CreateSubmissionService();

        var result = service.Validate(
            invoice,
            payload: null,
            MakeSendContext("DraftToSubmitted"));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-100", message.Code);
    }

    [Fact]
    public void InvVal101_Approve_ReturnsError_WhenAcceptedDocumentWouldBeEdited()
    {
        var invoice = MakeAcceptedInvoice();
        var service = CreateApprovalService();

        var result = service.Validate(
            invoice,
            MakeApproveContext("ApprovedToDraft"));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-101", message.Code);
    }

    [Fact]
    public void InvVal102_Approve_ReturnsError_WhenReopenForbiddenByPolicy()
    {
        var invoice = MakeApprovedInvoice();
        var service = CreateApprovalService();

        var result = service.Validate(
            invoice,
            MakeApproveContext("ApprovedToDraft", allowReopen: false));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-102", message.Code);
    }

    [Fact]
    public void StateTransitionRules_ReturnNoMessages_WhenTransitionsAreAllowed()
    {
        var approved = MakeApprovedInvoice();
        var approvalService = CreateApprovalService();
        var sendService = CreateSubmissionService();

        var reopen = approvalService.Validate(
            approved,
            MakeApproveContext("ApprovedToDraft", allowReopen: true));

        var send = sendService.Validate(
            approved,
            payload: null,
            MakeSendContext("ApprovedToSubmitted"));

        Assert.Empty(reopen.Messages);
        Assert.Empty(send.Messages);
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
        new(
            [
                new InvalidStateTransitionRule()
            ],
            [],
            []);

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
            Items: new Dictionary<string, object?>
            {
                ["RequestedTransition"] = transition
            });

    private static Invoice MakeInvoice()
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
        invoice.RecalculateTotals();
        return invoice;
    }

    private static Invoice MakeApprovedInvoice()
    {
        var invoice = MakeInvoice();
        invoice.Approve(DateTime.UtcNow);
        return invoice;
    }

    private static Invoice MakeAcceptedInvoice()
    {
        var invoice = MakeApprovedInvoice();
        invoice.SubmitToKsef(DateTime.UtcNow);
        invoice.AcceptByKsef(new KsefIdentifiers("KS/1", "REF/1"), DateTime.UtcNow);
        return invoice;
    }
}
