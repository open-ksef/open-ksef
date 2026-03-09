using Microsoft.Playwright;
using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E.Dashboard;

[Explicit("Requires running portal+keycloak and configured KEYCLOAK_USERNAME/KEYCLOAK_PASSWORD.")]
public sealed class DashboardTests : BasePortalTest
{
    [Test]
    public async Task Dashboard_RendersTenantCards_WithSyncStatusAndCounts()
    {
        if (string.IsNullOrWhiteSpace(Options.Username) || string.IsNullOrWhiteSpace(Options.Password))
        {
            Assert.Ignore("KEYCLOAK_USERNAME / KEYCLOAK_PASSWORD are not configured for this environment.");
        }

        await LoginWithKeycloakAsync();
        await NavigateToPortalAsync("/");

        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("text=Total invoices:")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("[data-testid='dashboard-link-invoices']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("[data-testid='dashboard-link-tenants']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("[data-testid='dashboard-link-credentials']")).ToBeVisibleAsync();
    }
}
