using Microsoft.Playwright;
using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E.Auth;

[Explicit("Requires running portal+keycloak and configured KEYCLOAK_USERNAME/KEYCLOAK_PASSWORD for relogin scenario.")]
public sealed class AuthorizationTests : BasePortalTest
{
    [Test]
    public async Task ProtectedRoute_WithoutAuthentication_RedirectsToLoginFlow()
    {
        await NavigateToPortalAsync("/invoices");

        await Page.WaitForURLAsync(IsLoginFlowUrl);
        Assert.That(Page.Url, Does.Contain("/realms/openksef/").Or.Contain("/login"));

        // Ensure protected table data is not rendered for unauthenticated session.
        await Assertions.Expect(Page.Locator("[data-testid='invoice-table']")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task ProtectedRoute_AfterSessionClear_RequiresReauthentication()
    {
        if (string.IsNullOrWhiteSpace(Options.Username) || string.IsNullOrWhiteSpace(Options.Password))
        {
            Assert.Ignore("KEYCLOAK_USERNAME / KEYCLOAK_PASSWORD are not configured for this environment.");
        }

        await LoginWithKeycloakAsync();
        await NavigateToPortalAsync("/invoices");
        await Assertions.Expect(Page.Locator("[data-testid='invoice-table']")).ToBeVisibleAsync();

        await Context.ClearCookiesAsync();
        await Page.EvaluateAsync("() => { window.localStorage.clear(); window.sessionStorage.clear(); }");
        await NavigateToPortalAsync("/invoices");

        await Page.WaitForURLAsync(IsLoginFlowUrl);
        Assert.That(Page.Url, Does.Contain("/realms/openksef/").Or.Contain("/login"));
        await Assertions.Expect(Page.Locator("[data-testid='invoice-table']")).ToHaveCountAsync(0);
    }

    private static bool IsLoginFlowUrl(string url) =>
        url.Contains("/realms/openksef/", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("/login", StringComparison.OrdinalIgnoreCase);
}
