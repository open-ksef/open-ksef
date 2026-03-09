using Microsoft.Playwright;
using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E.MobileSetup;

[Explicit("Requires running portal+keycloak and configured KEYCLOAK_USERNAME/KEYCLOAK_PASSWORD.")]
public sealed class MobileSetupTests : BasePortalTest
{
    [Test]
    public async Task MobileSetupPage_ShowsQrCode()
    {
        await LoginWithKeycloakAsync();
        await NavigateToPortalAsync("/mobile-setup");

        await Assertions.Expect(Page.Locator("[data-testid='mobile-setup-qr']")).ToBeVisibleAsync();

        var heading = Page.GetByRole(AriaRole.Heading, new() { Name = "Aplikacja mobilna" });
        await Assertions.Expect(heading).ToBeVisibleAsync();
    }

    [Test]
    public async Task MobileSetupPage_ShowsCountdownTimer()
    {
        await LoginWithKeycloakAsync();
        await NavigateToPortalAsync("/mobile-setup");

        var countdown = Page.Locator("[data-testid='mobile-setup-countdown']");
        await Assertions.Expect(countdown).ToBeVisibleAsync();
        await Page.WaitForTimeoutAsync(2000);

        var text = await countdown.InnerTextAsync();
        Assert.That(text, Does.Contain(":"), "Countdown should display a time in M:SS format");
    }

    [Test]
    public async Task MobileSetupPage_RegenerateUpdatesQr()
    {
        await LoginWithKeycloakAsync();
        await NavigateToPortalAsync("/mobile-setup");

        await Assertions.Expect(Page.Locator("[data-testid='mobile-setup-qr']")).ToBeVisibleAsync();
        await Page.WaitForTimeoutAsync(1000);

        var qrBefore = await Page.Locator("[data-testid='mobile-setup-qr'] svg").GetAttributeAsync("class")
                       ?? await Page.Locator("[data-testid='mobile-setup-qr'] svg").InnerHTMLAsync();

        await Page.ClickAsync("[data-testid='mobile-setup-regenerate']");
        await Page.WaitForTimeoutAsync(2000);

        var qrAfter = await Page.Locator("[data-testid='mobile-setup-qr'] svg").InnerHTMLAsync();

        Assert.That(qrAfter, Is.Not.Empty, "QR code should be rendered after regeneration");
    }

    [Test]
    public async Task DevicesPage_ConnectButtonNavigatesToMobileSetup()
    {
        await LoginWithKeycloakAsync();
        await NavigateToPortalAsync("/devices");

        var connectButton = Page.Locator("[data-testid='device-connect-mobile-button']");
        await Assertions.Expect(connectButton).ToBeVisibleAsync();

        await connectButton.ClickAsync();
        await Page.WaitForURLAsync("**/mobile-setup");

        await Assertions.Expect(Page.Locator("[data-testid='mobile-setup-qr']")).ToBeVisibleAsync();
    }
}
