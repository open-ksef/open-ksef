using Microsoft.Playwright;
using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E.Auth;

public sealed class LoginTests : BasePortalTest
{
    [Test]
    [Explicit("Requires running portal+keycloak and configured KEYCLOAK_USERNAME/KEYCLOAK_PASSWORD.")]
    public async Task OidcLogin_SuccessfullyAuthenticatesUserAndReturnsToDashboard()
    {
        if (string.IsNullOrWhiteSpace(Options.Username) || string.IsNullOrWhiteSpace(Options.Password))
        {
            Assert.Ignore("KEYCLOAK_USERNAME / KEYCLOAK_PASSWORD are not configured for this environment.");
        }

        await NavigateToPortalAsync("/");
        await Page.WaitForURLAsync(url => url.Contains("/realms/openksef/", StringComparison.OrdinalIgnoreCase));

        Assert.That(Page.Url, Does.Contain("/realms/openksef/"));

        await Page.Locator("#username").FillAsync(Options.Username!);
        await Page.Locator("#password").FillAsync(Options.Password!);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign In" }).ClickAsync();

        var portalBase = Options.PortalBaseUrl.TrimEnd('/');
        await Page.WaitForURLAsync(
            url => url.StartsWith(portalBase, StringComparison.OrdinalIgnoreCase) &&
                   !url.Contains("/realms/", StringComparison.OrdinalIgnoreCase));

        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Logout" })).ToBeVisibleAsync();
    }
}
