using Microsoft.Playwright;
using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E.Credentials;

[Explicit("Requires running portal+keycloak+API with directAccessGrantsEnabled and service account configured.")]
public sealed class CertificateCredentialTests : BasePortalTest
{
    private static readonly string TestPfxPath = Path.Combine(
        AppContext.BaseDirectory, "TestData", "test-certificate.pfx");

    private const string TestPfxPassword = "Test1234!";

    private async Task LoginViaDirectFormAsync()
    {
        if (string.IsNullOrWhiteSpace(Options.Username) || string.IsNullOrWhiteSpace(Options.Password))
        {
            Assert.Ignore("KEYCLOAK_USERNAME / KEYCLOAK_PASSWORD are not configured for this environment.");
        }

        await NavigateToPortalAsync("/login");
        await Page.GetByTestId("login-email").FillAsync(Options.Username!);
        await Page.GetByTestId("login-password").FillAsync(Options.Password!);
        await Page.GetByTestId("login-submit").ClickAsync();
    }

    [Test]
    public async Task Onboarding_FullFlow_WithCertificate()
    {
        Assert.That(File.Exists(TestPfxPath), Is.True, $"Test PFX not found at {TestPfxPath}");
        await DatabaseFixture.CleanupAsync();

        await LoginViaDirectFormAsync();

        await Page.WaitForURLAsync(
            url => url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        // Step 1: Fill company details
        await Page.GetByTestId("onboarding-nip").FillAsync("7777777777");
        await Page.GetByTestId("onboarding-display-name").FillAsync("Certificate Co");
        await Page.GetByTestId("onboarding-email").FillAsync("cert@test.open-ksef.pl");
        await Page.GetByTestId("onboarding-next").ClickAsync();

        // Step 2: Select certificate type
        await Assertions.Expect(Page.GetByTestId("onboarding-credential-type")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });
        await Page.GetByTestId("onboarding-credential-type-certificate").ClickAsync();

        // Upload PFX file
        var fileInput = Page.GetByTestId("onboarding-certificate-file");
        await Assertions.Expect(fileInput).ToBeVisibleAsync();
        await fileInput.SetInputFilesAsync(TestPfxPath);

        // Fill password
        await Page.GetByTestId("onboarding-certificate-password").FillAsync(TestPfxPassword);
        await Page.GetByTestId("onboarding-next").ClickAsync();

        // Step 3: Confirm success
        await Assertions.Expect(Page.GetByTestId("onboarding-success")).ToBeVisibleAsync(
            new() { Timeout = 15_000 });

        var successText = await Page.GetByTestId("onboarding-success").TextContentAsync();
        Assert.That(successText, Does.Contain("Certificate Co"));
        Assert.That(successText, Does.Contain("7777777777"));

        await Page.GetByTestId("onboarding-go-dashboard").ClickAsync();

        var portalBase = Options.PortalBaseUrl.TrimEnd('/');
        await Page.WaitForURLAsync(
            url => url.StartsWith(portalBase, StringComparison.OrdinalIgnoreCase)
                   && !url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 10_000 });

        Assert.That(Page.Url, Does.Not.Contain("/onboarding"));
    }

    [Test]
    public async Task Onboarding_TokenDeprecationBanner_IsVisible()
    {
        await DatabaseFixture.CleanupAsync();

        await LoginViaDirectFormAsync();

        await Page.WaitForURLAsync(
            url => url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        // Step 1: Fill and advance
        await Page.GetByTestId("onboarding-nip").FillAsync("6666666666");
        await Page.GetByTestId("onboarding-display-name").FillAsync("Deprecation Banner Co");
        await Page.GetByTestId("onboarding-next").ClickAsync();

        // Step 2: Token type is selected by default
        await Assertions.Expect(Page.GetByTestId("onboarding-credential-type")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });

        // Verify deprecation banner is visible
        var banner = Page.GetByTestId("onboarding-token-deprecation-warning");
        await Assertions.Expect(banner).ToBeVisibleAsync();

        var bannerText = await banner.TextContentAsync();
        Assert.That(bannerText, Does.Contain("2027"));
        Assert.That(bannerText, Does.Contain("tokeny autoryzacyjne"));

        // Switch to certificate -- banner should disappear
        await Page.GetByTestId("onboarding-credential-type-certificate").ClickAsync();
        await Assertions.Expect(banner).Not.ToBeVisibleAsync();

        // Switch back to token -- banner should reappear
        await Page.GetByTestId("onboarding-credential-type-token").ClickAsync();
        await Assertions.Expect(banner).ToBeVisibleAsync();
    }

    [Test]
    public async Task CredentialList_ShowsCredentialType()
    {
        await DatabaseFixture.CleanupAsync();
        await DatabaseFixture.SeedDefaultDataAsync();

        await LoginViaDirectFormAsync();

        var portalBase = Options.PortalBaseUrl.TrimEnd('/');
        await Page.WaitForURLAsync(
            url => url.StartsWith(portalBase, StringComparison.OrdinalIgnoreCase)
                   && !url.Contains("/login", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        await NavigateToPortalAsync("/credentials");

        // Wait for table to load
        await Assertions.Expect(Page.GetByTestId("credential-table")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });

        // Verify type badge is visible (default seeded credential is Token type)
        var badges = Page.GetByTestId("credential-type-badge");
        await Assertions.Expect(badges.First).ToBeVisibleAsync();
        var badgeText = await badges.First.TextContentAsync();
        Assert.That(badgeText, Does.Contain("Token"));

        // Verify deprecation warning for token-type rows
        var deprecation = Page.GetByTestId("credential-token-deprecation");
        await Assertions.Expect(deprecation.First).ToBeVisibleAsync();
    }

    [Test]
    public async Task CredentialCrud_CanAddCertificateCredential()
    {
        Assert.That(File.Exists(TestPfxPath), Is.True, $"Test PFX not found at {TestPfxPath}");
        await DatabaseFixture.CleanupAsync();
        await DatabaseFixture.SeedDefaultDataAsync();

        await LoginViaDirectFormAsync();

        var portalBase = Options.PortalBaseUrl.TrimEnd('/');
        await Page.WaitForURLAsync(
            url => url.StartsWith(portalBase, StringComparison.OrdinalIgnoreCase)
                   && !url.Contains("/login", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        await NavigateToPortalAsync("/credentials");

        await Assertions.Expect(Page.GetByTestId("credential-table")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });

        // Click update button on existing row
        await Page.GetByTestId("credential-update-button").First.ClickAsync();

        // Switch to certificate type in modal
        var typeSelect = Page.GetByTestId("credential-type-select");
        await Assertions.Expect(typeSelect).ToBeVisibleAsync();

        // Click certificate option in the modal type selector
        await typeSelect.Locator("button").Nth(1).ClickAsync();

        // Upload PFX
        await Page.GetByTestId("credential-certificate-file").SetInputFilesAsync(TestPfxPath);
        await Page.GetByTestId("credential-certificate-password").FillAsync(TestPfxPassword);
        await Page.GetByTestId("credential-submit-button").ClickAsync();

        // Wait for modal to close and verify type badge changed to Certyfikat
        await Assertions.Expect(Page.GetByTestId("credential-type-badge").First).ToContainTextAsync(
            "Certyfikat",
            new() { Timeout = 10_000 });

        // Delete the credential
        await Page.GetByTestId("credential-delete-button").First.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Usuń dane logowania" }).ClickAsync();

        // Wait for deletion to process
        await Page.WaitForTimeoutAsync(2_000);
    }

    [Test]
    public async Task Onboarding_FullFlow_WithToken_Regression()
    {
        await DatabaseFixture.CleanupAsync();

        await LoginViaDirectFormAsync();

        await Page.WaitForURLAsync(
            url => url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        // Step 1: Fill company details
        await Page.GetByTestId("onboarding-nip").FillAsync("5555555555");
        await Page.GetByTestId("onboarding-display-name").FillAsync("Token Regression Co");
        await Page.GetByTestId("onboarding-email").FillAsync("regression@test.open-ksef.pl");
        await Page.GetByTestId("onboarding-next").ClickAsync();

        // Step 2: Token type is selected by default -- enter token
        await Assertions.Expect(Page.GetByTestId("onboarding-credential-type")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });
        await Assertions.Expect(Page.GetByTestId("onboarding-token")).ToBeVisibleAsync();

        var testToken = Environment.GetEnvironmentVariable("E2E_TEST_KSEF_TOKEN") ?? "e2e-regression-token";
        await Page.GetByTestId("onboarding-token").FillAsync(testToken);
        await Page.GetByTestId("onboarding-next").ClickAsync();

        // Step 3: Confirm success
        await Assertions.Expect(Page.GetByTestId("onboarding-success")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });

        var successText = await Page.GetByTestId("onboarding-success").TextContentAsync();
        Assert.That(successText, Does.Contain("Token Regression Co"));
        Assert.That(successText, Does.Contain("5555555555"));

        await Page.GetByTestId("onboarding-go-dashboard").ClickAsync();

        var portalBase = Options.PortalBaseUrl.TrimEnd('/');
        await Page.WaitForURLAsync(
            url => url.StartsWith(portalBase, StringComparison.OrdinalIgnoreCase)
                   && !url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 10_000 });

        Assert.That(Page.Url, Does.Not.Contain("/onboarding"));
    }
}
