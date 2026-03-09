using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Worker.Extensions;

public static class WorkerServiceCollectionExtensions
{
    public static IServiceCollection AddWorkerDomainServices(this IServiceCollection services)
    {
        services.AddScoped<IEmailService, NoOpEmailService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        return services;
    }
}
