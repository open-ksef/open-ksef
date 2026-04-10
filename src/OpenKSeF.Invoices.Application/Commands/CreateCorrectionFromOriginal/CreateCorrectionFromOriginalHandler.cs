using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Policies;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Application.Commands.CreateCorrectionFromOriginal;

public sealed class CreateCorrectionFromOriginalHandler(ICorrectionPolicy correctionPolicy) : ICreateCorrectionFromOriginalHandler
{
    public Invoice Handle(Invoice original, CreateCorrectionFromOriginalCommand command)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(command);

        if (!correctionPolicy.CanCorrect(original))
        {
            throw new InvoiceDomainException("Original invoice cannot be corrected by current policy.");
        }

        var correction = Invoice.Draft(
            InvoiceId.New(),
            new TenantId(command.TenantId),
            DocumentKind.CorrectionInvoice,
            original.Seller,
            original.Buyer,
            original.Currency,
            command.IssueDate,
            original.KsefSubmissionRequirement,
            correctionReference: CorrectionReference.NormalizeFrom(
                original.Id,
                original.DocumentNumber ?? new DocumentNumber("UNKNOWN"),
                command.ReasonKind,
                command.ReasonDescription,
                original.CorrectionReference),
            externalReference: original.ExternalReference);

        foreach (var line in original.LineItems)
        {
            correction.AddLine(CloneLine(line, line.LineNumber * 2 - 1, CorrectionRole.BeforeCorrection));
            correction.AddLine(CloneLine(line, line.LineNumber * 2, CorrectionRole.AfterCorrection));
        }

        correction.RecalculateTotals();
        return correction;
    }

    private static InvoiceLine CloneLine(InvoiceLine line, int lineNumber, CorrectionRole correctionRole) =>
        InvoiceLine.Create(
            lineNumber,
            line.Description,
            line.Quantity,
            line.UnitPrice,
            line.PricingMode,
            line.VatRate,
            line.Discount,
            line.UnitOfMeasure,
            line.VatClassification,
            correctionRole);
}
