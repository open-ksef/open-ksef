using System.Text;
using System.Text.RegularExpressions;
using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Validation.Rules;

public sealed class DocumentNumberPatternRule : IDomainValidationRule<Invoice>
{
    public string Code => "INV-VAL-032";

    public bool AppliesTo(ValidationContext context, Invoice target) =>
        context.Stage == ValidationStage.Draft;

    public IEnumerable<ValidationMessage> Validate(ValidationContext context, Invoice target)
    {
        if (target.DocumentNumber is null)
        {
            return [];
        }

        var pattern = BuildRegex(context.Policies.Numbering.FormatTemplate);
        if (Regex.IsMatch(target.DocumentNumber.Value, pattern, RegexOptions.CultureInvariant))
        {
            return [];
        }

        return
        [
            new ValidationMessage(
                Code,
                ValidationSeverity.Warning,
                context.Stage,
                "Numer dokumentu nie pasuje do domyślnego wzorca.",
                "DocumentNumber does not match NumberingPolicy pattern.",
                "DocumentNumber")
        ];
    }

    private static string BuildRegex(string formatTemplate)
    {
        var builder = new StringBuilder("^");

        for (var i = 0; i < formatTemplate.Length; i++)
        {
            if (formatTemplate[i] != '{')
            {
                builder.Append(Regex.Escape(formatTemplate[i].ToString()));
                continue;
            }

            var end = formatTemplate.IndexOf('}', i + 1);
            if (end < 0)
            {
                builder.Append(Regex.Escape(formatTemplate[i].ToString()));
                continue;
            }

            var token = formatTemplate.Substring(i + 1, end - i - 1);
            builder.Append(token switch
            {
                "YEAR" => "\\d{4}",
                var t when t.StartsWith("SEQ:", StringComparison.Ordinal) => $"\\d{{{t["SEQ:".Length..].Length}}}",
                _ => ".+"
            });
            i = end;
        }

        builder.Append("$");
        return builder.ToString();
    }
}
