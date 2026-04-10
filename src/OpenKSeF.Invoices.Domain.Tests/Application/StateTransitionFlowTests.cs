using OpenKSeF.Invoices.Application.Commands.ApproveInvoice;
using OpenKSeF.Invoices.Application.Commands.CreateInvoice;
using OpenKSeF.Invoices.Application.Commands.ReopenInvoice;
using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Tests.TestSupport;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;
using OpenKSeF.Invoices.Domain.Validation.Rules;

namespace OpenKSeF.Invoices.Domain.Tests.ApplicationTests;

public class StateTransitionFlowTests
{
    [Fact]
    public void St001_DraftToApproved_FlowChangesStatusToApproved()
    {
        var invoice = CreateDraftInvoice();
        var handler = new ApproveInvoiceHandler(new ApprovalValidationService([], []));

        handler.Handle(
            invoice,
            new ApproveInvoiceCommand(invoice.Id.Value, new DateTime(2026, 04, 10, 12, 0, 0, DateTimeKind.Utc)),
            MakeApprovalContext(invoice.TenantId));

        Assert.Equal(DocumentStatus.Approved, invoice.Status);
    }

    [Fact]
    public void St002_ApprovedBackToDraft_WhenPolicyAllows_ReturnsToDraft()
    {
        var invoice = CreateDraftInvoice();
        new ApproveInvoiceHandler(new ApprovalValidationService([], []))
            .Handle(
                invoice,
                new ApproveInvoiceCommand(invoice.Id.Value, new DateTime(2026, 04, 10, 12, 0, 0, DateTimeKind.Utc)),
                MakeApprovalContext(invoice.TenantId));

        new ReopenInvoiceHandler(new AlwaysAllowReopenPolicy())
            .Handle(invoice, new ReopenInvoiceCommand(invoice.Id.Value, new DateTime(2026, 04, 10, 13, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(DocumentStatus.Draft, invoice.Status);
    }

    [Fact]
    public void St003_ApprovedBackToDraft_WhenPolicyForbids_Throws()
    {
        var invoice = CreateDraftInvoice();
        new ApproveInvoiceHandler(new ApprovalValidationService([], []))
            .Handle(
                invoice,
                new ApproveInvoiceCommand(invoice.Id.Value, new DateTime(2026, 04, 10, 12, 0, 0, DateTimeKind.Utc)),
                MakeApprovalContext(invoice.TenantId));

        var handler = new ReopenInvoiceHandler(new NeverAllowReopenPolicy());

        Assert.Throws<InvoiceDomainException>(() =>
            handler.Handle(invoice, new ReopenInvoiceCommand(invoice.Id.Value, DateTime.UtcNow)));
    }

    [Fact]
    public void St004_InvalidSendFromDraft_BlockedWithInvVal100()
    {
        var invoice = CreateDraftInvoice();
        var service = new KsefSubmissionValidationService([new InvalidStateTransitionRule()], [], []);

        var result = service.Validate(
            invoice,
            payload: null,
            new ValidationContext(
                ValidationStage.SendToKsef,
                invoice.TenantId,
                DateTime.UtcNow,
                TestPolicySnapshot.Default,
                IsKsefSubmissionRequested: true,
                IsNumberAssigned: true,
                Items: new Dictionary<string, object?> { ["RequestedTransition"] = "DraftToSubmitted" }));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-100");
    }

    private static Domain.Aggregates.Invoice CreateDraftInvoice()
    {
        var handler = new CreateInvoiceHandler();
        var invoice = handler.Handle(new CreateInvoiceCommand(
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
            "FV/2026/0001"));

        invoice.AddLine(Domain.Entities.InvoiceLine.Create(
            1,
            "Service",
            1m,
            new Domain.ValueObjects.Money(100m, Domain.ValueObjects.CurrencyCode.Pln),
            PricingMode.Net,
            Domain.ValueObjects.VatRate.OfPercentage(new Domain.ValueObjects.Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }

    private static ValidationContext MakeApprovalContext(Domain.ValueObjects.TenantId tenantId) =>
        new(
            ValidationStage.Approve,
            tenantId,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: true,
            Items: new Dictionary<string, object?>());
}
