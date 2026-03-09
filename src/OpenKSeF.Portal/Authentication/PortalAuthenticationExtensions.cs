using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace OpenKSeF.Portal.Authentication;

public static class PortalAuthenticationExtensions
{
    public static IServiceCollection AddPortalAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authority = configuration["Auth:Authority"];
        ArgumentException.ThrowIfNullOrWhiteSpace(authority, "Auth:Authority");

        var clientId = configuration["Auth:ClientId"];
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId, "Auth:ClientId");

        var publicAuthority = configuration["Auth:PublicAuthority"];
        var getClaimsFromUserInfoEndpoint = configuration.GetValue("Auth:GetClaimsFromUserInfoEndpoint", true);
        var requireHttpsMetadata = configuration.GetValue("Auth:RequireHttpsMetadata", true);

        services.AddAuthorization();
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, PortalRevalidatingAuthenticationStateProvider>();

        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.LogoutPath = "/logout";
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = authority;
                options.ClientId = clientId;
                options.ClientSecret = configuration["Auth:ClientSecret"];
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = getClaimsFromUserInfoEndpoint;
                options.CallbackPath = "/signin-oidc";
                options.SignedOutCallbackPath = "/signout-oidc";
                options.RequireHttpsMetadata = requireHttpsMetadata;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");

                if (!string.IsNullOrWhiteSpace(publicAuthority))
                {
                    options.TokenValidationParameters.ValidIssuers = new[]
                    {
                        authority,
                        publicAuthority
                    }
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                    ConfigurePublicAuthorityRedirects(options, authority, publicAuthority);
                }
            });

        return services;
    }

    public static IEndpointRouteBuilder MapPortalAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/login", async (HttpContext context, string? returnUrl) =>
        {
            var redirectUri = NormalizeReturnUrl(returnUrl);
            await context.ChallengeAsync(
                OpenIdConnectDefaults.AuthenticationScheme,
                new AuthenticationProperties
                {
                    RedirectUri = redirectUri
                });
        }).AllowAnonymous();

        endpoints.MapGet("/logout", async (HttpContext context, string? returnUrl) =>
        {
            var redirectUri = NormalizeReturnUrl(returnUrl);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignOutAsync(
                OpenIdConnectDefaults.AuthenticationScheme,
                new AuthenticationProperties
                {
                    RedirectUri = redirectUri
                });
        }).AllowAnonymous();

        return endpoints;
    }

    private static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        if (!returnUrl.StartsWith('/'))
        {
            return "/";
        }

        if (returnUrl.StartsWith("//", StringComparison.Ordinal) ||
            returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return "/";
        }

        return returnUrl;
    }

    private static void ConfigurePublicAuthorityRedirects(
        OpenIdConnectOptions options,
        string authority,
        string publicAuthority)
    {
        options.Events ??= new OpenIdConnectEvents();

        var onRedirectToIdentityProvider = options.Events.OnRedirectToIdentityProvider;
        options.Events.OnRedirectToIdentityProvider = async context =>
        {
            RewriteIssuerAddress(context.ProtocolMessage, authority, publicAuthority);
            if (onRedirectToIdentityProvider is not null)
            {
                await onRedirectToIdentityProvider(context);
            }
        };

        var onRedirectToIdentityProviderForSignOut = options.Events.OnRedirectToIdentityProviderForSignOut;
        options.Events.OnRedirectToIdentityProviderForSignOut = async context =>
        {
            RewriteIssuerAddress(context.ProtocolMessage, authority, publicAuthority);
            if (onRedirectToIdentityProviderForSignOut is not null)
            {
                await onRedirectToIdentityProviderForSignOut(context);
            }
        };
    }

    private static void RewriteIssuerAddress(
        OpenIdConnectMessage message,
        string authority,
        string publicAuthority)
    {
        if (string.IsNullOrWhiteSpace(message.IssuerAddress) ||
            !Uri.TryCreate(message.IssuerAddress, UriKind.Absolute, out var issuerUri) ||
            !Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) ||
            !Uri.TryCreate(publicAuthority, UriKind.Absolute, out var publicAuthorityUri))
        {
            return;
        }

        var suffix = issuerUri.AbsolutePath.StartsWith(authorityUri.AbsolutePath, StringComparison.OrdinalIgnoreCase)
            ? issuerUri.AbsolutePath[authorityUri.AbsolutePath.Length..]
            : issuerUri.AbsolutePath;

        var publicAuthorityPath = publicAuthorityUri.AbsolutePath.TrimEnd('/');
        var normalizedSuffix = suffix.TrimStart('/');
        var combinedPath = string.IsNullOrEmpty(normalizedSuffix)
            ? (string.IsNullOrEmpty(publicAuthorityPath) ? "/" : publicAuthorityPath)
            : $"{publicAuthorityPath}/{normalizedSuffix}";

        var builder = new UriBuilder(publicAuthorityUri.Scheme, publicAuthorityUri.Host, publicAuthorityUri.Port)
        {
            Path = combinedPath,
            Query = issuerUri.Query.TrimStart('?')
        };

        message.IssuerAddress = builder.Uri.ToString();
    }
}
