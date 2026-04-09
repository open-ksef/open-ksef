using System.Reflection;
using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Domain.Tests.Entities;

/// <summary>
/// Architectural guard: legacy EF entities must remain pure data containers.
/// No public methods should be added — if you need behaviour, use a mapper or domain service.
/// </summary>
#pragma warning disable CS0618 // accessing obsolete types intentionally in architecture tests
public class LegacyEntityArchitectureTests
{
    [Fact]
    public void InvoiceHeader_HasNoBehaviourMethods()
    {
        var methods = typeof(InvoiceHeader)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName) // exclude property get_/set_ accessors
            .ToList();

        Assert.Empty(methods);
    }

    [Fact]
    public void InvoiceLine_HasNoBehaviourMethods()
    {
        var methods = typeof(InvoiceLine)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();

        Assert.Empty(methods);
    }

    [Fact]
    public void InvoiceHeader_IsMarkedObsolete()
    {
        var attr = typeof(InvoiceHeader).GetCustomAttribute<ObsoleteAttribute>();
        Assert.NotNull(attr);
    }

    [Fact]
    public void InvoiceLine_IsMarkedObsolete()
    {
        var attr = typeof(InvoiceLine).GetCustomAttribute<ObsoleteAttribute>();
        Assert.NotNull(attr);
    }
}
#pragma warning restore CS0618
