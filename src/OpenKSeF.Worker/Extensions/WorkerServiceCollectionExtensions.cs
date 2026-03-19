using Microsoft.Extensions.Configuration;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Push;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Worker.Extensions;

public static class WorkerServiceCollectionExtensions
{
    public static IServiceCollection AddWorkerDomainServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IEmailService, NoOpEmailService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IInvoiceService, InvoiceService>();

        // Push providers (Relay -> FCM -> APNs, same order as API)
        services.AddHttpClient("push-relay");
        services.AddSingleton<IPushProvider, RelayPushProvider>();
        services.AddSingleton<IPushProvider, FcmPushProvider>();
        services.AddSingleton<IPushProvider, ApnsPushProvider>(sp =>
        {
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(configuration["APNs:BaseUrl"] ?? "https://api.push.apple.com")
            };
            return new ApnsPushProvider(
                httpClient,
                configuration,
                sp.GetRequiredService<ILogger<ApnsPushProvider>>());
        });

        return services;
    }
}
