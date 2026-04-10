using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Policies;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Application.Commands.CreateCorrectionFromOriginal;

public sealed class CreateCorrectionFromOriginalHandler(ICorrectionPolicy correctionPolicy) : ICreateCorrectionFromOriginalHandler
{
    public Invoice Handle(Invoice original, CreateCorrectionFromOriginalCommand command)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(command);

        if (original.TenantId.Value != command.TenantId)
        {
            throw new InvoiceDomainException("Correction invoice tenant must match the original invoice tenant.");
        }

        if (string.IsNullOrWhiteSpace(command.ReasonDescription))
        {
            throw CreateDraftValidationException("INV-VAL-081", "CorrectionReference.ReasonDescription");
        }

        if (!correctionPolicy.CanCorrect(original))
        {
            var code = original.Kind == DocumentKind.Proforma ? "INV-VAL-083" : "INV-VAL-080";
            throw CreateDraftValidationException(code, "CorrectionReference");
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

    private static InvoiceDomainException CreateDraftValidationException(string code, string path) =>
        new(
            $"Correction draft validation failed with {code}.",
            stage: ValidationStage.Draft,
            validationResult: new ValidationResult(
            [
                new ValidationMessage(
                    code,
                    ValidationSeverity.Error,
                    ValidationStage.Draft,
                    code,
                    code,
                    path)
            ]));
}
