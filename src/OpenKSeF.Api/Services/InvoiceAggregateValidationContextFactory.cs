using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Policies;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Rules;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Api.Services;

public sealed class InvoiceAggregateValidationContextFactory(
    ApplicationDbContext db,
    IClock clock)
{
    public async Task<ValidationContext> CreateDraftAsync(
        Invoice invoice,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        return new ValidationContext(
            ValidationStage.Draft,
            invoice.TenantId,
            clock.UtcNow,
            DefaultPolicySnapshot.Instance,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: invoice.DocumentNumber is not null,
            Items: new Dictionary<string, object?>
            {
                ["NumberingPolicy"] = PassiveNumberingPolicy.Instance
            });
    }

    public async Task<ValidationContext> CreateApprovalAsync(
        Invoice invoice,
        string requestedTransition,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var items = await CreateSharedItemsAsync(invoice, ct);
        items["RequestedTransition"] = requestedTransition;
        items["AllowReopenApproved"] = DefaultPolicySnapshot.Instance.Edit.AllowReopenApproved;

        return new ValidationContext(
            ValidationStage.Approve,
            invoice.TenantId,
            clock.UtcNow,
            DefaultPolicySnapshot.Instance,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: invoice.DocumentNumber is not null,
            Items: items);
    }

    public async Task<ValidationContext> CreateSendToKsefAsync(
        Invoice invoice,
        bool mappingFailed,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var items = await CreateSharedItemsAsync(invoice, ct);
        items["RequestedTransition"] = "ApprovedToSubmitted";
        items["KsefConfigAvailable"] = await db.KSeFCredentials.AnyAsync(
            credential => credential.TenantId == invoice.TenantId.Value,
            ct);
        items["KsefPayloadMappingFailed"] = mappingFailed;

        return new ValidationContext(
            ValidationStage.SendToKsef,
            invoice.TenantId,
            clock.UtcNow,
            DefaultPolicySnapshot.Instance,
            IsKsefSubmissionRequested: true,
            IsNumberAssigned: invoice.DocumentNumber is not null,
            Items: items);
    }

    private async Task<Dictionary<string, object?>> CreateSharedItemsAsync(
        Invoice invoice,
        CancellationToken ct)
    {
        var items = new Dictionary<string, object?>
        {
            ["NumberingPolicy"] = PassiveNumberingPolicy.Instance,
            ["DocumentUniquenessPolicy"] = new DbDocumentUniquenessPolicy(db, invoice.Id),
            ["AdvanceSettlementPolicy"] = DefaultAdvanceSettlementPolicy.Instance
        };

        if (invoice.AdvanceDocumentIds.Count > 0)
        {
            var contexts = await db.IssuedInvoices
                .AsNoTracking()
                .Where(issued => invoice.AdvanceDocumentIds.Select(id => id.Value).Contains(issued.Id))
                .ToDictionaryAsync(
                    issued => new InvoiceId(issued.Id),
                    issued => new AdvanceReferenceContext(
                        issued.SellerNip,
                        issued.BuyerNip,
                        issued.Currency),
                    ct);

            items["AdvanceReferenceContexts"] = contexts;
        }

        return items;
    }

    private sealed class DefaultPolicySnapshot : IPolicySnapshot
    {
        public static readonly DefaultPolicySnapshot Instance = new();

        public NumberingPolicy Numbering { get; } = new();
        public KsefPolicy Ksef { get; } = new();
        public VatPolicy Vat { get; } = new();
        public EditPolicy Edit { get; } = new(AllowReopenApproved: true);
        public ValidationPolicy Validation { get; } = new();
        public CurrencyPolicy Currency { get; } = new();
    }

    private sealed class PassiveNumberingPolicy : INumberingPolicy
    {
        public static readonly PassiveNumberingPolicy Instance = new();

        public bool AssignOnApproval => false;

        public DocumentNumber AssignNumber(Invoice invoice) =>
            invoice.DocumentNumber ?? new DocumentNumber($"TMP/{invoice.Id.Value:N}");
    }

    private sealed class DbDocumentUniquenessPolicy(
        ApplicationDbContext db,
        InvoiceId currentInvoiceId) : IDocumentUniquenessPolicy
    {
        public bool IsDuplicate(TenantId tenantId, DocumentNumber number) =>
            db.IssuedInvoices.Any(invoice =>
                invoice.TenantId == tenantId.Value &&
                invoice.Id != currentInvoiceId.Value &&
                invoice.DocumentNumber == number.Value);
    }

    private sealed class DefaultAdvanceSettlementPolicy : IAdvanceSettlementPolicy
    {
        public static readonly DefaultAdvanceSettlementPolicy Instance = new();

        public bool AreAllocationsValid(Invoice finalInvoice, IReadOnlyList<AdvanceAllocation> allocations) =>
            allocations.Sum(allocation => allocation.SettledAmount.Amount) <= finalInvoice.Totals.GrossTotal.Amount;
    }
}
