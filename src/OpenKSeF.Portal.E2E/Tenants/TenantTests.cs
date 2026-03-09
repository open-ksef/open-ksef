using Microsoft.Playwright;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E.Tenants;

[Explicit("Requires running portal+keycloak and configured KEYCLOAK_USERNAME/KEYCLOAK_PASSWORD.")]
public sealed class TenantTests : BasePortalTest
{
    [Test]
    public async Task TenantSwitching_UpdatesInvoiceRowsForSelectedTenant()
    {
        if (string.IsNullOrWhiteSpace(Options.Username) || string.IsNullOrWhiteSpace(Options.Password))
        {
            Assert.Ignore("KEYCLOAK_USERNAME / KEYCLOAK_PASSWORD are not configured for this environment.");
        }

        await SeedSecondTenantWithDistinctInvoicesAsync();

        await LoginWithKeycloakAsync();
        await NavigateToPortalAsync("/invoices");

        var tenantFilter = Page.Locator("[data-testid='invoice-tenant-filter']");
        await Assertions.Expect(tenantFilter).ToBeVisibleAsync();

        var optionsCount = await Page.Locator("[data-testid='invoice-tenant-filter'] option").CountAsync();
        Assert.That(optionsCount, Is.GreaterThanOrEqualTo(2), "Expected at least two tenant options.");

        await tenantFilter.SelectOptionAsync(new SelectOptionValue { Index = 0 });
        await Page.ClickAsync("[data-testid='invoice-apply-filters']");
        await Assertions.Expect(Page.Locator("[data-testid='invoice-table']")).ToBeVisibleAsync();

        var firstTenantInvoiceNumber = await Page.Locator("[data-testid='invoice-ksef-number']").First.InnerTextAsync();

        await tenantFilter.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await Page.ClickAsync("[data-testid='invoice-apply-filters']");
        await Assertions.Expect(Page.Locator("[data-testid='invoice-table']")).ToBeVisibleAsync();

        var secondTenantInvoiceNumber = await Page.Locator("[data-testid='invoice-ksef-number']").First.InnerTextAsync();

        Assert.That(secondTenantInvoiceNumber, Is.Not.EqualTo(firstTenantInvoiceNumber));
    }

    private async Task SeedSecondTenantWithDistinctInvoicesAsync()
    {
        if (!DatabaseFixture.References.TryGetValue("TestUserId", out var userId) || string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("Test fixture does not contain seeded TestUserId reference.");
        }

        var tenantId = await DatabaseFixture.CreateTestTenantAsync(userId, "9876543210", "Portal E2E Tenant B");
        await DatabaseFixture.CreateTestCredentialAsync(tenantId, "encrypted-e2e-token-b");

        await using var db = DatabaseFixture.CreateDbContext();
        db.InvoiceHeaders.AddRange(
            new InvoiceHeader
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                KSeFInvoiceNumber = "E2E-B-001",
                KSeFReferenceNumber = "E2E-B-REF-001",
                VendorName = "E2E Vendor B",
                VendorNip = "9876543210",
                AmountGross = 201m,
                Currency = "PLN",
                IssueDate = DateTime.UtcNow.Date,
                FirstSeenAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow
            },
            new InvoiceHeader
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                KSeFInvoiceNumber = "E2E-B-002",
                KSeFReferenceNumber = "E2E-B-REF-002",
                VendorName = "E2E Vendor B",
                VendorNip = "9876543210",
                AmountGross = 202m,
                Currency = "PLN",
                IssueDate = DateTime.UtcNow.Date.AddDays(-1),
                FirstSeenAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();
    }
}
