using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Push;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain;

/// <summary>Services both the API and the Worker host need; host-only services stay in each Program.cs.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddOpenKSeFCore(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Db");
        ArgumentException.ThrowIfNullOrEmpty(connectionString, "ConnectionStrings:Db");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);

            if (configuration.GetValue("Database:EnableSensitiveDataLogging", false))
                options.EnableSensitiveDataLogging();
        });

        services.AddSingleton<ISystemConfigService, SystemConfigService>();
        services.AddEncryptionService(configuration);

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IssuedInvoiceEscalationService>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient("push-relay");
        services.AddSingleton<RelayHealth>();
        services.AddSingleton<IPushProvider, RelayPushProvider>();
        services.AddSingleton<IPushProvider, FcmPushProvider>();
        services.AddSingleton<IPushProvider, ApnsPushProvider>(sp =>
        {
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(configuration["APNs:BaseUrl"] ?? "https://api.push.apple.com")
            };
            return new ApnsPushProvider(httpClient, configuration, sp.GetRequiredService<ILogger<ApnsPushProvider>>());
        });

        return services;
    }
}
