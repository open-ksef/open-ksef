using Microsoft.Playwright;
using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E.Credentials;

[Explicit("Requires running portal+keycloak+API with directAccessGrantsEnabled and test certificates.")]
public sealed class PemCertificateCredentialTests : BasePortalTest
{
    private static readonly string TestCrtPath = Path.Combine(
        AppContext.BaseDirectory, "TestData", "TestOpenKSeF.crt");
    private static readonly string TestKeyPath = Path.Combine(
        AppContext.BaseDirectory, "TestData", "TestOpenKSeF.key");

    private const string TestKeyPassword = "test!@#123TESTaaaa";

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
    public async Task Onboarding_FullFlow_WithPemCertificate()
    {
        Assert.That(File.Exists(TestCrtPath), Is.True, $"Test CRT not found at {TestCrtPath}");
        Assert.That(File.Exists(TestKeyPath), Is.True, $"Test KEY not found at {TestKeyPath}");
        await DatabaseFixture.CleanupAsync();

        await LoginViaDirectFormAsync();

        await Page.WaitForURLAsync(
            url => url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        // Step 1: Fill company details
        await Page.GetByTestId("onboarding-nip").FillAsync("8888888888");
        await Page.GetByTestId("onboarding-display-name").FillAsync("PEM Certificate Co");
        await Page.GetByTestId("onboarding-email").FillAsync("pem@test.open-ksef.pl");
        await Page.GetByTestId("onboarding-next").ClickAsync();

        // Step 2: Select certificate type
        await Assertions.Expect(Page.GetByTestId("onboarding-credential-type")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });
        await Page.GetByTestId("onboarding-credential-type-certificate").ClickAsync();

        // CRT+KEY format should be selected by default
        var pemToggle = Page.GetByTestId("onboarding-cert-format-pem");
        await Assertions.Expect(pemToggle).ToBeVisibleAsync();

        // Upload CRT file
        var certFileInput = Page.GetByTestId("onboarding-certificate-file");
        await Assertions.Expect(certFileInput).ToBeVisibleAsync();
        await certFileInput.SetInputFilesAsync(TestCrtPath);

        // Upload KEY file
        var keyFileInput = Page.GetByTestId("onboarding-key-file");
        await Assertions.Expect(keyFileInput).ToBeVisibleAsync();
        await keyFileInput.SetInputFilesAsync(TestKeyPath);

        // Fill private key password
        await Page.GetByTestId("onboarding-certificate-password").FillAsync(TestKeyPassword);
        await Page.GetByTestId("onboarding-next").ClickAsync();

        // Step 3: Confirm success
        await Assertions.Expect(Page.GetByTestId("onboarding-success")).ToBeVisibleAsync(
            new() { Timeout = 15_000 });

        var successText = await Page.GetByTestId("onboarding-success").TextContentAsync();
        Assert.That(successText, Does.Contain("PEM Certificate Co"));
        Assert.That(successText, Does.Contain("8888888888"));

        await Page.GetByTestId("onboarding-go-dashboard").ClickAsync();

        var portalBase = Options.PortalBaseUrl.TrimEnd('/');
        await Page.WaitForURLAsync(
            url => url.StartsWith(portalBase, StringComparison.OrdinalIgnoreCase)
                   && !url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 10_000 });

        Assert.That(Page.Url, Does.Not.Contain("/onboarding"));
    }

    [Test]
    public async Task Onboarding_PemFormatToggle_SwitchesBetweenFormats()
    {
        await DatabaseFixture.CleanupAsync();

        await LoginViaDirectFormAsync();

        await Page.WaitForURLAsync(
            url => url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        // Step 1: Fill and advance
        await Page.GetByTestId("onboarding-nip").FillAsync("4444444444");
        await Page.GetByTestId("onboarding-display-name").FillAsync("Format Toggle Co");
        await Page.GetByTestId("onboarding-next").ClickAsync();

        // Step 2: Select certificate
        await Assertions.Expect(Page.GetByTestId("onboarding-credential-type")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });
        await Page.GetByTestId("onboarding-credential-type-certificate").ClickAsync();

        // CRT+KEY mode (default) -- key file input should be visible
        await Assertions.Expect(Page.GetByTestId("onboarding-key-file")).ToBeVisibleAsync();

        // Switch to PFX mode
        await Page.GetByTestId("onboarding-cert-format-pfx").ClickAsync();

        // Key file input should be hidden in PFX mode
        await Assertions.Expect(Page.GetByTestId("onboarding-key-file")).Not.ToBeVisibleAsync();

        // Certificate file input should still be visible
        await Assertions.Expect(Page.GetByTestId("onboarding-certificate-file")).ToBeVisibleAsync();

        // Switch back to CRT+KEY mode
        await Page.GetByTestId("onboarding-cert-format-pem").ClickAsync();

        // Key file input should reappear
        await Assertions.Expect(Page.GetByTestId("onboarding-key-file")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Onboarding_PemCertificate_WrongPassword_ShowsToastError()
    {
        Assert.That(File.Exists(TestCrtPath), Is.True, $"Test CRT not found at {TestCrtPath}");
        Assert.That(File.Exists(TestKeyPath), Is.True, $"Test KEY not found at {TestKeyPath}");
        await DatabaseFixture.CleanupAsync();

        await LoginViaDirectFormAsync();

        await Page.WaitForURLAsync(
            url => url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        // Step 1
        await Page.GetByTestId("onboarding-nip").FillAsync("3333333333");
        await Page.GetByTestId("onboarding-display-name").FillAsync("Wrong Password Co");
        await Page.GetByTestId("onboarding-next").ClickAsync();

        // Step 2: Select certificate, upload CRT+KEY with wrong password
        await Assertions.Expect(Page.GetByTestId("onboarding-credential-type")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });
        await Page.GetByTestId("onboarding-credential-type-certificate").ClickAsync();

        await Page.GetByTestId("onboarding-certificate-file").SetInputFilesAsync(TestCrtPath);
        await Page.GetByTestId("onboarding-key-file").SetInputFilesAsync(TestKeyPath);
        await Page.GetByTestId("onboarding-certificate-password").FillAsync("wrong-password");
        await Page.GetByTestId("onboarding-next").ClickAsync();

        // API returns 400, shown via toast.error()
        var toastError = Page.Locator("[role='status']").Or(Page.GetByText("Błąd"));
        await Assertions.Expect(toastError.First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Should NOT advance to step 3
        await Assertions.Expect(Page.GetByTestId("onboarding-success")).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task CredentialList_CanUpdateToPemCertificate()
    {
        Assert.That(File.Exists(TestCrtPath), Is.True, $"Test CRT not found at {TestCrtPath}");
        Assert.That(File.Exists(TestKeyPath), Is.True, $"Test KEY not found at {TestKeyPath}");
        await DatabaseFixture.CleanupAsync();

        // Create a tenant with token credential via onboarding flow
        await LoginViaDirectFormAsync();
        await Page.WaitForURLAsync(
            url => url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        await Page.GetByTestId("onboarding-nip").FillAsync("2222222222");
        await Page.GetByTestId("onboarding-display-name").FillAsync("CredList PEM Co");
        await Page.GetByTestId("onboarding-next").ClickAsync();

        await Assertions.Expect(Page.GetByTestId("onboarding-credential-type")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });
        var testToken = Environment.GetEnvironmentVariable("E2E_TEST_KSEF_TOKEN") ?? "e2e-test-token";
        await Page.GetByTestId("onboarding-token").FillAsync(testToken);
        await Page.GetByTestId("onboarding-next").ClickAsync();

        await Assertions.Expect(Page.GetByTestId("onboarding-success")).ToBeVisibleAsync(
            new() { Timeout = 15_000 });
        await Page.GetByTestId("onboarding-go-dashboard").ClickAsync();

        // Navigate to credentials page
        await NavigateToPortalAsync("/credentials");

        await Assertions.Expect(Page.GetByTestId("credential-table")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });

        // Click update on existing Token credential
        await Page.GetByTestId("credential-update-button").First.ClickAsync();

        // Switch to certificate type in modal
        var typeSelect = Page.GetByTestId("credential-type-select");
        await Assertions.Expect(typeSelect).ToBeVisibleAsync();
        await typeSelect.Locator("button").Nth(1).ClickAsync();

        // CRT+KEY format should be default
        await Assertions.Expect(Page.GetByTestId("credential-cert-format-pem")).ToBeVisibleAsync();

        // Upload CRT file
        await Page.GetByTestId("credential-certificate-file").SetInputFilesAsync(TestCrtPath);

        // Upload KEY file
        await Page.GetByTestId("credential-key-file").SetInputFilesAsync(TestKeyPath);

        // Fill password
        await Page.GetByTestId("credential-certificate-password").FillAsync(TestKeyPassword);

        // Submit
        await Page.GetByTestId("credential-submit-button").ClickAsync();

        // Wait for modal to close and verify type badge changed to Certyfikat
        await Assertions.Expect(Page.GetByTestId("credential-type-badge").First).ToContainTextAsync(
            "Certyfikat",
            new() { Timeout = 10_000 });
    }

    [Test]
    public async Task CredentialList_PemFormatToggle_InModal()
    {
        await DatabaseFixture.CleanupAsync();

        // Create a tenant with token credential via onboarding flow
        await LoginViaDirectFormAsync();
        await Page.WaitForURLAsync(
            url => url.Contains("/onboarding", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15_000 });

        await Page.GetByTestId("onboarding-nip").FillAsync("1111111111");
        await Page.GetByTestId("onboarding-display-name").FillAsync("Format Toggle Co");
        await Page.GetByTestId("onboarding-next").ClickAsync();

        await Assertions.Expect(Page.GetByTestId("onboarding-credential-type")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });
        var testToken = Environment.GetEnvironmentVariable("E2E_TEST_KSEF_TOKEN") ?? "e2e-test-token";
        await Page.GetByTestId("onboarding-token").FillAsync(testToken);
        await Page.GetByTestId("onboarding-next").ClickAsync();

        await Assertions.Expect(Page.GetByTestId("onboarding-success")).ToBeVisibleAsync(
            new() { Timeout = 15_000 });
        await Page.GetByTestId("onboarding-go-dashboard").ClickAsync();

        // Navigate to credentials page
        await NavigateToPortalAsync("/credentials");

        await Assertions.Expect(Page.GetByTestId("credential-table")).ToBeVisibleAsync(
            new() { Timeout = 10_000 });

        // Open update modal
        await Page.GetByTestId("credential-update-button").First.ClickAsync();

        // Switch to certificate type
        var typeSelect = Page.GetByTestId("credential-type-select");
        await Assertions.Expect(typeSelect).ToBeVisibleAsync();
        await typeSelect.Locator("button").Nth(1).ClickAsync();

        // CRT+KEY is default -- key file should be visible
        await Assertions.Expect(Page.GetByTestId("credential-key-file")).ToBeVisibleAsync();

        // Switch to PFX
        await Page.GetByTestId("credential-cert-format-pfx").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("credential-key-file")).Not.ToBeVisibleAsync();

        // Switch back to CRT+KEY
        await Page.GetByTestId("credential-cert-format-pem").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("credential-key-file")).ToBeVisibleAsync();

        // Close modal
        await Page.GetByRole(AriaRole.Button, new() { Name = "Zamknij" }).ClickAsync();
    }
}
