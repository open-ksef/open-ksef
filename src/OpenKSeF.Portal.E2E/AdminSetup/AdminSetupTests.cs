using Microsoft.Playwright;
using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E.AdminSetup;

[Explicit("Requires running portal+keycloak+API with clean system_config table (no dev-env-up provisioning).")]
public sealed class AdminSetupTests : BasePortalTest
{
    [Test]
    public async Task FreshSystem_ShowsSetupWizard()
    {
        await DatabaseFixture.CleanupAsync();

        await NavigateToPortalAsync("/");

        await Page.WaitForURLAsync(
            url => url.Contains("/admin-setup", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        Assert.That(Page.Url, Does.Contain("/admin-setup"));

        await Assertions.Expect(Page.GetByTestId("admin-setup-step-indicator")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("setup-kc-user")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("setup-kc-pass")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AdminSetup_FullFlow_MinimalConfig()
    {
        await DatabaseFixture.CleanupAsync();

        await NavigateToPortalAsync("/admin-setup");

        // Step 1: Keycloak admin login
        await Assertions.Expect(Page.GetByTestId("setup-kc-user")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-kc-user").FillAsync(Options.KeycloakAdminUsername);
        await Page.GetByTestId("setup-kc-pass").FillAsync(Options.KeycloakAdminPassword);
        await Page.GetByTestId("setup-next").ClickAsync();

        // Step 2: Base config
        await Assertions.Expect(Page.GetByTestId("setup-url")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        // URL pre-filled, KSeF defaults to Test
        await Page.GetByTestId("setup-admin-email").FillAsync("admin@e2e-test.pl");
        await Page.GetByTestId("setup-admin-pass").FillAsync("Test1234!");
        await Page.GetByTestId("setup-next").ClickAsync();

        // Step 3: Auth & Email (defaults, skip SMTP)
        await Assertions.Expect(Page.GetByTestId("setup-pass-policy")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-next").ClickAsync();

        // Step 4: Security (auto-generated)
        await Assertions.Expect(Page.GetByTestId("setup-next")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-next").ClickAsync();

        // Step 5: Integrations (skip all)
        await Assertions.Expect(Page.GetByTestId("setup-google-id")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-next").ClickAsync();

        // Step 6: Summary + Apply
        await Assertions.Expect(Page.GetByTestId("setup-apply")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-apply").ClickAsync();

        // Wait for success
        await Assertions.Expect(Page.GetByTestId("setup-success")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Verify generated values shown
        var successText = await Page.GetByTestId("setup-success").TextContentAsync();
        Assert.That(successText, Does.Contain("admin@e2e-test.pl"));

        // Navigate to login
        await Page.GetByTestId("setup-go-login").ClickAsync();
        await Page.WaitForURLAsync(
            url => url.Contains("/login", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 10_000 });

        Assert.That(Page.Url, Does.Contain("/login"));

        // Verify DB: system_config has is_initialized=true
        await using var db = DatabaseFixture.CreateDbContext();
        var isInit = await db.SystemConfigs.FindAsync("is_initialized");
        Assert.That(isInit, Is.Not.Null);
        Assert.That(isInit!.Value, Is.EqualTo("true"));

        var encKey = await db.SystemConfigs.FindAsync("encryption_key");
        Assert.That(encKey, Is.Not.Null);
        Assert.That(encKey!.Value, Is.Not.Empty);

        var apiSecret = await db.SystemConfigs.FindAsync("api_client_secret");
        Assert.That(apiSecret, Is.Not.Null);
        Assert.That(apiSecret!.Value, Is.Not.Empty);
    }

    [Test]
    public async Task AdminSetup_InvalidAdminCreds_ShowsError()
    {
        await DatabaseFixture.CleanupAsync();

        await NavigateToPortalAsync("/admin-setup");

        await Assertions.Expect(Page.GetByTestId("setup-kc-user")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-kc-user").FillAsync("admin");
        await Page.GetByTestId("setup-kc-pass").FillAsync("wrong-password");
        await Page.GetByTestId("setup-next").ClickAsync();

        // Should show error and stay on step 1
        await Assertions.Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Still on step 1
        await Assertions.Expect(Page.GetByTestId("setup-kc-user")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AdminSetup_AlreadyInitialized_RedirectsToLogin()
    {
        await DatabaseFixture.CleanupAsync();
        await DatabaseFixture.SeedSystemInitializedAsync();
        await DatabaseFixture.SeedDefaultDataAsync();

        await NavigateToPortalAsync("/");

        // Should NOT show the wizard since system is initialized
        // Instead, should end up at login (unauthenticated) or dashboard
        await Page.WaitForURLAsync(
            url => !url.Contains("/admin-setup", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        Assert.That(Page.Url, Does.Not.Contain("/admin-setup"));
    }

    [Test]
    public async Task AdminSetup_Step2_ValidationErrors()
    {
        await DatabaseFixture.CleanupAsync();

        await NavigateToPortalAsync("/admin-setup");

        // Step 1: Login
        await Assertions.Expect(Page.GetByTestId("setup-kc-user")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-kc-user").FillAsync(Options.KeycloakAdminUsername);
        await Page.GetByTestId("setup-kc-pass").FillAsync(Options.KeycloakAdminPassword);
        await Page.GetByTestId("setup-next").ClickAsync();

        // Step 2: Submit with empty email
        await Assertions.Expect(Page.GetByTestId("setup-admin-email")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-admin-pass").FillAsync("Test1234!");
        await Page.GetByTestId("setup-next").ClickAsync();

        // Should show validation error
        await Assertions.Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync(new() { Timeout = 5_000 });

        // Still on step 2
        await Assertions.Expect(Page.GetByTestId("setup-admin-email")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AdminSetup_FullFlow_WithSmtp()
    {
        await DatabaseFixture.CleanupAsync();

        await NavigateToPortalAsync("/admin-setup");

        // Step 1: Login
        await Assertions.Expect(Page.GetByTestId("setup-kc-user")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-kc-user").FillAsync(Options.KeycloakAdminUsername);
        await Page.GetByTestId("setup-kc-pass").FillAsync(Options.KeycloakAdminPassword);
        await Page.GetByTestId("setup-next").ClickAsync();

        // Step 2: Base config
        await Assertions.Expect(Page.GetByTestId("setup-url")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-admin-email").FillAsync("smtp-admin@e2e-test.pl");
        await Page.GetByTestId("setup-admin-pass").FillAsync("Test1234!");
        await Page.GetByTestId("setup-next").ClickAsync();

        // Step 3: Enable SMTP
        await Assertions.Expect(Page.GetByTestId("setup-pass-policy")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        // Enable SMTP by clicking the checkbox (it's labeled "Skonfiguruj serwer SMTP")
        var smtpCheckbox = Page.Locator("input[type='checkbox']").Last;
        await smtpCheckbox.CheckAsync();

        await Assertions.Expect(Page.GetByTestId("setup-smtp-host")).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await Page.GetByTestId("setup-smtp-host").FillAsync("smtp.test.local");
        await Page.GetByTestId("setup-smtp-from").FillAsync("test@e2e.pl");
        await Page.GetByTestId("setup-next").ClickAsync();

        // Step 4: Security
        await Page.GetByTestId("setup-next").ClickAsync();

        // Step 5: Skip integrations
        await Assertions.Expect(Page.GetByTestId("setup-google-id")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-next").ClickAsync();

        // Step 6: Apply
        await Assertions.Expect(Page.GetByTestId("setup-apply")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-apply").ClickAsync();

        await Assertions.Expect(Page.GetByTestId("setup-success")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Test]
    public async Task AdminSetup_FullFlow_WithGoogleOAuth()
    {
        await DatabaseFixture.CleanupAsync();

        await NavigateToPortalAsync("/admin-setup");

        // Step 1: Login
        await Assertions.Expect(Page.GetByTestId("setup-kc-user")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-kc-user").FillAsync(Options.KeycloakAdminUsername);
        await Page.GetByTestId("setup-kc-pass").FillAsync(Options.KeycloakAdminPassword);
        await Page.GetByTestId("setup-next").ClickAsync();

        // Step 2
        await Assertions.Expect(Page.GetByTestId("setup-url")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-admin-email").FillAsync("google-admin@e2e-test.pl");
        await Page.GetByTestId("setup-admin-pass").FillAsync("Test1234!");
        await Page.GetByTestId("setup-next").ClickAsync();

        // Step 3
        await Assertions.Expect(Page.GetByTestId("setup-pass-policy")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-next").ClickAsync();

        // Step 4
        await Page.GetByTestId("setup-next").ClickAsync();

        // Step 5: Add Google OAuth
        await Assertions.Expect(Page.GetByTestId("setup-google-id")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-google-id").FillAsync("e2e-fake-client-id.apps.googleusercontent.com");
        await Page.GetByTestId("setup-google-secret").FillAsync("e2e-fake-secret");
        await Page.GetByTestId("setup-next").ClickAsync();

        // Step 6: Apply
        await Assertions.Expect(Page.GetByTestId("setup-apply")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.GetByTestId("setup-apply").ClickAsync();

        await Assertions.Expect(Page.GetByTestId("setup-success")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Verify Google credentials saved
        await using var db = DatabaseFixture.CreateDbContext();
        var googleId = await db.SystemConfigs.FindAsync("google_client_id");
        Assert.That(googleId, Is.Not.Null);
        Assert.That(googleId!.Value, Is.EqualTo("e2e-fake-client-id.apps.googleusercontent.com"));
    }
}
