using System.Security.Cryptography;
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

// Encryption (shared key with API for decrypting KSeF tokens)
var encKeyBase64 = builder.Configuration["ENCRYPTION_KEY"];
if (!string.IsNullOrEmpty(encKeyBase64))
{
    var encKey = Convert.FromBase64String(encKeyBase64);
    builder.Services.AddSingleton<IEncryptionService>(new AesGcmEncryptionService(encKey));
}
else if (builder.Environment.IsDevelopment())
{
    var ephemeralKey = RandomNumberGenerator.GetBytes(32);
    builder.Services.AddSingleton<IEncryptionService>(new AesGcmEncryptionService(ephemeralKey));
}
else
{
    throw new InvalidOperationException("ENCRYPTION_KEY is required in non-development environments.");
}

// System config (DB-backed key-value store with env var fallback)
builder.Services.AddSingleton<ISystemConfigService, SystemConfigService>();

// KSeF sync services (gateway + sync + KSeF client)
builder.Services.AddSyncServices(builder.Configuration);

// Domain services
builder.Services.AddWorkerDomainServices();

// Worker-specific sync options
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));

builder.Services.AddHostedService<InvoiceSyncService>();

var host = builder.Build();

// Load system config cache and override KSeF base URL from DB
var systemConfig = host.Services.GetRequiredService<ISystemConfigService>();
await systemConfig.RefreshCacheAsync();

var dbKsefUrl = systemConfig.GetValue(SystemConfigKeys.KSeFBaseUrl);
if (!string.IsNullOrEmpty(dbKsefUrl))
{
    var ksefOptions = host.Services.GetRequiredService<KSeF.Client.DI.KSeFClientOptions>();
    ksefOptions.BaseUrl = dbKsefUrl;
}

host.Run();
