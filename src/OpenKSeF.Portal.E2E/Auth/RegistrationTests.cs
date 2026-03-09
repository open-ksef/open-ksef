using Microsoft.Playwright;
using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E.Auth;

[Explicit("Requires running portal+keycloak+API with directAccessGrantsEnabled and service account configured.")]
public sealed class RegistrationTests : BasePortalTest
{
    private string _testEmail = null!;

    [SetUp]
    public void GenerateTestEmail()
    {
        _testEmail = $"e2e-reg-{Guid.NewGuid():N}@test.open-ksef.pl";
    }

    [Test]
    public async Task LoginPage_ShowsLoginFormByDefault()
    {
        await NavigateToPortalAsync("/login");

        await Assertions.Expect(Page.GetByTestId("login-email")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("login-password")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("login-submit")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("login-google")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("switch-to-register")).ToBeVisibleAsync();
    }

    [Test]
    public async Task LoginPage_SwitchToRegisterTab_ShowsRegistrationForm()
    {
        await NavigateToPortalAsync("/login");

        await Page.GetByTestId("switch-to-register").ClickAsync();

        await Assertions.Expect(Page.GetByTestId("register-email")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("register-password")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("register-confirm-password")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("register-first-name")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("register-last-name")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("register-submit")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("switch-to-login")).ToBeVisibleAsync();
    }

    [Test]
    public async Task LoginPage_SwitchBetweenTabs_PreservesContext()
    {
        await NavigateToPortalAsync("/login");

        await Assertions.Expect(Page.GetByTestId("login-email")).ToBeVisibleAsync();

        await Page.GetByTestId("switch-to-register").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("register-email")).ToBeVisibleAsync();

        await Page.GetByTestId("switch-to-login").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("login-email")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Registration_PasswordMismatch_ShowsValidationError()
    {
        await NavigateToPortalAsync("/login");
        await Page.GetByTestId("switch-to-register").ClickAsync();

        await Page.GetByTestId("register-email").FillAsync(_testEmail);
        await Page.GetByTestId("register-password").FillAsync("SecurePass1!");
        await Page.GetByTestId("register-confirm-password").FillAsync("DifferentPass!");
        await Page.GetByTestId("register-submit").ClickAsync();

        await Assertions.Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Hasła nie są identyczne");
    }

    [Test]
    public async Task Registration_PasswordTooShort_ShowsValidationError()
    {
        await NavigateToPortalAsync("/login");
        await Page.GetByTestId("switch-to-register").ClickAsync();

        await Page.GetByTestId("register-email").FillAsync(_testEmail);
        await Page.GetByTestId("register-password").FillAsync("short");
        await Page.GetByTestId("register-confirm-password").FillAsync("short");
        await Page.GetByTestId("register-submit").ClickAsync();

        await Assertions.Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("co najmniej 8 znaków");
    }

    [Test]
    public async Task Registration_ValidData_CreatesAccountAndLogsIn()
    {
        await NavigateToPortalAsync("/login");
        await Page.GetByTestId("switch-to-register").ClickAsync();

        await Page.GetByTestId("register-first-name").FillAsync("Test");
        await Page.GetByTestId("register-last-name").FillAsync("User");
        await Page.GetByTestId("register-email").FillAsync(_testEmail);
        await Page.GetByTestId("register-password").FillAsync("SecurePass1!");
        await Page.GetByTestId("register-confirm-password").FillAsync("SecurePass1!");
        await Page.GetByTestId("register-submit").ClickAsync();

        var portalBase = Options.PortalBaseUrl.TrimEnd('/');
        await Page.WaitForURLAsync(
            url => url.StartsWith(portalBase, StringComparison.OrdinalIgnoreCase)
                   && !url.Contains("/login", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        Assert.That(Page.Url, Does.Not.Contain("/login"));
    }

    [Test]
    public async Task Registration_DuplicateEmail_ShowsConflictError()
    {
        await NavigateToPortalAsync("/login");
        await Page.GetByTestId("switch-to-register").ClickAsync();

        await Page.GetByTestId("register-email").FillAsync(_testEmail);
        await Page.GetByTestId("register-password").FillAsync("SecurePass1!");
        await Page.GetByTestId("register-confirm-password").FillAsync("SecurePass1!");
        await Page.GetByTestId("register-submit").ClickAsync();

        var portalBase = Options.PortalBaseUrl.TrimEnd('/');
        await Page.WaitForURLAsync(
            url => !url.Contains("/login", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        await Context.ClearCookiesAsync();
        await Page.EvaluateAsync("() => { window.localStorage.clear(); window.sessionStorage.clear(); }");

        await NavigateToPortalAsync("/login");
        await Page.GetByTestId("switch-to-register").ClickAsync();

        await Page.GetByTestId("register-email").FillAsync(_testEmail);
        await Page.GetByTestId("register-password").FillAsync("AnotherPass2!");
        await Page.GetByTestId("register-confirm-password").FillAsync("AnotherPass2!");
        await Page.GetByTestId("register-submit").ClickAsync();

        await Assertions.Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync(
            new() { Timeout = 10_000 });
        await Assertions.Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("already exists");
    }

    [Test]
    public async Task DirectLogin_WithCredentials_AuthenticatesWithoutKeycloakRedirect()
    {
        if (string.IsNullOrWhiteSpace(Options.Username) || string.IsNullOrWhiteSpace(Options.Password))
        {
            Assert.Ignore("KEYCLOAK_USERNAME / KEYCLOAK_PASSWORD are not configured for this environment.");
        }

        await NavigateToPortalAsync("/login");

        await Page.GetByTestId("login-email").FillAsync(Options.Username!);
        await Page.GetByTestId("login-password").FillAsync(Options.Password!);
        await Page.GetByTestId("login-submit").ClickAsync();

        var portalBase = Options.PortalBaseUrl.TrimEnd('/');
        await Page.WaitForURLAsync(
            url => url.StartsWith(portalBase, StringComparison.OrdinalIgnoreCase)
                   && !url.Contains("/login", StringComparison.OrdinalIgnoreCase)
                   && !url.Contains("/realms/", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        Assert.That(Page.Url, Does.Not.Contain("/login"));
        Assert.That(Page.Url, Does.Not.Contain("/realms/"));
    }
}
