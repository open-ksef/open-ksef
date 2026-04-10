using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Application.Commands.CreateFinalInvoiceFromAdvances;

public sealed class CreateFinalInvoiceFromAdvancesHandler : ICreateFinalInvoiceFromAdvancesHandler
{
    public Invoice Handle(IReadOnlyList<Invoice> advances, CreateFinalInvoiceFromAdvancesCommand command)
    {
        ArgumentNullException.ThrowIfNull(advances);
        ArgumentNullException.ThrowIfNull(command);

        if (advances.Count == 0)
        {
            throw CreateDraftValidationException("INV-VAL-071", "AdvanceDocumentIds");
        }

        ValidateCommercialConsistency(advances, new TenantId(command.TenantId));

        var first = advances[0];
        var final = Invoice.Draft(
            InvoiceId.New(),
            new TenantId(command.TenantId),
            DocumentKind.FinalInvoice,
            first.Seller,
            first.Buyer,
            first.Currency,
            command.IssueDate,
            first.KsefSubmissionRequirement);

        NormalizeReferencesAndAllocations(final, advances, command);

        return final;
    }

    private static void ValidateCommercialConsistency(IReadOnlyList<Invoice> advances, TenantId expectedTenantId)
    {
        var first = advances[0];
        foreach (var adv in advances)
        {
            if (adv.TenantId != expectedTenantId)
                throw CreateDraftValidationException("INV-VAL-073", "AdvanceDocumentIds");

            if (adv.Seller.Nip?.Value != first.Seller.Nip?.Value)
                throw CreateDraftValidationException("INV-VAL-073", "AdvanceDocumentIds");

            if (adv.Buyer.Nip?.Value != first.Buyer.Nip?.Value)
                throw CreateDraftValidationException("INV-VAL-073", "AdvanceDocumentIds");

            if (adv.Currency.Value != first.Currency.Value)
                throw CreateDraftValidationException("INV-VAL-073", "AdvanceDocumentIds");
        }
    }

    private static void NormalizeReferencesAndAllocations(
        Invoice final,
        IReadOnlyList<Invoice> advances,
        CreateFinalInvoiceFromAdvancesCommand command)
    {
        var settlementByAdvanceId = command.Advances
            .ToDictionary(e => new InvoiceId(e.AdvanceInvoiceId));

        foreach (var adv in advances)
        {
            final.AddAdvanceDocumentId(adv.Id);

            if (settlementByAdvanceId.TryGetValue(adv.Id, out var entry))
            {
                var settledAmount = new Money(entry.SettledAmount, final.Currency);
                var advanceNumber = adv.DocumentNumber
                    ?? new DocumentNumber(entry.AdvanceDocumentNumber);

                final.AddAdvanceAllocation(new AdvanceAllocation(adv.Id, advanceNumber, settledAmount));
            }
        }
    }

    private static InvoiceDomainException CreateDraftValidationException(string code, string path) =>
        new(
            $"Final invoice draft validation failed with {code}.",
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
