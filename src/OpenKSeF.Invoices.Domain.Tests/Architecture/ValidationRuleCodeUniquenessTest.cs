using System.Reflection;
using OpenKSeF.Invoices.Domain.Validation;

namespace OpenKSeF.Invoices.Domain.Tests.ArchitectureTests;

/// <summary>
/// X2: Error code governance — verifies that every validation rule has a unique INV-VAL-### code.
/// This test acts as the CI guard against accidental code collisions.
/// </summary>
public class ValidationRuleCodeUniquenessTest
{
    [Fact]
    public void AllValidationRuleCodes_AreUnique()
    {
        var domainAssembly = typeof(IValidationRule<>).Assembly;
        var ruleInstances = InstantiateAllRules(domainAssembly);

        var codes = ruleInstances.Select(r => r.code).ToList();
        var duplicates = codes
            .GroupBy(c => c)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            $"Duplicate validation rule codes found: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void AllValidationRuleCodes_MatchInvValPattern()
    {
        var domainAssembly = typeof(IValidationRule<>).Assembly;
        var ruleInstances = InstantiateAllRules(domainAssembly);

        var invalid = ruleInstances
            .Where(r => !System.Text.RegularExpressions.Regex.IsMatch(r.code, @"^INV-VAL-\d{3}$"))
            .Select(r => $"{r.typeName}: {r.code}")
            .ToList();

        Assert.True(
            invalid.Count == 0,
            $"Validation rules with non-conforming codes (expected INV-VAL-NNN): {string.Join(", ", invalid)}");
    }

    [Fact]
    public void AllValidationRuleCodes_AreNonEmpty()
    {
        var domainAssembly = typeof(IValidationRule<>).Assembly;
        var ruleInstances = InstantiateAllRules(domainAssembly);

        var empty = ruleInstances
            .Where(r => string.IsNullOrWhiteSpace(r.code))
            .Select(r => r.typeName)
            .ToList();

        Assert.True(
            empty.Count == 0,
            $"Validation rules with empty codes: {string.Join(", ", empty)}");
    }

    private static IReadOnlyList<(string typeName, string code)> InstantiateAllRules(Assembly assembly)
    {
        var results = new List<(string, string)>();
        var ruleInterface = typeof(IValidationRule<>);

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;

            var implementedRule = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == ruleInterface);

            if (implementedRule is null) continue;

            try
            {
                var instance = Activator.CreateInstance(type);
                var codeProp = type.GetProperty("Code");
                if (codeProp is null) continue;
                var code = codeProp.GetValue(instance) as string ?? string.Empty;
                results.Add((type.Name, code));
            }
            catch
            {
                // Skip rules that require constructor arguments
            }
        }

        return results;
    }
}
