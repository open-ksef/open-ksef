using Microsoft.Playwright;
using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E.Tenants;

[Explicit("Requires running portal+keycloak and configured KEYCLOAK_USERNAME/KEYCLOAK_PASSWORD.")]
public sealed class TenantCrudTests : BasePortalTest
{
    [Test]
    public async Task TenantCrud_CanCreateEditAndDeleteTenant()
    {
        if (string.IsNullOrWhiteSpace(Options.Username) || string.IsNullOrWhiteSpace(Options.Password))
        {
            Assert.Ignore("KEYCLOAK_USERNAME / KEYCLOAK_PASSWORD are not configured for this environment.");
        }

        await LoginWithKeycloakAsync();
        await NavigateToPortalAsync("/tenants");

        await Page.ClickAsync("[data-testid='tenant-create-button']");
        await Page.FillAsync("[data-testid='tenant-form-nip']", "5555555555");
        await Page.FillAsync("[data-testid='tenant-form-display-name']", "Tenant CRUD");
        await Page.ClickAsync("[data-testid='tenant-form-submit']");

        await Assertions.Expect(Page.Locator("text=Tenant CRUD")).ToBeVisibleAsync();

        await Page.ClickAsync("[data-testid='tenant-edit-button']");
        await Page.FillAsync("[data-testid='tenant-form-display-name']", "Tenant CRUD Updated");
        await Page.ClickAsync("[data-testid='tenant-form-submit']");

        await Assertions.Expect(Page.Locator("text=Tenant CRUD Updated")).ToBeVisibleAsync();

        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await Page.ClickAsync("[data-testid='tenant-delete-button']");
    }
}
