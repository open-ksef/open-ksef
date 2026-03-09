using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E;

public class PortalE2EConfigurationTests
{
    [Test]
    public void Load_UsesDefaults_WhenEnvNotProvided()
    {
        Environment.SetEnvironmentVariable("PORTAL_BASE_URL", null);

        var options = PortalE2ETestOptions.Load();

        Assert.That(options.PortalBaseUrl, Is.EqualTo("http://localhost:5173"));
        Assert.That(options.BrowserName, Is.EqualTo("chromium"));
        Assert.That(options.Headless, Is.True);
        Assert.That(options.TestDatabaseConnectionString, Is.EqualTo("Data Source=openksef.portal.e2e.db"));
    }

    [Test]
    public void Load_UsesEnvironmentOverrides()
    {
        Environment.SetEnvironmentVariable("PORTAL_BASE_URL", "http://localhost:9999");
        Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSER", "firefox");
        Environment.SetEnvironmentVariable("PLAYWRIGHT_HEADLESS", "false");
        Environment.SetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING", "Data Source=test-portal-e2e.db");

        try
        {
            var options = PortalE2ETestOptions.Load();

            Assert.That(options.PortalBaseUrl, Is.EqualTo("http://localhost:9999"));
            Assert.That(options.BrowserName, Is.EqualTo("firefox"));
            Assert.That(options.Headless, Is.False);
            Assert.That(options.TestDatabaseConnectionString, Is.EqualTo("Data Source=test-portal-e2e.db"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTAL_BASE_URL", null);
            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSER", null);
            Environment.SetEnvironmentVariable("PLAYWRIGHT_HEADLESS", null);
            Environment.SetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING", null);
        }
    }
}
