using System.Text.Json;

namespace OpenKSeF.Portal.Tests;

public class KeycloakRealmConfigurationTests
{
    [Fact]
    public void PortalWebClient_Exists_WithSpaDefaults()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(FindRealmFilePath()));
        var portalWebClient = GetClient(json, "openksef-portal-web");

        Assert.Equal("openid-connect", portalWebClient.GetProperty("protocol").GetString());
        Assert.True(portalWebClient.GetProperty("enabled").GetBoolean());
        Assert.True(portalWebClient.GetProperty("publicClient").GetBoolean());
        Assert.True(portalWebClient.GetProperty("standardFlowEnabled").GetBoolean());
        Assert.False(portalWebClient.GetProperty("implicitFlowEnabled").GetBoolean());
        Assert.False(portalWebClient.GetProperty("directAccessGrantsEnabled").GetBoolean());
    }

    [Fact]
    public void PortalWebClient_ContainsExpectedRedirectUris_AndWebOrigins()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(FindRealmFilePath()));
        var portalWebClient = GetClient(json, "openksef-portal-web");

        var redirectUris = portalWebClient
            .GetProperty("redirectUris")
            .EnumerateArray()
            .Select(uri => uri.GetString())
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var webOrigins = portalWebClient
            .GetProperty("webOrigins")
            .EnumerateArray()
            .Select(uri => uri.GetString())
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("http://localhost:5173/callback", redirectUris);
        Assert.Contains("http://localhost:5173/silent-callback", redirectUris);
        Assert.Contains("__APP_EXTERNAL_BASE_URL__/callback", redirectUris);
        Assert.Contains("__APP_EXTERNAL_BASE_URL__/*", redirectUris);

        Assert.Contains("http://localhost:5173", webOrigins);
        Assert.Contains("__APP_EXTERNAL_BASE_URL__", webOrigins);
    }

    [Fact]
    public void PortalWebClient_ConfiguresPkceS256()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(FindRealmFilePath()));
        var portalWebClient = GetClient(json, "openksef-portal-web");

        var attributes = portalWebClient.GetProperty("attributes");
        Assert.Equal("S256", attributes.GetProperty("pkce.code.challenge.method").GetString());
    }

    [Fact]
    public void Realm_AllowsSelfRegistration()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(FindRealmFilePath()));

        Assert.True(json.RootElement.GetProperty("registrationAllowed").GetBoolean());
    }

    [Fact]
    public void MobileClient_ContainsPlaceholderAndCustomScheme()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(FindRealmFilePath()));
        var mobileClient = GetClient(json, "openksef-mobile");

        var redirectUris = mobileClient
            .GetProperty("redirectUris")
            .EnumerateArray()
            .Select(uri => uri.GetString())
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("__APP_EXTERNAL_BASE_URL__/*", redirectUris);
        Assert.Contains("openksef://auth/*", redirectUris);
    }

    private static JsonElement GetClient(JsonDocument document, string clientId)
    {
        return document.RootElement
            .GetProperty("clients")
            .EnumerateArray()
            .First(client => client.GetProperty("clientId").GetString() == clientId);
    }

    private static string FindRealmFilePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "keycloak", "realm-openksef.json.template");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate keycloak/realm-openksef.json.template");
    }
}
