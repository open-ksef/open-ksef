using KSeF.Client.Api.Services;
using KSeF.Client.DI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync.Issuing;

namespace OpenKSeF.Sync;

public static class DependencyInjection
{
    public const string ForbidProductionKey = "KSeF:ForbidProduction";

    public static IServiceCollection AddSyncServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddKSeFClient(options =>
        {
            options.BaseUrl = KsefEnvironmentsUris.TEST;
        });

        services.AddCryptographyClient(CryptographyServiceWarmupMode.NonBlocking);

        if (configuration.GetValue<bool>(ForbidProductionKey))
            services.ConfigureHttpClientDefaults(client => client.AddHttpMessageHandler(() => new KSeFProductionGuard()));

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

    /// <summary>Resolves the environment a host starts on. Only a stack that sets KSeF:ForbidProduction refuses production, so a real install, which never sets it, cannot stop here.</summary>
    public static string ResolveStartupKSeFEnvironment(string envKey, IConfiguration configuration)
    {
        var url = ResolveKSeFEnvironment(envKey);
        if (url == KsefEnvironmentsUris.PROD && configuration.GetValue<bool>(ForbidProductionKey))
            throw new InvalidOperationException(
                $"KSeF environment '{envKey}' is production, and {ForbidProductionKey} forbids it. On a real install remove KSeF__ForbidProduction from the stack and keep the row. On a dev or test stack set the ksef_environment row or KSEF_BASE_URL to test.");

        return url;
    }

    public static void ApplyStartupKSeFEnvironment(this IServiceProvider services, IConfiguration configuration)
    {
        var systemConfig = services.GetRequiredService<ISystemConfigService>();
        var ksefEnv = systemConfig.GetValue(SystemConfigKeys.KSeFEnvironment)
            ?? systemConfig.GetValue(SystemConfigKeys.KSeFBaseUrl);
        if (!string.IsNullOrEmpty(ksefEnv))
            services.GetRequiredService<KSeFClientOptions>().BaseUrl = ResolveStartupKSeFEnvironment(ksefEnv, configuration);
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
