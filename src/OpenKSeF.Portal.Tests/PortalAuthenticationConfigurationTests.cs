using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using OpenKSeF.Portal.Authentication;

namespace OpenKSeF.Portal.Tests;

public class PortalAuthenticationConfigurationTests
{
    [Fact]
    public void AddPortalAuthentication_ConfiguresDefaultSchemesAndCookiePaths()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration();

        services.AddPortalAuthentication(configuration);

        using var provider = services.BuildServiceProvider();
        var authenticationOptions = provider
            .GetRequiredService<IOptionsMonitor<AuthenticationOptions>>()
            .CurrentValue;
        var cookieOptions = provider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, authenticationOptions.DefaultScheme);
        Assert.Equal(OpenIdConnectDefaults.AuthenticationScheme, authenticationOptions.DefaultChallengeScheme);
        Assert.Equal("/login", cookieOptions.LoginPath.Value);
        Assert.Equal("/logout", cookieOptions.LogoutPath.Value);
    }

    [Fact]
    public void AddPortalAuthentication_ConfiguresOpenIdConnectOptions()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration();

        services.AddPortalAuthentication(configuration);

        using var provider = services.BuildServiceProvider();
        var oidcOptions = provider
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        Assert.Equal("http://localhost:8080/realms/openksef", oidcOptions.Authority);
        Assert.Equal("openksef-portal", oidcOptions.ClientId);
        Assert.Equal("portal-secret", oidcOptions.ClientSecret);
        Assert.Equal("code", oidcOptions.ResponseType);
        Assert.True(oidcOptions.SaveTokens);
        Assert.True(oidcOptions.GetClaimsFromUserInfoEndpoint);
        Assert.Equal("/signin-oidc", oidcOptions.CallbackPath.Value);
        Assert.Equal("/signout-oidc", oidcOptions.SignedOutCallbackPath.Value);
        Assert.Contains("openid", oidcOptions.Scope);
        Assert.Contains("profile", oidcOptions.Scope);
        Assert.Contains("email", oidcOptions.Scope);
        Assert.False(oidcOptions.RequireHttpsMetadata);
    }

    [Fact]
    public void AddPortalAuthentication_UsesConfiguredGetClaimsFromUserInfoEndpoint()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Auth:GetClaimsFromUserInfoEndpoint"] = "false"
        });

        services.AddPortalAuthentication(configuration);

        using var provider = services.BuildServiceProvider();
        var oidcOptions = provider
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        Assert.False(oidcOptions.GetClaimsFromUserInfoEndpoint);
    }

    [Fact]
    public void AddPortalAuthentication_AllowsAuthorityAndPublicAuthorityAsValidIssuers()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Auth:Authority"] = "http://keycloak:8080/realms/openksef",
            ["Auth:PublicAuthority"] = "http://localhost:18080/realms/openksef"
        });

        services.AddPortalAuthentication(configuration);

        using var provider = services.BuildServiceProvider();
        var oidcOptions = provider
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        var validIssuers = oidcOptions.TokenValidationParameters.ValidIssuers?.ToArray() ?? Array.Empty<string>();

        Assert.Contains("http://keycloak:8080/realms/openksef", validIssuers);
        Assert.Contains("http://localhost:18080/realms/openksef", validIssuers);
    }

    [Fact]
    public void RewriteIssuerAddress_UsesPublicAuthorityHostAndPort()
    {
        var message = new OpenIdConnectMessage
        {
            IssuerAddress =
            "http://keycloak:8080/realms/openksef/protocol/openid-connect/auth"
        };

        var rewriteIssuerAddress = typeof(PortalAuthenticationExtensions).GetMethod(
            "RewriteIssuerAddress",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(rewriteIssuerAddress);
        rewriteIssuerAddress!.Invoke(
            null,
            new object[]
            {
                message,
                "http://keycloak:8080/realms/openksef",
                "http://localhost:18080/realms/openksef"
            });

        Assert.Equal(
            "http://localhost:18080/realms/openksef/protocol/openid-connect/auth",
            message.IssuerAddress);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Auth:Authority"] = "http://localhost:8080/realms/openksef",
            ["Auth:ClientId"] = "openksef-portal",
            ["Auth:ClientSecret"] = "portal-secret",
            ["Auth:RequireHttpsMetadata"] = "false"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                values[key] = value;
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
