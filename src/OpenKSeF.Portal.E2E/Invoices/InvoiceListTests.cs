using Microsoft.Playwright;
using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E.Invoices;

[Explicit("Requires running portal+keycloak and configured KEYCLOAK_USERNAME/KEYCLOAK_PASSWORD.")]
public sealed class InvoiceListTests : BasePortalTest
{
    [Test]
    public async Task InvoiceList_RendersHeaders_Paginates_AndResetsPageAfterDateFilter()
    {
        if (string.IsNullOrWhiteSpace(Options.Username) || string.IsNullOrWhiteSpace(Options.Password))
        {
            Assert.Ignore("KEYCLOAK_USERNAME / KEYCLOAK_PASSWORD are not configured for this environment.");
        }

        await SeedInvoiceListDataAsync();

        await LoginWithKeycloakAsync();
        await NavigateToPortalAsync("/invoices");

        await Page.SelectOptionAsync("[data-testid='invoice-page-size']", "25");
        await Page.ClickAsync("[data-testid='invoice-apply-filters']");
        await Page.WaitForURLAsync(url => url.Contains("pageSize=25", StringComparison.OrdinalIgnoreCase));

        await Assertions.Expect(Page.Locator("[data-testid='invoice-table']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("[data-testid='invoice-row']")).ToHaveCountAsync(25);
        await Assertions.Expect(Page.Locator("[data-testid='invoice-prev-page']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("[data-testid='invoice-next-page']")).ToBeVisibleAsync();

        var headerTexts = await Page.Locator("[data-testid='invoice-table'] thead th").AllInnerTextsAsync();
        var normalizedHeaders = headerTexts.Select(text => text.Trim()).Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();
        Assert.That(normalizedHeaders, Does.Contain("KSeF Number"));
        Assert.That(normalizedHeaders, Does.Contain("Vendor Name"));
        Assert.That(normalizedHeaders, Does.Contain("Vendor NIP"));
        Assert.That(normalizedHeaders, Does.Contain("Issue Date"));
        Assert.That(normalizedHeaders, Does.Contain("Amount"));
        Assert.That(normalizedHeaders, Does.Contain("Currency"));

        var firstPageFirstInvoice = (await Page.Locator("[data-testid='invoice-ksef-number']").First.InnerTextAsync()).Trim();
        Assert.That(firstPageFirstInvoice, Is.EqualTo("E2E-KSEF-001"));

        await Page.ClickAsync("[data-testid='invoice-next-page']");
        await Page.WaitForURLAsync(url => url.Contains("page=2", StringComparison.OrdinalIgnoreCase));
        await Assertions.Expect(Page.Locator("[data-testid='invoice-row']")).ToHaveCountAsync(10);

        var secondPageFirstInvoice = (await Page.Locator("[data-testid='invoice-ksef-number']").First.InnerTextAsync()).Trim();
        Assert.That(secondPageFirstInvoice, Is.EqualTo("E2E-KSEF-026"));

        var dateFrom = DateTime.UtcNow.Date.AddDays(-4).ToString("yyyy-MM-dd");
        var dateTo = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        await Page.FillAsync("[data-testid='invoice-date-from']", dateFrom);
        await Page.FillAsync("[data-testid='invoice-date-to']", dateTo);
        await Page.ClickAsync("[data-testid='invoice-apply-filters']");

        await Page.WaitForURLAsync(url =>
            url.Contains("page=1", StringComparison.OrdinalIgnoreCase) &&
            url.Contains($"dateFrom={dateFrom}", StringComparison.OrdinalIgnoreCase) &&
            url.Contains($"dateTo={dateTo}", StringComparison.OrdinalIgnoreCase));
        await Assertions.Expect(Page.Locator("[data-testid='invoice-row']")).ToHaveCountAsync(5);

        var filteredFirstInvoice = (await Page.Locator("[data-testid='invoice-ksef-number']").First.InnerTextAsync()).Trim();
        var filteredLastInvoice = (await Page.Locator("[data-testid='invoice-ksef-number']").Nth(4).InnerTextAsync()).Trim();
        Assert.That(filteredFirstInvoice, Is.EqualTo("E2E-KSEF-001"));
        Assert.That(filteredLastInvoice, Is.EqualTo("E2E-KSEF-005"));
    }

    private async Task SeedInvoiceListDataAsync()
    {
        await DatabaseFixture.CleanupAsync();

        var userId = DatabaseFixture.CreateTestUser("portal-e2e", "portal-e2e-password");
        var tenantId = await DatabaseFixture.CreateTestTenantAsync(userId, "1234567890", "Portal E2E Tenant");
        await DatabaseFixture.CreateTestCredentialAsync(tenantId, "encrypted-e2e-token");
        await DatabaseFixture.CreateTestInvoicesAsync(tenantId, 35);
    }
}
