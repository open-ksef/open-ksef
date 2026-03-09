using Microsoft.Playwright;
using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E.Invoices;

[Explicit("Requires running portal+keycloak and configured KEYCLOAK_USERNAME/KEYCLOAK_PASSWORD.")]
public sealed class InvoiceDetailsTests : BasePortalTest
{
    [Test]
    public async Task InvoiceDetails_FromList_ShowsMetadata_AndSupportsBackNavigation()
    {
        if (string.IsNullOrWhiteSpace(Options.Username) || string.IsNullOrWhiteSpace(Options.Password))
        {
            Assert.Ignore("KEYCLOAK_USERNAME / KEYCLOAK_PASSWORD are not configured for this environment.");
        }

        await SeedInvoiceDetailsDataAsync();

        await LoginWithKeycloakAsync();
        await NavigateToPortalAsync("/invoices");

        await Assertions.Expect(Page.Locator("[data-testid='invoice-table']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("[data-testid='invoice-view-details']").First).ToBeVisibleAsync();

        var selectedInvoiceNumber = (await Page.Locator("[data-testid='invoice-ksef-number']").First.InnerTextAsync()).Trim();
        await Page.Locator("[data-testid='invoice-view-details']").First.ClickAsync();
        await Page.WaitForURLAsync(url => url.Contains($"/invoices/{selectedInvoiceNumber}", StringComparison.OrdinalIgnoreCase));

        await Assertions.Expect(Page.Locator("[data-testid='invoice-details-card']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("[data-testid='invoice-detail-ksef-number']")).ToHaveTextAsync(selectedInvoiceNumber);
        await Assertions.Expect(Page.Locator("[data-testid='invoice-detail-vendor-name']")).ToHaveTextAsync("E2E Vendor");
        await Assertions.Expect(Page.Locator("[data-testid='invoice-detail-vendor-nip']")).ToHaveTextAsync("1234567890");
        await Assertions.Expect(Page.Locator("[data-testid='invoice-detail-issue-date']")).ToHaveTextAsync(DateTime.UtcNow.Date.ToString("yyyy-MM-dd"));
        await Assertions.Expect(Page.Locator("[data-testid='invoice-detail-amount']")).ToHaveTextAsync("100.00");
        await Assertions.Expect(Page.Locator("[data-testid='invoice-detail-currency']")).ToHaveTextAsync("PLN");

        await Page.ClickAsync("[data-testid='invoice-details-back-link']");
        await Page.WaitForURLAsync(url => url.Contains("/invoices", StringComparison.OrdinalIgnoreCase) &&
                                         !url.Contains("/invoices/E2E-KSEF-", StringComparison.OrdinalIgnoreCase));
        await Assertions.Expect(Page.Locator("[data-testid='invoice-table']")).ToBeVisibleAsync();
    }

    [Test]
    public async Task InvoiceDetails_DirectUrl_Loads_AndInvalidInvoiceShowsError()
    {
        if (string.IsNullOrWhiteSpace(Options.Username) || string.IsNullOrWhiteSpace(Options.Password))
        {
            Assert.Ignore("KEYCLOAK_USERNAME / KEYCLOAK_PASSWORD are not configured for this environment.");
        }

        await SeedInvoiceDetailsDataAsync();

        await LoginWithKeycloakAsync();
        await NavigateToPortalAsync("/invoices/E2E-KSEF-003");
        await Assertions.Expect(Page.Locator("[data-testid='invoice-details-card']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("[data-testid='invoice-detail-ksef-number']")).ToHaveTextAsync("E2E-KSEF-003");

        await NavigateToPortalAsync("/invoices/INVALID-123");
        await Assertions.Expect(Page.GetByText("Invoice not found")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText("The requested invoice could not be found.")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("[data-testid='invoice-details-back-link']")).ToBeVisibleAsync();
    }

    private async Task SeedInvoiceDetailsDataAsync()
    {
        await DatabaseFixture.CleanupAsync();

        var userId = DatabaseFixture.CreateTestUser("portal-e2e", "portal-e2e-password");
        var tenantId = await DatabaseFixture.CreateTestTenantAsync(userId, "1234567890", "Portal E2E Tenant");
        await DatabaseFixture.CreateTestCredentialAsync(tenantId, "encrypted-e2e-token");
        await DatabaseFixture.CreateTestInvoicesAsync(tenantId, 5);
    }
}
