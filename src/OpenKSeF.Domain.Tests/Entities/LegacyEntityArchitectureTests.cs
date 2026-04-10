using System.Reflection;
using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Domain.Tests.Entities;

/// <summary>
/// Architectural guard: synced read-side EF entities must remain pure data containers.
/// No public methods should be added — if you need behaviour, use a mapper or domain service.
/// </summary>
public class LegacyEntityArchitectureTests
{
    [Fact]
    public void SyncedInvoice_HasNoBehaviourMethods()
    {
        var methods = typeof(SyncedInvoice)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName) // exclude property get_/set_ accessors
            .ToList();

        Assert.Empty(methods);
    }

    [Fact]
    public void SyncedInvoiceLine_HasNoBehaviourMethods()
    {
        var methods = typeof(SyncedInvoiceLine)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();

        Assert.Empty(methods);
    }

    [Fact]
    public void SyncedInvoice_IsNotObsolete()
    {
        // After rename, SyncedInvoice is the correct name — it must NOT be marked obsolete
        var attr = typeof(SyncedInvoice).GetCustomAttribute<ObsoleteAttribute>();
        Assert.Null(attr);
    }

    [Fact]
    public void SyncedInvoiceLine_IsNotObsolete()
    {
        var attr = typeof(SyncedInvoiceLine).GetCustomAttribute<ObsoleteAttribute>();
        Assert.Null(attr);
    }
}
