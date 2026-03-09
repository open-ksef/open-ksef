using Microsoft.Extensions.Configuration;

namespace OpenKSeF.Portal.E2E.Infrastructure;

public sealed class PortalE2ETestOptions
{
    public string PortalBaseUrl { get; init; } = "http://localhost:5173";
    public string BrowserName { get; init; } = "chromium";
    public bool Headless { get; init; } = true;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string TestDatabaseConnectionString { get; init; } = "Data Source=openksef.portal.e2e.db";
    public string KeycloakAdminUsername { get; init; } = "admin";
    public string KeycloakAdminPassword { get; init; } = "admin";

    public static PortalE2ETestOptions Load()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Test.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        return new PortalE2ETestOptions
        {
            PortalBaseUrl = ReadString(configuration, "PORTAL_BASE_URL", "Portal:BaseUrl", "http://localhost:5173"),
            BrowserName = ReadString(configuration, "PLAYWRIGHT_BROWSER", "Playwright:Browser", "chromium"),
            Headless = ReadBool(configuration, "PLAYWRIGHT_HEADLESS", "Playwright:Headless", true),
            Username = ReadString(configuration, "KEYCLOAK_USERNAME", "Keycloak:Username", string.Empty),
            Password = ReadString(configuration, "KEYCLOAK_PASSWORD", "Keycloak:Password", string.Empty),
            TestDatabaseConnectionString = ReadString(
                configuration,
                "TEST_DATABASE_CONNECTION_STRING",
                "Database:ConnectionString",
                "Data Source=openksef.portal.e2e.db"),
            KeycloakAdminUsername = ReadString(configuration, "KEYCLOAK_ADMIN", "Keycloak:AdminUsername", "admin"),
            KeycloakAdminPassword = ReadString(configuration, "KEYCLOAK_ADMIN_PASSWORD", "Keycloak:AdminPassword", "admin")
        };
    }

    private static string ReadString(IConfiguration configuration, string envKey, string jsonKey, string defaultValue)
    {
        var value = configuration[envKey] ?? configuration[jsonKey];
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static bool ReadBool(IConfiguration configuration, string envKey, string jsonKey, bool defaultValue)
    {
        if (bool.TryParse(configuration[envKey], out var envValue))
        {
            return envValue;
        }

        if (bool.TryParse(configuration[jsonKey], out var jsonValue))
        {
            return jsonValue;
        }

        return defaultValue;
    }
}
