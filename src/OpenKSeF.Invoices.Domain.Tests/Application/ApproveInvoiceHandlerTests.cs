using OpenKSeF.Invoices.Application.Commands.ApproveInvoice;
using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.ApplicationTests;

public class ApproveInvoiceHandlerTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void Handle_ApprovesInvoice_WhenValidationPasses()
    {
        var invoice = MakeDraftInvoice();
        var handler = new ApproveInvoiceHandler(new ApprovalValidationService([], []));
        var approvedAt = new DateTime(2026, 04, 10, 12, 0, 0, DateTimeKind.Utc);

        var result = handler.Handle(
            invoice,
            new ApproveInvoiceCommand(invoice.Id.Value, approvedAt),
            MakeContext());

        Assert.Empty(result.Messages);
        Assert.Equal(DocumentStatus.Approved, invoice.Status);
        Assert.Equal(approvedAt, invoice.ApprovedAt);
    }

    [Fact]
    public void Handle_Throws_WhenValidationFails()
    {
        var invoice = MakeDraftInvoice();
        var handler = new ApproveInvoiceHandler(new ApprovalValidationService([new BlockingRule()], []));

        var exception = Assert.Throws<InvoiceDomainException>(() =>
            handler.Handle(
                invoice,
                new ApproveInvoiceCommand(invoice.Id.Value, DateTime.UtcNow),
                MakeContext()));

        Assert.Equal(ValidationStage.Approve, exception.Stage);
        Assert.NotNull(exception.ValidationResult);
        Assert.Contains(exception.ValidationResult!.Messages, message => message.Code == "INV-TEST-APPROVE");
    }

    private static ValidationContext MakeContext() =>
        new(
            ValidationStage.Approve,
            Tenant,
            DateTime.UtcNow,
            Tests.TestSupport.TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: true,
            Items: new Dictionary<string, object?>());

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

    private sealed class BlockingRule : IDomainValidationRule<Invoice>
    {
        public string Code => "INV-TEST-APPROVE";

        public bool AppliesTo(ValidationContext context, Invoice target) => true;

        public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target) =>
        [
            new(
                Code,
                ValidationSeverity.Error,
                ValidationStage.Approve,
                "Blocked",
                "Blocked",
                "Invoice")
        ];
    }
}
