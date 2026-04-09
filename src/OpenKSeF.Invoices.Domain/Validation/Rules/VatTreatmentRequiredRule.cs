using OpenKSeF.Invoices.Domain.Entities;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class VatTreatmentRequiredRule : IDomainValidationRule<InvoiceLine>
{
    public string Code => "INV-VAL-060";

    public bool AppliesTo(ValidationContext context, InvoiceLine target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, InvoiceLine target)
    {
        var hasTreatment = target.VatRate is not null ||
                           !string.IsNullOrWhiteSpace(target.VatClassification?.Code);

        if (hasTreatment)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Każda pozycja musi mieć określoną stawkę VAT lub podstawę zwolnienia.",
                "Line missing VatRate/VatExemptionReason.",
                $"LineItems[{target.LineNumber}].VatRate")
        ];
    }
}
