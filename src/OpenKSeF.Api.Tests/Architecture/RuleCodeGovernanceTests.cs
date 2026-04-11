using OpenKSeF.Api.Services;
using System.Text.RegularExpressions;

namespace OpenKSeF.Api.Tests.Architecture;

/// <summary>
/// X3 Architectural guard: every INV-VAL-### code used in the backend source tree
/// must exist in the InvoiceValidationSpecificationCatalog (driven by 02-validation-specification.md).
/// Prevents silently shipping rule codes that are not documented or localized.
/// </summary>
public class RuleCodeGovernanceTests
{
    private static readonly string SrcRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));

    private static readonly Regex RuleCodePattern = new(
        @"INV-VAL-\d{3}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void AllRuleCodesUsedInSourceExistInValidationSpecificationCatalog()
    {
        var catalog = new InvoiceValidationSpecificationCatalog();
        var catalogCodes = catalog.GetAllCodes().ToHashSet(StringComparer.Ordinal);

        var sourceFiles = Directory.GetFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                     && !f.EndsWith(".g.cs", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(sourceFiles);

        var missingCodes = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            foreach (Match match in RuleCodePattern.Matches(content))
            {
                var code = match.Value;
                if (!catalogCodes.Contains(code))
                    missingCodes.Add(code);
            }
        }

        Assert.True(
            missingCodes.Count == 0,
            $"The following rule codes appear in source code but are not defined in " +
            $"02-validation-specification.md: {string.Join(", ", missingCodes)}. " +
            $"Add the missing code(s) to domain/02-validation-specification.md and rebuild.");
    }
}
