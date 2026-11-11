using Microsoft.Extensions.Options;
using OpenKSeF.Domain;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;
using OpenKSeF.Worker;
using OpenKSeF.Worker.Services;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

NotificationService.EnsurePolishFormatting(System.Globalization.CultureInfo.GetCultureInfo("pl-PL"));

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}"));

builder.Services.AddOpenKSeFCore(builder.Configuration).AddSyncServices(builder.Configuration);
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
builder.Services.AddSingleton<KSeFRuntimeConfig>();
builder.Services.AddHostedService<InvoiceSyncService>();
builder.Services.AddHostedService<IssuedInvoiceSendService>();

var host = builder.Build();

var systemConfig = host.Services.GetRequiredService<ISystemConfigService>();
await systemConfig.RefreshCacheAsync();

using (var scope = host.Services.CreateScope())
{
    if (!await EncryptionKeyMigration.AdoptConfiguredKeyAsync(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            builder.Configuration,
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("OpenKSeF.Worker.EncryptionKey")))
    {
        await Log.CloseAndFlushAsync();
        return 1;
    }
    EmailService.WarnIfPlaintextAuth(
        scope.ServiceProvider.GetRequiredService<IOptions<EmailOptions>>().Value,
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("OpenKSeF.Worker.Email"));
}
await systemConfig.RefreshCacheAsync();

// Wait for admin setup to complete before starting sync.
// The Worker starts alongside all services via docker-compose, but the Keycloak realm
// and KSeF environment only exist after the admin runs the setup wizard.
while (!systemConfig.IsInitialized)
{
    Log.Information("System not initialized — waiting for admin setup wizard. Retrying in 30s...");
    await Task.Delay(TimeSpan.FromSeconds(30));
    await systemConfig.RefreshCacheAsync();
}
Log.Information("System initialized — starting Worker");

try
{
    host.Services.ApplyStartupKSeFEnvironment(builder.Configuration);
}
catch (InvalidOperationException ex)
{
    Log.Fatal("{Message}", ex.Message);
    await Log.CloseAndFlushAsync();
    return 1;
}

host.Run();
return 0;
