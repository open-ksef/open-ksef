using OpenKSeF.Invoices.Domain.Validation;
using System.Reflection;
using System.Text.RegularExpressions;

namespace OpenKSeF.Api.Services;

public interface IInvoiceValidationSpecificationCatalog
{
    InvoiceValidationSpecificationEntry GetRequired(string code);
    IReadOnlyCollection<string> GetAllCodes();
}

public sealed record InvoiceValidationSpecificationEntry(
    string Code,
    ValidationSeverity Severity,
    IReadOnlyList<ValidationStage> Stages,
    string MessagePl,
    string MessageTechnical);

public sealed partial class InvoiceValidationSpecificationCatalog : IInvoiceValidationSpecificationCatalog
{
    private readonly Lazy<IReadOnlyDictionary<string, InvoiceValidationSpecificationEntry>> _entries;

    public InvoiceValidationSpecificationCatalog()
    {
        _entries = new Lazy<IReadOnlyDictionary<string, InvoiceValidationSpecificationEntry>>(LoadEntries);
    }

    public InvoiceValidationSpecificationEntry GetRequired(string code)
    {
        if (_entries.Value.TryGetValue(code, out var entry))
        {
            return entry;
        }

        throw new KeyNotFoundException($"Validation code '{code}' was not found in 02-validation-specification.md.");
    }

    public IReadOnlyCollection<string> GetAllCodes() => _entries.Value.Keys.ToArray();

    private static IReadOnlyDictionary<string, InvoiceValidationSpecificationEntry> LoadEntries()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("OpenKSeF.Api.ValidationSpecs.02-validation-specification.md");

        if (stream is null)
        {
            throw new InvalidOperationException("Embedded validation specification resource is missing.");
        }

        using var reader = new StreamReader(stream);
        var markdown = reader.ReadToEnd();

        return ValidationSectionRegex()
            .Matches(markdown)
            .Select(ParseMatch)
            .ToDictionary(entry => entry.Code, StringComparer.Ordinal);
    }

    private static InvoiceValidationSpecificationEntry ParseMatch(Match match)
    {
        var code = match.Groups["code"].Value;
        var severity = Enum.Parse<ValidationSeverity>(match.Groups["severity"].Value, ignoreCase: false);
        var stages = match.Groups["stages"].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(stage => Enum.Parse<ValidationStage>(stage, ignoreCase: false))
            .ToArray();

        return new InvoiceValidationSpecificationEntry(
            code,
            severity,
            stages,
            match.Groups["user"].Value,
            match.Groups["technical"].Value);
    }

    [GeneratedRegex(
        @"### (?<code>INV-VAL-\d{3})\r?\n- Severity: (?<severity>\w+)\r?\n- Stage: (?<stages>[^\r\n]+)\r?\n- Rule: [^\r\n]+\r?\n- User: `(?<user>[^`]+)`\r?\n- Technical: `(?<technical>[^`]+)`",
        RegexOptions.CultureInvariant)]
    private static partial Regex ValidationSectionRegex();
}
