using Microsoft.Playwright;
using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E.Devices;

[Explicit("Requires running portal+keycloak and configured KEYCLOAK_USERNAME/KEYCLOAK_PASSWORD.")]
public sealed class DeviceTests : BasePortalTest
{
    [Test]
    public async Task DeviceCrud_CanRegisterAndUnregisterDevice()
    {
        if (string.IsNullOrWhiteSpace(Options.Username) || string.IsNullOrWhiteSpace(Options.Password))
        {
            Assert.Ignore("KEYCLOAK_USERNAME / KEYCLOAK_PASSWORD are not configured for this environment.");
        }

        await LoginWithKeycloakAsync();
        await NavigateToPortalAsync("/devices");

        await Page.ClickAsync("[data-testid='device-register-button']");
        await Page.FillAsync("[data-testid='device-form-token']", "device-token-e2e");
        await Page.SelectOptionAsync("[data-testid='device-form-platform']", "Web");
        await Page.ClickAsync("[data-testid='device-form-submit']");

        await Assertions.Expect(Page.Locator("text=Web")).ToBeVisibleAsync();

        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await Page.ClickAsync("[data-testid='device-unregister-button']");
    }
}
