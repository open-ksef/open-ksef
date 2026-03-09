using KSeF.Client.Api.Services;
using KSeF.Client.DI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Domain.Abstractions;

namespace OpenKSeF.Sync;

public static class DependencyInjection
{
    private static readonly Dictionary<string, string> LegacyUrlMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["https://ksef-test.mf.gov.pl/api"] = KsefEnvironmentsUris.TEST,
        ["https://ksef-test.mf.gov.pl"]     = KsefEnvironmentsUris.TEST,
        ["https://ksef-demo.mf.gov.pl/api"] = KsefEnvironmentsUris.DEMO,
        ["https://ksef-demo.mf.gov.pl"]     = KsefEnvironmentsUris.DEMO,
        ["https://ksef.mf.gov.pl/api"]      = KsefEnvironmentsUris.PROD,
        ["https://ksef.mf.gov.pl"]          = KsefEnvironmentsUris.PROD,
    };

    public static IServiceCollection AddSyncServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var configuredUrl = configuration["KSeF:BaseUrl"];
        var baseUrl = ResolveBaseUrl(configuredUrl);

        services.AddKSeFClient(options =>
        {
            options.BaseUrl = baseUrl;
        });

        services.AddCryptographyClient(CryptographyServiceWarmupMode.NonBlocking);

        services.AddScoped<IKSeFGateway, KSeFGateway>();

        services.Configure<TenantSyncOptions>(
            configuration.GetSection(TenantSyncOptions.SectionName));

        services.AddScoped<ITenantSyncService, TenantSyncService>();

        return services;
    }

    internal static string ResolveBaseUrl(string? configuredUrl)
    {
        if (string.IsNullOrWhiteSpace(configuredUrl))
            return KsefEnvironmentsUris.TEST;

        var trimmed = configuredUrl.TrimEnd('/');

        if (LegacyUrlMap.TryGetValue(trimmed, out var mapped))
            return mapped;

        return trimmed;
    }
}
