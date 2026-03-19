using KSeF.Client.Api.Services;
using KSeF.Client.DI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Domain.Abstractions;

namespace OpenKSeF.Sync;

public static class DependencyInjection
{
    public static IServiceCollection AddSyncServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddKSeFClient(options =>
        {
            options.BaseUrl = KsefEnvironmentsUris.TEST;
        });

        services.AddCryptographyClient(CryptographyServiceWarmupMode.NonBlocking);

        services.AddScoped<IKSeFGateway, KSeFGateway>();
        services.AddSingleton<KSeFInvoiceXmlParser>();

        services.Configure<TenantSyncOptions>(
            configuration.GetSection(TenantSyncOptions.SectionName));

        services.AddScoped<ITenantSyncService, TenantSyncService>();

        return services;
    }

    /// <summary>
    /// Resolves a KSeF environment key ("test", "production", "demo") or a legacy
    /// full URL to the canonical base URL from KSeF.Client NuGet package.
    /// Handles backward compat with older installs that stored raw URLs.
    /// </summary>
    public static string ResolveKSeFEnvironment(string? envKey)
    {
        if (string.IsNullOrWhiteSpace(envKey))
            return KsefEnvironmentsUris.TEST;

        var val = envKey.Trim().ToLowerInvariant();

        // Environment keys
        if (val is "production" or "prod") return KsefEnvironmentsUris.PROD;
        if (val is "demo") return KsefEnvironmentsUris.DEMO;
        if (val is "test") return KsefEnvironmentsUris.TEST;

        // Legacy full URLs — detect production vs demo vs test by domain fragments
        if (val.Contains("ksef.podatki.gov.pl") || val.Contains("api.ksef.mf.gov.pl"))
            return KsefEnvironmentsUris.PROD;
        if (val.Contains("demo"))
            return KsefEnvironmentsUris.DEMO;

        return KsefEnvironmentsUris.TEST;
    }

    /// <summary>
    /// Maps a value (env key or legacy URL) to the canonical environment key
    /// suitable for storage in SystemConfigs.
    /// </summary>
    public static string NormalizeKSeFEnvironmentKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "test";

        var resolved = ResolveKSeFEnvironment(value);
        if (resolved == KsefEnvironmentsUris.PROD) return "production";
        if (resolved == KsefEnvironmentsUris.DEMO) return "demo";
        return "test";
    }
}
