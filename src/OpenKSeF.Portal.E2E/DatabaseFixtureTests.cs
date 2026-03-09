using Microsoft.EntityFrameworkCore;
using OpenKSeF.Portal.E2E.Fixtures;

namespace OpenKSeF.Portal.E2E;

public class DatabaseFixtureTests
{
    [Test]
    public async Task DatabaseFixture_CanSeedTenantCredentialAndInvoices()
    {
        await using var fixture = await DatabaseFixture.CreateAsync("Data Source=:memory:");

        var userId = fixture.CreateTestUser("portal-e2e-user", "password");
        var tenantId = await fixture.CreateTestTenantAsync(userId, "1234567890", "Test Tenant");
        await fixture.CreateTestCredentialAsync(tenantId, "encrypted-token");
        await fixture.CreateTestInvoicesAsync(tenantId, 3);

        await using var db = fixture.CreateDbContext();
        Assert.That(await db.Tenants.CountAsync(), Is.EqualTo(1));
        Assert.That(await db.KSeFCredentials.CountAsync(), Is.EqualTo(1));
        Assert.That(await db.InvoiceHeaders.CountAsync(), Is.EqualTo(3));
    }

    [Test]
    public async Task CleanupAsync_RemovesSeededData()
    {
        await using var fixture = await DatabaseFixture.CreateAsync("Data Source=:memory:");

        var userId = fixture.CreateTestUser("portal-e2e-user", "password");
        var tenantId = await fixture.CreateTestTenantAsync(userId, "1234567890", "Test Tenant");
        await fixture.CreateTestInvoicesAsync(tenantId, 2);

        await fixture.CleanupAsync();

        await using var db = fixture.CreateDbContext();
        Assert.That(await db.Tenants.CountAsync(), Is.EqualTo(0));
        Assert.That(await db.InvoiceHeaders.CountAsync(), Is.EqualTo(0));
    }
}
