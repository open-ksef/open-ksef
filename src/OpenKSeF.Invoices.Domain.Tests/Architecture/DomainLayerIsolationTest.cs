using System.Reflection;
using OpenKSeF.Invoices.Domain.Validation;

namespace OpenKSeF.Invoices.Domain.Tests.ArchitectureTests;

/// <summary>
/// X3: Architectural guard — verifies that the Domain project carries no references to
/// infrastructure, EF Core, ASP.NET, or application-layer assemblies.
/// Domain must remain a pure, dependency-free kernel.
/// </summary>
public class DomainLayerIsolationTest
{
    private static readonly Assembly DomainAssembly = typeof(IValidationRule<>).Assembly;

    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "OpenKSeF.Invoices.Infrastructure",
        "OpenKSeF.Invoices.Application",
        "OpenKSeF.Domain",
        "OpenKSeF.Sync",
        "OpenKSeF.Api",
        "OpenKSeF.Worker",
    ];

    [Fact]
    public void InvoicesDomain_DoesNotReferenceInfrastructureOrApplicationAssemblies()
    {
        var referencedNames = DomainAssembly
            .GetReferencedAssemblies()
            .Select(a => a.FullName ?? string.Empty)
            .ToList();

        var violations = referencedNames
            .Where(name => ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"OpenKSeF.Invoices.Domain must not reference infrastructure or application assemblies. " +
            $"Found forbidden references: {string.Join(", ", violations)}");
    }

    [Fact]
    public void InvoicesDomain_DoesNotContainEfCoreNamespaces()
    {
        var forbiddenNamespaces = new[]
        {
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
        };

        var types = DomainAssembly.GetTypes();
        var violations = new List<string>();

        foreach (var type in types)
        {
            if (type.Namespace is null) continue;

            foreach (var forbidden in forbiddenNamespaces)
            {
                if (type.Namespace.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{type.FullName} (from forbidden namespace {forbidden})");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Domain types must not live in EF/ASP.NET namespaces: {string.Join(", ", violations)}");
    }
}
