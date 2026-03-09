using Microsoft.Playwright;
using OpenKSeF.Portal.E2E.Fixtures;

namespace OpenKSeF.Portal.E2E.Infrastructure;

[NonParallelizable]
public abstract class BasePortalTest
{
    protected PortalE2ETestOptions Options { get; private set; } = null!;
    protected DatabaseFixture DatabaseFixture { get; private set; } = null!;
    protected IPlaywright Playwright { get; private set; } = null!;
    protected IBrowser Browser { get; private set; } = null!;
    protected IBrowserContext Context { get; private set; } = null!;
    protected IPage Page { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        Options = PortalE2ETestOptions.Load();
        DatabaseFixture = await global::OpenKSeF.Portal.E2E.Fixtures.DatabaseFixture
            .CreateAsync(Options.TestDatabaseConnectionString);
        await DatabaseFixture.CleanupAsync();
        await DatabaseFixture.SeedDefaultDataAsync();
    }

    [SetUp]
    public async Task SetUpAsync()
    {
        Options = PortalE2ETestOptions.Load();
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await LaunchBrowserAsync(Playwright, Options);
        Context = await Browser.NewContextAsync();
        Page = await Context.NewPageAsync();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        if (Context is not null)
        {
            await Context.CloseAsync();
        }

        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        Playwright?.Dispose();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync()
    {
        if (DatabaseFixture is not null)
        {
            await DatabaseFixture.CleanupAsync();
            await DatabaseFixture.DisposeAsync();
        }
    }

    protected async Task NavigateToPortalAsync(string relativePath = "/")
    {
        var baseUri = Options.PortalBaseUrl.TrimEnd('/');
        var path = relativePath.StartsWith('/') ? relativePath : $"/{relativePath}";
        await Page.GotoAsync($"{baseUri}{path}");
    }

    protected async Task LoginWithKeycloakAsync()
    {
        if (string.IsNullOrWhiteSpace(Options.Username) || string.IsNullOrWhiteSpace(Options.Password))
        {
            throw new InvalidOperationException("Keycloak credentials are not configured for E2E login.");
        }

        await NavigateToPortalAsync("/login");
        await Page.GetByLabel("Username or email").FillAsync(Options.Username);
        await Page.GetByLabel("Password").FillAsync(Options.Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign In" }).ClickAsync();
    }

    private static Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright, PortalE2ETestOptions options)
    {
        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = options.Headless
        };

        return options.BrowserName.ToLowerInvariant() switch
        {
            "firefox" => playwright.Firefox.LaunchAsync(launchOptions),
            "webkit" => playwright.Webkit.LaunchAsync(launchOptions),
            _ => playwright.Chromium.LaunchAsync(launchOptions)
        };
    }
}
