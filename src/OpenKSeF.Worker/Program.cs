using System.Security.Cryptography;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Abstractions;
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

    if (builder.Environment.IsDevelopment())
        options.EnableSensitiveDataLogging();
});

// Encryption: deferred factory that reads from SystemConfigService (DB first, env fallback).
// The actual key resolution happens at first use, after the startup wait-gate confirms setup is complete.
var isDev = builder.Environment.IsDevelopment();
builder.Services.AddSingleton<IEncryptionService>(sp =>
{
    var sysConfig = sp.GetRequiredService<ISystemConfigService>();
    var keyBase64 = sysConfig.GetValue(SystemConfigKeys.EncryptionKey);

    if (string.IsNullOrEmpty(keyBase64))
    {
        if (!isDev)
            throw new InvalidOperationException(
                "Encryption key not configured. Run the admin setup wizard or set ENCRYPTION_KEY env var. " +
                "Generate one with: openssl rand -base64 32");
        return new AesGcmEncryptionService(RandomNumberGenerator.GetBytes(32));
    }

    return new AesGcmEncryptionService(Convert.FromBase64String(keyBase64));
});

// System config (DB-backed key-value store with env var fallback)
builder.Services.AddSingleton<ISystemConfigService, SystemConfigService>();

// KSeF sync services (gateway + sync + KSeF client)
builder.Services.AddSyncServices(builder.Configuration);

// Firebase / Push Notifications (direct FCM -- advanced/opt-in)
var firebaseCredentialsJson = builder.Configuration["Firebase:CredentialsJson"];
if (!string.IsNullOrEmpty(firebaseCredentialsJson))
{
    FirebaseApp.Create(new AppOptions
    {
        Credential = GoogleCredential.FromJson(firebaseCredentialsJson)
    });
    Log.Information("Firebase initialized in Worker");
}
else
{
    Log.Information("Firebase:CredentialsJson not configured — using relay for push notifications");
}

// Domain services (including push providers: Relay -> FCM -> APNs)
builder.Services.AddWorkerDomainServices(builder.Configuration);

// Worker-specific sync options
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));

builder.Services.AddHostedService<InvoiceSyncService>();

var host = builder.Build();

// Load system config cache
var systemConfig = host.Services.GetRequiredService<ISystemConfigService>();
await systemConfig.RefreshCacheAsync();

// Wait for admin setup to complete before starting sync.
// The Worker starts alongside all services via docker-compose, but the encryption key
// and KSeF environment only exist after the admin runs the setup wizard.
// Env-configured deployments (ENCRYPTION_KEY set directly) skip the wait-gate.
bool IsReady() => systemConfig.IsInitialized
    || !string.IsNullOrEmpty(systemConfig.GetValue(SystemConfigKeys.EncryptionKey));

while (!IsReady())
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
    ksefOptions.BaseUrl = OpenKSeF.Sync.DependencyInjection.ResolveKSeFEnvironment(ksefEnv);
}

host.Run();
