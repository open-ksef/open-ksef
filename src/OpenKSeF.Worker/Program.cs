using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using Microsoft.Extensions.Options;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;
using OpenKSeF.Worker.Extensions;
using OpenKSeF.Worker;
using OpenKSeF.Worker.Services;
using OpenKSeF.Sync;
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

// Database
var connectionString = builder.Configuration.GetConnectionString("Db");
ArgumentException.ThrowIfNullOrEmpty(connectionString, "ConnectionStrings:Db");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase))
        options.UseNpgsql(connectionString);
    else
        options.UseSqlite(connectionString);

    if (builder.Configuration.GetValue("Database:EnableSensitiveDataLogging", false))
        options.EnableSensitiveDataLogging();
});

// Encryption: key resolution happens at first use, after adoption and the startup wait-gate.
builder.Services.AddEncryptionService(builder.Configuration);

// System config (DB-backed key-value store with env var fallback)
builder.Services.AddSingleton<ISystemConfigService, SystemConfigService>();

// KSeF sync services (gateway + sync + KSeF client)
builder.Services.AddSyncServices(builder.Configuration);

// Domain services (including push providers: Relay -> FCM -> APNs)
builder.Services.AddWorkerDomainServices(builder.Configuration);

// Worker-specific sync options
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));

builder.Services.AddSingleton<KSeFRuntimeConfig>();
builder.Services.AddHostedService<InvoiceSyncService>();
builder.Services.AddHostedService<IssuedInvoiceSendService>();

var host = builder.Build();

// Load system config cache
var systemConfig = host.Services.GetRequiredService<ISystemConfigService>();
await systemConfig.RefreshCacheAsync();

using (var scope = host.Services.CreateScope())
{
    await EncryptionKeyMigration.AdoptConfiguredKeyAsync(
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
        builder.Configuration,
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("OpenKSeF.Worker.EncryptionKey"));
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

// Override KSeF base URL from DB config
var ksefEnv = systemConfig.GetValue(SystemConfigKeys.KSeFEnvironment)
    ?? systemConfig.GetValue(SystemConfigKeys.KSeFBaseUrl);
if (!string.IsNullOrEmpty(ksefEnv))
{
    var ksefOptions = host.Services.GetRequiredService<KSeF.Client.DI.KSeFClientOptions>();
    try
    {
        ksefOptions.BaseUrl = OpenKSeF.Sync.DependencyInjection.ResolveStartupKSeFEnvironment(ksefEnv, builder.Configuration);
    }
    catch (InvalidOperationException ex)
    {
        Log.Fatal("{Message}", ex.Message);
        await Log.CloseAndFlushAsync();
        return 1;
    }
}

host.Run();
return 0;
