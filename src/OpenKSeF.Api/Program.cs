using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenKSeF.Api.Extensions;
using OpenKSeF.Api.Hubs;
using OpenKSeF.Api.Push;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;
using Serilog;
using Serilog.Context;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
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

// System config (DB-backed key-value store with env var fallback)
builder.Services.AddSingleton<ISystemConfigService, SystemConfigService>();
builder.Services.AddScoped<ISystemSetupService, SystemSetupService>();
builder.Services.AddScoped<ISystemSettingsService, SystemSettingsService>();

// Encryption
builder.Services.AddEncryptionService(builder.Configuration, builder.Environment);

// KSeF sync services (gateway + sync + KSeF client)
builder.Services.AddSyncServices(builder.Configuration);

// Authentication
var authority = builder.Configuration["Auth:Authority"]?.TrimEnd('/');
ArgumentException.ThrowIfNullOrEmpty(authority, "Auth:Authority");
var publicAuthority = builder.Configuration["Auth:PublicAuthority"]?.TrimEnd('/');
var additionalIssuers = builder.Configuration.GetSection("Auth:AdditionalIssuers").Get<string[]>() ?? [];
var validIssuers = new[] { authority, publicAuthority }
    .Concat(additionalIssuers)
    .Where(static issuer => !string.IsNullOrWhiteSpace(issuer))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.Audience = "openksef-api";
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidIssuers = validIssuers,
            ValidAudiences = new[] { "openksef-api", "account" }
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

// Dynamic issuer validation: also accept tokens whose issuer matches the
// ExternalBaseUrl stored in the DB by the admin-setup wizard.
builder.Services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>>(sp =>
{
    var sysConfig = sp.GetRequiredService<ISystemConfigService>();
    return new PostConfigureOptions<JwtBearerOptions>(
        JwtBearerDefaults.AuthenticationScheme, options =>
    {
        var staticIssuers = options.TokenValidationParameters.ValidIssuers?.ToArray() ?? [];
        options.TokenValidationParameters.ValidIssuers = null;
        options.TokenValidationParameters.IssuerValidator = (issuer, _, _) =>
        {
            if (staticIssuers.Contains(issuer, StringComparer.OrdinalIgnoreCase))
                return issuer;

            var extUrl = sysConfig.GetValue(SystemConfigKeys.ExternalBaseUrl)?.TrimEnd('/');
            if (!string.IsNullOrEmpty(extUrl))
            {
                var dynamicIssuer = $"{extUrl}/auth/realms/openksef";
                if (string.Equals(issuer, dynamicIssuer, StringComparison.OrdinalIgnoreCase))
                    return issuer;
            }

            throw new SecurityTokenInvalidIssuerException(
                $"IDX10205: Issuer validation failed. Issuer: '{issuer}'. " +
                $"Did not match static issuers: '{string.Join(", ", staticIssuers)}' " +
                $"or dynamic issuer from ExternalBaseUrl: '{extUrl}'.");
        };
    });
});

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Mobile setup token services
builder.Services.AddSingleton<ISetupTokenService, SetupTokenService>();
builder.Services.AddHttpClient("keycloak");
builder.Services.AddSingleton<IKeycloakTokenExchangeService, KeycloakTokenExchangeService>();
builder.Services.AddSingleton<IKeycloakUserService, KeycloakUserService>();

// Domain services
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ITransferDetailsService, TransferDetailsService>();
builder.Services.AddScoped<IQrCodeService, QrCodeService>();

// Email
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddScoped<IEmailService, EmailService>();

// SignalR for local push notifications
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();
builder.Services.AddSingleton<IUserPushProvider, SignalRPushProvider>();

// Push Relay (team-operated, default for self-hosted instances)
builder.Services.AddHttpClient("push-relay");
builder.Services.AddSingleton<IPushProvider, RelayPushProvider>();

// Firebase / Push Notifications (direct FCM -- advanced/opt-in)
var firebaseCredentialsJson = builder.Configuration["Firebase:CredentialsJson"];
if (!string.IsNullOrEmpty(firebaseCredentialsJson))
{
    FirebaseApp.Create(new AppOptions
    {
        Credential = GoogleCredential.FromJson(firebaseCredentialsJson)
    });
    Log.Information("Firebase initialized");
}
else
{
    Log.Information("Firebase:CredentialsJson not configured — using relay for push notifications");
}

builder.Services.AddSingleton<IPushProvider, FcmPushProvider>();
builder.Services.AddSingleton<IPushProvider, ApnsPushProvider>(sp =>
{
    var httpClient = new HttpClient
    {
        BaseAddress = new Uri(builder.Configuration["APNs:BaseUrl"] ?? "https://api.push.apple.com")
    };
    return new ApnsPushProvider(httpClient, builder.Configuration, sp.GetRequiredService<ILogger<ApnsPushProvider>>());
});

// Controllers
builder.Services.AddControllers();

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "OpenKSeF API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter a valid JWT token from Keycloak"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>();

var app = builder.Build();

// Apply pending migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var startupLogger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("OpenKSeF.Api.Migrations");
    await MigrationHelper.ApplyMigrationsIdempotentlyAsync(db, startupLogger);
}

// Load system config cache
var systemConfig = app.Services.GetRequiredService<ISystemConfigService>();
await systemConfig.RefreshCacheAsync();

// Correlation ID middleware
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
        ?? Guid.NewGuid().ToString();
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    using (LogContext.PushProperty("CorrelationId", correlationId))
    {
        await next();
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
