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
        var lines = record.Lines
            .OrderBy(line => line.LineNumber)
            .Select(line => ToLine(line, currency))
            .ToList();
        var advanceDocumentIds = Deserialize(record.AdvanceDocumentIdsJson, Array.Empty<Guid>())
            .Select(advanceDocumentId => new InvoiceId(advanceDocumentId))
            .ToList();
        var settledAdvanceAllocations = Deserialize(record.SettledAdvanceAllocationsJson, Array.Empty<AdvanceAllocationRecord>())
            .Select(allocation => new AdvanceAllocation(
                new InvoiceId(allocation.AdvanceInvoiceId),
                new DocumentNumber(allocation.AdvanceDocumentNumber),
                new Money(allocation.SettledAmount, currency)))
            .ToList();
        var duplicateIssuances = Deserialize(record.DuplicateIssuancesJson, Array.Empty<DuplicateIssuanceRecord>())
            .Select(duplicate => new DuplicateMetadata(duplicate.IssuedAt, duplicate.IssuedBy))
            .ToList();

        return Invoice.Restore(
            new InvoiceId(record.Id),
            new TenantId(record.TenantId),
            ParseEnum<DocumentKind>(record.Kind),
            ParseEnum<DocumentStatus>(record.Status),
            new SellerSnapshot(new PartyName(record.SellerName), new Nip(record.SellerNip)),
            new BuyerSnapshot(
                new PartyName(record.BuyerName),
                ParseEnum<BuyerKind>(record.BuyerKind),
                string.IsNullOrWhiteSpace(record.BuyerNip) ? null : new Nip(record.BuyerNip)),
            currency,
            record.IssueDate,
            ParseEnum<KsefSubmissionRequirement>(record.KsefSubmissionRequirement),
            ParseEnum<KsefSubmissionState>(record.KsefSubmissionState),
            new DocumentTotals(
                new Money(record.TotalNet, currency),
                new Money(record.TotalVat, currency),
                new Money(record.TotalGross, currency)),
            BuildVatBreakdown(lines, currency),
            lines,
            string.IsNullOrWhiteSpace(record.DocumentNumber) ? null : new DocumentNumber(record.DocumentNumber),
            CreateCorrectionReference(record),
            record.ExternalReference,
            record.SaleDate,
            record.DueDate,
            record.ApprovedAt,
            record.SubmittedToKsefAt,
            record.AcceptedByKsefAt,
            record.PaymentMethod,
            null,
            record.PublicNotes,
            record.InternalNotes,
            string.IsNullOrWhiteSpace(record.KsefDocumentNumber) || string.IsNullOrWhiteSpace(record.KsefReferenceNumber)
                ? null
                : new KsefIdentifiers(record.KsefDocumentNumber, record.KsefReferenceNumber),
            record.KsefRejectionReason,
            advanceDocumentIds,
            settledAdvanceAllocations,
            duplicateIssuances);
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

        var existingLines = record.Lines.ToDictionary(line => line.Id);
        var currentLineIds = invoice.LineItems.Select(line => line.LineId.Value).ToHashSet();

        foreach (var orphanedLine in record.Lines.Where(line => !currentLineIds.Contains(line.Id)).ToList())
        {
            record.Lines.Remove(orphanedLine);
        }

        foreach (var line in invoice.LineItems)
        {
            if (!existingLines.TryGetValue(line.LineId.Value, out var lineRecord))
            {
                lineRecord = new IssuedInvoiceLineRecord
                {
                    Id = line.LineId.Value,
                    IssuedInvoiceId = invoice.Id.Value
                };
                record.Lines.Add(lineRecord);
            }

            lineRecord.LineNumber = line.LineNumber;
            lineRecord.Description = line.Description;
            lineRecord.Quantity = line.Quantity;
            lineRecord.UnitOfMeasure = line.UnitOfMeasure;
            lineRecord.PricingMode = line.PricingMode.ToString();
            lineRecord.UnitPrice = line.UnitPrice.Amount;
            lineRecord.DiscountPercent = line.Discount?.Value;
            lineRecord.VatRate = line.VatRate.ToString();
            lineRecord.VatClassification = line.VatClassification?.Code;
            lineRecord.CorrectionRole = line.CorrectionRole.ToString();
            lineRecord.NetAmount = line.NetAmount.Amount;
            lineRecord.VatAmount = line.VatAmount.Amount;
            lineRecord.GrossAmount = line.GrossAmount.Amount;
        }
    }

    private static InvoiceLine ToLine(IssuedInvoiceLineRecord line, CurrencyCode currency)
    {
        var vatRate = ParseVatRate(line.VatRate);
        var discount = line.DiscountPercent.HasValue ? new Percentage(line.DiscountPercent.Value) : null;

        return InvoiceLine.Restore(
            new LineId(line.Id),
            line.LineNumber,
            line.Description,
            line.Quantity,
            new Money(line.UnitPrice, currency),
            ParseEnum<PricingMode>(line.PricingMode),
            vatRate,
            new Money(line.NetAmount, currency),
            new Money(line.VatAmount, currency),
            new Money(line.GrossAmount, currency),
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

    private static IReadOnlyList<VatSummary> BuildVatBreakdown(
        IReadOnlyList<InvoiceLine> lines,
        CurrencyCode currency)
    {
        return lines
            .GroupBy(line => line.VatRate)
            .Select(group => new VatSummary(
                group.Key,
                new Money(group.Sum(line => line.NetAmount.Amount), currency),
                new Money(group.Sum(line => line.VatAmount.Amount), currency),
                new Money(group.Sum(line => line.GrossAmount.Amount), currency)))
            .ToList();
    }

    private sealed record AdvanceAllocationRecord(
        Guid AdvanceInvoiceId,
        string AdvanceDocumentNumber,
        decimal SettledAmount);

    private sealed record DuplicateIssuanceRecord(
        DateTime IssuedAt,
        string? IssuedBy);
}
