using Microsoft.Playwright;
using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E.Credentials;

[Explicit("Requires running portal+keycloak and configured KEYCLOAK_USERNAME/KEYCLOAK_PASSWORD.")]
public sealed class CredentialTests : BasePortalTest
{
    [Test]
    public async Task CredentialCrud_CanAddUpdateAndDeleteCredential()
    {
        if (string.IsNullOrWhiteSpace(Options.Username) || string.IsNullOrWhiteSpace(Options.Password))
        {
            Assert.Ignore("KEYCLOAK_USERNAME / KEYCLOAK_PASSWORD are not configured for this environment.");
        }

        await LoginWithKeycloakAsync();
        await NavigateToPortalAsync("/credentials");

        await Page.ClickAsync("[data-testid='credential-add-button']");
        await Page.FillAsync("[data-testid='credential-token-input']", "token-abc");
        await Page.ClickAsync("[data-testid='credential-submit-button']");

        await Assertions.Expect(Page.Locator("text=Exists")).ToBeVisibleAsync();

        await Page.ClickAsync("[data-testid='credential-update-button']");
        await Page.FillAsync("[data-testid='credential-token-input']", "token-updated");
        await Page.ClickAsync("[data-testid='credential-submit-button']");

        await Assertions.Expect(Page.Locator("text=Exists")).ToBeVisibleAsync();

        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await Page.ClickAsync("[data-testid='credential-delete-button']");
    }
}
