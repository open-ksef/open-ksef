using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Application.Commands.UpdateInvoiceDraft;

public sealed class UpdateInvoiceDraftHandler : IUpdateInvoiceDraftHandler
{
    public Invoice Handle(Invoice invoice, UpdateInvoiceDraftCommand command)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(command);

        if (invoice.Status != DocumentStatus.Draft)
        {
            throw new InvoiceDomainException(
                $"Cannot update invoice draft in state {invoice.Status}. Expected Draft.");
        }

        if (command.IssueDate.HasValue || command.SaleDate.HasValue || command.DueDate.HasValue)
        {
            invoice.SetIssueDates(
                command.IssueDate ?? invoice.IssueDate,
                command.SaleDate ?? invoice.SaleDate,
                command.DueDate ?? invoice.DueDate);
        }

        if (!string.IsNullOrWhiteSpace(command.DocumentNumber))
        {
            invoice.SetDocumentNumber(new DocumentNumber(command.DocumentNumber));
        }

        if (command.ExternalReference is not null)
        {
            invoice.SetExternalReference(command.ExternalReference);
        }

        if (command.PaymentMethod is not null || command.PublicNotes is not null || command.InternalNotes is not null)
        {
            invoice.SetCommercialData(
                command.PaymentMethod ?? invoice.PaymentMethod,
                command.PublicNotes ?? invoice.PublicNotes,
                command.InternalNotes ?? invoice.InternalNotes);
        }

        if (command.Lines is not null)
        {
            var existingLines = invoice.LineItems.ToList();
            var nextLines = command.Lines
                .Select((line, index) => CreateLine(invoice, existingLines.ElementAtOrDefault(index)?.LineId, line))
                .ToList();

            invoice.ReplaceLines(nextLines);
            invoice.RecalculateTotals();
        }

        return invoice;
    }

    private static VatRate ParseVatRate(string rawVatRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawVatRate);

        if (rawVatRate.EndsWith("%", StringComparison.Ordinal))
        {
            var percentageText = rawVatRate[..^1];
            if (decimal.TryParse(percentageText, out var percentage))
            {
                return VatRate.OfPercentage(new Percentage(percentage));
            }
        }

        return VatRate.OfExemption(new TaxExemptionReason(rawVatRate));
    }

    private static InvoiceLine CreateLine(
        Invoice invoice,
        LineId? existingLineId,
        UpdateInvoiceDraftLineCommand line)
    {
        var nextLine = InvoiceLine.Create(
            line.LineNumber,
            line.Description,
            line.Quantity,
            new Money(line.UnitPrice, invoice.Currency),
            line.PricingMode,
            ParseVatRate(line.VatRate),
            line.DiscountPercent is null ? null : new Percentage(line.DiscountPercent.Value),
            line.UnitOfMeasure);

        return InvoiceLine.Restore(
            existingLineId ?? LineId.New(),
            nextLine.LineNumber,
            nextLine.Description,
            nextLine.Quantity,
            nextLine.UnitPrice,
            nextLine.PricingMode,
            nextLine.VatRate,
            nextLine.NetAmount,
            nextLine.VatAmount,
            nextLine.GrossAmount,
            nextLine.Discount,
            nextLine.UnitOfMeasure,
            nextLine.VatClassification,
            nextLine.CorrectionRole);
    }
}
