using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Policies;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class DocumentNumberMustBeUniqueRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-031";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage is ValidationStage.Approve or ValidationStage.SendToKsef;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.DocumentNumber is null)
        {
            return [];
        }

        var uniquenessPolicy = context.Items.TryGetValue("DocumentUniquenessPolicy", out var item)
            ? item as IDocumentUniquenessPolicy
            : null;

        if (uniquenessPolicy is null || !uniquenessPolicy.IsDuplicate(context.TenantId, target.DocumentNumber))
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Error,
                context.Stage,
                "Numer dokumentu jest już użyty.",
                "DocumentNumber uniqueness violation in policy scope.",
                "DocumentNumber")
        ];
    }
}
