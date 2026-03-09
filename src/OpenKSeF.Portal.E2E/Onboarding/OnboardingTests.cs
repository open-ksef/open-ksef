using Microsoft.Playwright;
using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E.Onboarding;

[Explicit("Requires running portal+keycloak+API with directAccessGrantsEnabled and service account configured.")]
public sealed class OnboardingTests : BasePortalTest
{
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
    public async Task NewUser_RedirectedToOnboarding()
    {
        await DatabaseFixture.CleanupAsync();

        await LoginViaDirectFormAsync();

        var portalBase = Options.PortalBaseUrl.TrimEnd('/');
        await Page.WaitForURLAsync(
            url => url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        Assert.That(Page.Url, Does.Contain("/onboarding"));

        await Assertions.Expect(Page.GetByTestId("onboarding-step-indicator")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("onboarding-nip")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("onboarding-display-name")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("onboarding-email")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Onboarding_FullFlow_WithToken()
    {
        await DatabaseFixture.CleanupAsync();

        await LoginViaDirectFormAsync();

        await Page.WaitForURLAsync(
            url => url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        // Step 1: Fill company details
        await Page.GetByTestId("onboarding-nip").FillAsync("9999999999");
        await Page.GetByTestId("onboarding-display-name").FillAsync("E2E Test Company");
        await Page.GetByTestId("onboarding-email").FillAsync("e2e@test.open-ksef.pl");
        await Page.GetByTestId("onboarding-next").ClickAsync();

        // Step 2: Verify instruction panel and enter token
        await Assertions.Expect(Page.GetByTestId("onboarding-instruction")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });
        await Assertions.Expect(Page.GetByTestId("onboarding-token")).ToBeVisibleAsync();

        var testToken = Environment.GetEnvironmentVariable("E2E_TEST_KSEF_TOKEN") ?? "e2e-test-token-placeholder";
        await Page.GetByTestId("onboarding-token").FillAsync(testToken);
        await Page.GetByTestId("onboarding-next").ClickAsync();

        // Step 3: Confirm success
        await Assertions.Expect(Page.GetByTestId("onboarding-success")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });

        var successText = await Page.GetByTestId("onboarding-success").TextContentAsync();
        Assert.That(successText, Does.Contain("E2E Test Company"));
        Assert.That(successText, Does.Contain("9999999999"));

        // Navigate to dashboard
        await Page.GetByTestId("onboarding-go-dashboard").ClickAsync();

        var portalBase = Options.PortalBaseUrl.TrimEnd('/');
        await Page.WaitForURLAsync(
            url => url.StartsWith(portalBase, StringComparison.OrdinalIgnoreCase)
                   && !url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 10_000 });

        Assert.That(Page.Url, Does.Not.Contain("/onboarding"));
    }

    [Test]
    public async Task Onboarding_SkipToken()
    {
        await DatabaseFixture.CleanupAsync();

        await LoginViaDirectFormAsync();

        await Page.WaitForURLAsync(
            url => url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        // Step 1: Fill company details
        await Page.GetByTestId("onboarding-nip").FillAsync("8888888888");
        await Page.GetByTestId("onboarding-display-name").FillAsync("Skip Token Co");
        await Page.GetByTestId("onboarding-next").ClickAsync();

        // Step 2: Skip token
        await Assertions.Expect(Page.GetByTestId("onboarding-skip-token")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });
        await Page.GetByTestId("onboarding-skip-token").ClickAsync();

        // Step 3: Success with warning about missing token
        await Assertions.Expect(Page.GetByTestId("onboarding-success")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });

        var successText = await Page.GetByTestId("onboarding-success").TextContentAsync();
        Assert.That(successText, Does.Contain("Token KSeF nie został dodany"));

        // Navigate to dashboard
        await Page.GetByTestId("onboarding-go-dashboard").ClickAsync();

        await Page.WaitForURLAsync(
            url => !url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 10_000 });

        Assert.That(Page.Url, Does.Not.Contain("/onboarding"));
    }

    [Test]
    public async Task OnboardedUser_BypassesWizard()
    {
        await DatabaseFixture.CleanupAsync();
        await DatabaseFixture.SeedDefaultDataAsync();

        await LoginViaDirectFormAsync();

        var portalBase = Options.PortalBaseUrl.TrimEnd('/');
        await Page.WaitForURLAsync(
            url => url.StartsWith(portalBase, StringComparison.OrdinalIgnoreCase)
                   && !url.Contains("/login", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        Assert.That(Page.Url, Does.Not.Contain("/onboarding"));

        // Try navigating directly to /onboarding
        await NavigateToPortalAsync("/onboarding");
        await Page.WaitForURLAsync(
            url => url.StartsWith(portalBase, StringComparison.OrdinalIgnoreCase)
                   && !url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 10_000 });

        Assert.That(Page.Url, Does.Not.Contain("/onboarding"));
    }

    [Test]
    public async Task Onboarding_Step1_NipValidation()
    {
        await DatabaseFixture.CleanupAsync();

        await LoginViaDirectFormAsync();

        await Page.WaitForURLAsync(
            url => url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        // Submit with invalid NIP
        await Page.GetByTestId("onboarding-nip").FillAsync("123");
        await Page.GetByTestId("onboarding-next").ClickAsync();

        await Assertions.Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("NIP musi zawierać dokładnie 10 cyfr");

        // Still on step 1
        await Assertions.Expect(Page.GetByTestId("onboarding-nip")).ToBeVisibleAsync();
    }
}
