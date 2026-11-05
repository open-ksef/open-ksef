using KSeF.Client.Api.Services;
using KSeF.Client.DI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Sync.Issuing;

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
        services.AddSingleton<IFa3InvoiceWriter, Fa3InvoiceWriter>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IKSeFInvoiceSubmitter, KSeFInvoiceSubmitter>();
        services.AddScoped<IssuedInvoiceSendProcessor>();

        services.Configure<TenantSyncOptions>(
            configuration.GetSection(TenantSyncOptions.SectionName));

        services.AddSingleton<XmlDownloadBlocks>();
        services.AddScoped<ITenantSyncService, TenantSyncService>();

        return services;
    }

    /// <summary>
    /// Resolves a KSeF environment key ("test", "production", "demo") or a legacy
    /// full URL to the canonical base URL from KSeF.Client NuGet package.
    /// Legacy URLs match by exact host. Any other value throws, so nothing runs on a guessed environment.
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

        var host = Uri.TryCreate(val.Contains("://") ? val : $"https://{val}", UriKind.Absolute, out var uri) ? uri.Host : val;

        return host switch
        {
            "ksef-test.mf.gov.pl" or "api-test.ksef.mf.gov.pl" => KsefEnvironmentsUris.TEST,
            "ksef-demo.mf.gov.pl" or "api-demo.ksef.mf.gov.pl" => KsefEnvironmentsUris.DEMO,
            "ksef.podatki.gov.pl" or "api.ksef.mf.gov.pl" => KsefEnvironmentsUris.PROD,
            // ksef.mf.gov.pl is left out on purpose: it means production but has run as TEST since #4, so either mapping flips someone.
            _ => throw new InvalidOperationException(
                $"Unknown KSeF environment '{envKey}'. Use test, demo or production, see docs/admin-setup.md."),
        };
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
