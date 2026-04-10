using System.Text.Json;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Infrastructure.Persistence;

public sealed class IssuedInvoiceAggregateMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Invoice ToAggregate(IssuedInvoiceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var currency = new CurrencyCode(record.Currency);
        var invoice = Invoice.Draft(
            new InvoiceId(record.Id),
            new TenantId(record.TenantId),
            ParseEnum<DocumentKind>(record.Kind),
            new SellerSnapshot(new PartyName(record.SellerName), new Nip(record.SellerNip)),
            new BuyerSnapshot(new PartyName(record.BuyerName), ParseEnum<BuyerKind>(record.BuyerKind), string.IsNullOrWhiteSpace(record.BuyerNip) ? null : new Nip(record.BuyerNip)),
            currency,
            record.IssueDate,
            ParseEnum<KsefSubmissionRequirement>(record.KsefSubmissionRequirement),
            string.IsNullOrWhiteSpace(record.DocumentNumber) ? null : new DocumentNumber(record.DocumentNumber),
            CreateCorrectionReference(record),
            record.ExternalReference);

        invoice.SetIssueDates(record.IssueDate, record.SaleDate, record.DueDate);
        invoice.SetCommercialData(record.PaymentMethod, record.PublicNotes, record.InternalNotes);

        foreach (var line in record.Lines.OrderBy(line => line.LineNumber))
        {
            invoice.AddLine(ToLine(line, currency));
        }

        foreach (var advanceDocumentId in Deserialize(record.AdvanceDocumentIdsJson, Array.Empty<Guid>()))
        {
            invoice.AddAdvanceDocumentId(new InvoiceId(advanceDocumentId));
        }

        foreach (var allocation in Deserialize(record.SettledAdvanceAllocationsJson, Array.Empty<AdvanceAllocationRecord>()))
        {
            invoice.AddAdvanceAllocation(new AdvanceAllocation(
                new InvoiceId(allocation.AdvanceInvoiceId),
                new DocumentNumber(allocation.AdvanceDocumentNumber),
                new Money(allocation.SettledAmount, currency)));
        }

        invoice.RecalculateTotals();
        ApplyState(record, invoice);

        foreach (var duplicate in Deserialize(record.DuplicateIssuancesJson, Array.Empty<DuplicateIssuanceRecord>()))
        {
            invoice.RecordDuplicateIssue(duplicate.IssuedAt, duplicate.IssuedBy);
        }

        return invoice;
    }

    public void Apply(Invoice invoice, IssuedInvoiceRecord record)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(record);

        record.TenantId = invoice.TenantId.Value;
        record.Kind = invoice.Kind.ToString();
        record.Status = invoice.Status.ToString();
        record.BuyerKind = invoice.BuyerKind.ToString();
        record.KsefSubmissionRequirement = invoice.KsefSubmissionRequirement.ToString();
        record.KsefSubmissionState = invoice.KsefSubmissionState.ToString();
        record.SellerName = invoice.Seller.Name.Value;
        record.SellerNip = invoice.Seller.Nip?.Value ?? string.Empty;
        record.BuyerName = invoice.Buyer.Name.Value;
        record.BuyerNip = invoice.Buyer.Nip?.Value;
        record.IssueDate = invoice.IssueDate;
        record.SaleDate = invoice.SaleDate;
        record.DueDate = invoice.DueDate;
        record.ApprovedAt = invoice.ApprovedAt;
        record.SubmittedToKsefAt = invoice.SubmittedToKsefAt;
        record.AcceptedByKsefAt = invoice.AcceptedByKsefAt;
        record.Currency = invoice.Currency.Value;
        record.TotalNet = invoice.Totals.NetTotal.Amount;
        record.TotalVat = invoice.Totals.VatTotal.Amount;
        record.TotalGross = invoice.Totals.GrossTotal.Amount;
        record.DocumentNumber = invoice.DocumentNumber?.Value;
        record.ExternalReference = invoice.ExternalReference;
        record.PaymentMethod = invoice.PaymentMethod;
        record.PublicNotes = invoice.PublicNotes;
        record.InternalNotes = invoice.InternalNotes;
        record.KsefDocumentNumber = invoice.KsefIdentifiers?.KsefDocumentNumber;
        record.KsefReferenceNumber = invoice.KsefIdentifiers?.KsefReferenceNumber;
        record.KsefRejectionReason = invoice.KsefRejectionReason;
        record.AdvanceDocumentIdsJson = JsonSerializer.Serialize(
            invoice.AdvanceDocumentIds.Select(id => id.Value).ToList(),
            JsonOptions);
        record.SettledAdvanceAllocationsJson = JsonSerializer.Serialize(
            invoice.SettledAdvanceAllocations
                .Select(allocation => new AdvanceAllocationRecord(
                    allocation.AdvanceInvoiceId.Value,
                    allocation.AdvanceDocumentNumber.Value,
                    allocation.SettledAmount.Amount))
                .ToList(),
            JsonOptions);
        record.DuplicateIssuancesJson = JsonSerializer.Serialize(
            invoice.DuplicateIssuances
                .Select(duplicate => new DuplicateIssuanceRecord(duplicate.IssuedAt, duplicate.IssuedBy))
                .ToList(),
            JsonOptions);
        record.CorrectionOriginalInvoiceId = invoice.CorrectionReference?.OriginalInvoiceId.Value;
        record.CorrectionOriginalDocumentNumber = invoice.CorrectionReference?.OriginalDocumentNumber.Value;
        record.CorrectionReasonKind = invoice.CorrectionReference?.ReasonKind.ToString();
        record.CorrectionReasonDescription = invoice.CorrectionReference?.ReasonDescription;

        record.Lines.Clear();
        foreach (var line in invoice.LineItems)
        {
            record.Lines.Add(new IssuedInvoiceLineRecord
            {
                Id = line.LineId.Value,
                IssuedInvoiceId = invoice.Id.Value,
                LineNumber = line.LineNumber,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitOfMeasure = line.UnitOfMeasure,
                PricingMode = line.PricingMode.ToString(),
                UnitPrice = line.UnitPrice.Amount,
                DiscountPercent = line.Discount?.Value,
                VatRate = line.VatRate.ToString(),
                VatClassification = line.VatClassification?.Code,
                CorrectionRole = line.CorrectionRole.ToString(),
                NetAmount = line.NetAmount.Amount,
                VatAmount = line.VatAmount.Amount,
                GrossAmount = line.GrossAmount.Amount
            });
        }
    }

    private static void ApplyState(IssuedInvoiceRecord record, Invoice invoice)
    {
        var status = ParseEnum<DocumentStatus>(record.Status);
        var approvedAt = record.ApprovedAt ?? record.UpdatedAt;
        var submittedAt = record.SubmittedToKsefAt ?? approvedAt;
        var acceptedAt = record.AcceptedByKsefAt ?? submittedAt;

        switch (status)
        {
            case DocumentStatus.Draft:
                return;
            case DocumentStatus.Approved:
                invoice.Approve(approvedAt);
                return;
            case DocumentStatus.SubmittedToKsef:
                invoice.Approve(approvedAt);
                invoice.SubmitToKsef(submittedAt);
                return;
            case DocumentStatus.AcceptedByKsef:
                invoice.Approve(approvedAt);
                invoice.SubmitToKsef(submittedAt);
                invoice.AcceptByKsef(
                    new KsefIdentifiers(
                        record.KsefDocumentNumber ?? "UNKNOWN",
                        record.KsefReferenceNumber ?? "UNKNOWN"),
                    acceptedAt);
                return;
            case DocumentStatus.RejectedByKsef:
                invoice.Approve(approvedAt);
                invoice.SubmitToKsef(submittedAt);
                invoice.RejectByKsef(record.KsefRejectionReason ?? string.Empty, record.UpdatedAt);
                return;
            default:
                throw new InvalidOperationException($"Unsupported invoice status '{record.Status}'.");
        }
    }

    private static InvoiceLine ToLine(IssuedInvoiceLineRecord line, CurrencyCode currency)
    {
        var vatRate = ParseVatRate(line.VatRate);
        var discount = line.DiscountPercent.HasValue ? new Percentage(line.DiscountPercent.Value) : null;

        return InvoiceLine.Create(
            line.LineNumber,
            line.Description,
            line.Quantity,
            new Money(line.UnitPrice, currency),
            ParseEnum<PricingMode>(line.PricingMode),
            vatRate,
            discount,
            line.UnitOfMeasure,
            string.IsNullOrWhiteSpace(line.VatClassification) ? null : new VatClassification(line.VatClassification),
            string.IsNullOrWhiteSpace(line.CorrectionRole)
                ? CorrectionRole.Normal
                : ParseEnum<CorrectionRole>(line.CorrectionRole));
    }

    private static CorrectionReference? CreateCorrectionReference(IssuedInvoiceRecord record)
    {
        if (record.CorrectionOriginalInvoiceId is null ||
            string.IsNullOrWhiteSpace(record.CorrectionOriginalDocumentNumber) ||
            string.IsNullOrWhiteSpace(record.CorrectionReasonKind))
        {
            return null;
        }

        return new CorrectionReference(
            new InvoiceId(record.CorrectionOriginalInvoiceId.Value),
            new DocumentNumber(record.CorrectionOriginalDocumentNumber),
            ParseEnum<CorrectionReasonKind>(record.CorrectionReasonKind),
            record.CorrectionReasonDescription);
    }

    private static T ParseEnum<T>(string value)
        where T : struct, Enum =>
        Enum.Parse<T>(value, ignoreCase: true);

    private static VatRate ParseVatRate(string value)
    {
        if (value.StartsWith("Exempt(", StringComparison.OrdinalIgnoreCase) &&
            value.EndsWith(')'))
        {
            var code = value[7..^1];
            return VatRate.OfExemption(new TaxExemptionReason(code));
        }

        var percentage = value.EndsWith('%')
            ? value[..^1]
            : value;

        return VatRate.OfPercentage(new Percentage(decimal.Parse(percentage)));
    }

    private static T Deserialize<T>(string? json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallback;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
    }

    private sealed record AdvanceAllocationRecord(
        Guid AdvanceInvoiceId,
        string AdvanceDocumentNumber,
        decimal SettledAmount);

    private sealed record DuplicateIssuanceRecord(
        DateTime IssuedAt,
        string? IssuedBy);
}
