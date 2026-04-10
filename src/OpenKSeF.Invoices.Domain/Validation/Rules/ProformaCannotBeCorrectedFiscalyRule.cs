using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class ProformaCannotBeCorrectedFiscalyRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-083";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.Kind != DocumentKind.CorrectionInvoice || target.CorrectionReference is null)
        {
            return [];
        }

        var originalKind = context.Items.TryGetValue("OriginalDocumentKind", out var item)
            ? item as DocumentKind?
            : null;

        if (originalKind != DocumentKind.Proforma)
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Proforma nie podlega korekcie fiskalnej.",
                "Correction references non-fiscal proforma.",
                "CorrectionReference")
        ];
    }
}
