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
        var configuredUrl = configuration["KSeF:BaseUrl"];

        services.AddKSeFClient(options =>
        {
            options.BaseUrl = !string.IsNullOrWhiteSpace(configuredUrl)
                ? configuredUrl
                : KsefEnvironmentsUris.TEST;
        });

        services.AddCryptographyClient(CryptographyServiceWarmupMode.NonBlocking);

        services.AddScoped<IKSeFGateway, KSeFGateway>();

        services.Configure<TenantSyncOptions>(
            configuration.GetSection(TenantSyncOptions.SectionName));

        services.AddScoped<ITenantSyncService, TenantSyncService>();

        return services;
    }

    /// <summary>
    /// Resolves a KSeF environment key (from the admin setup wizard dropdown)
    /// to the base URL expected by the KSeF.Client NuGet library.
    /// </summary>
    public static string ResolveKSeFEnvironment(string? envKey) => envKey?.ToLowerInvariant() switch
    {
        "production" => KsefEnvironmentsUris.PROD,
        "demo" => KsefEnvironmentsUris.DEMO,
        _ => KsefEnvironmentsUris.TEST,
    };
}
