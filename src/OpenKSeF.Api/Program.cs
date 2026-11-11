using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.Hubs;
using OpenKSeF.Api.Push;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain;
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

NotificationService.EnsurePolishFormatting(System.Globalization.CultureInfo.GetCultureInfo("pl-PL"));

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}"));

builder.Services.AddOpenKSeFCore(builder.Configuration).AddSyncServices(builder.Configuration);
builder.Services.AddScoped<ISystemSetupService, SystemSetupService>();
builder.Services.AddScoped<ISystemSettingsService, SystemSettingsService>();

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
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Auth:RequireHttpsMetadata", true);
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

builder.Services.AddAuthorization(options => options.AddPolicy(SystemSettingsController.AdminPolicy, policy =>
    policy.RequireAssertion(context => context.User.FindAll("realm_access").Any(claim =>
        JsonDocument.Parse(claim.Value).RootElement.TryGetProperty("roles", out var roles)
        && roles.ValueKind == JsonValueKind.Array
        && roles.EnumerateArray().Any(role => role.GetString() == SystemSettingsController.AdminRole)))));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

builder.Services.AddMemoryCache();

builder.Services.AddSingleton<ISetupSessionService, SetupSessionService>();

builder.Services.AddSingleton<ISetupTokenService, SetupTokenService>();
builder.Services.AddHttpClient("keycloak");
builder.Services.AddSingleton<IKeycloakTokenExchangeService, KeycloakTokenExchangeService>();
builder.Services.AddSingleton<IKeycloakUserService, KeycloakUserService>();

builder.Services.AddScoped<ITransferDetailsService, TransferDetailsService>();
builder.Services.AddScoped<IQrCodeService, QrCodeService>();
builder.Services.AddScoped<ISellerProfileService, SellerProfileService>();
builder.Services.AddScoped<IKSeFIssuingEnvironment, OpenKSeF.Api.Services.KSeFIssuingEnvironment>();
builder.Services.AddScoped<IIssuedInvoiceService, IssuedInvoiceService>();
builder.Services.AddHttpClient<WhiteListClient>(client =>
{
    client.BaseAddress = WhiteListClient.BaseAddress;
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient<IExchangeRateSource, NbpExchangeRateSource>(client =>
{
    client.BaseAddress = NbpExchangeRateSource.BaseAddress;
    client.Timeout = TimeSpan.FromSeconds(3);
});

builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();
builder.Services.AddSingleton<IUserPushProvider, SignalRPushProvider>();
builder.Services.AddSingleton<ManualSyncJobs>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ManualSyncJobs>());

// UseForwardedHeaders (called in the pipeline below) populates RemoteIpAddress with
// the real client IP before any rate-limit policy runs, so it is safe to partition on
// RemoteIpAddress here. Never read X-Forwarded-For directly — it is client-controllable.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, ct) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
            new { code = "rate_limited", error = "Zbyt wiele prób. Spróbuj ponownie później." }, ct));
    };

    options.AddPolicy("setup-auth", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(15),
            PermitLimit = 5,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.AddPolicy("setup-apply", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(15),
            PermitLimit = 5,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.AddPolicy("register", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(15),
            PermitLimit = 5,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.AddPolicy(SystemSettingsController.RateLimitPolicy, context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(15),
            PermitLimit = 10,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.AddPolicy(OpenKSeF.Api.Controllers.NipLookupController.RateLimitPolicy, context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromHours(1),
            PermitLimit = 30,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.AddPolicy(CredentialsController.ManualSyncRateLimitPolicy, context =>
    {
        var user = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? context.User.FindFirst("sub")?.Value;
        var partition = user is null ? $"ip:{context.Connection.RemoteIpAddress}" : $"user:{user}";
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(15),
            PermitLimit = 6,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 120,
            SegmentsPerWindow = 6,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

builder.Services.AddControllers();

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

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>();

var app = builder.Build();

// Before migrations and adoption, because a refused rotation must write nothing.
if (args.Contains("rotate-encryption-key"))
{
    using var scope = app.Services.CreateScope();
    return await EncryptionKeyMigration.RunRotateCommandAsync(
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), app.Configuration, Console.Out);
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var startupLogger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("OpenKSeF.Api.Migrations");
    await MigrationHelper.ApplyMigrationsIdempotentlyAsync(db, startupLogger);
    if (!await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, app.Configuration, startupLogger))
    {
        await Log.CloseAndFlushAsync();
        return 1;
    }
    EmailService.WarnIfPlaintextAuth(scope.ServiceProvider.GetRequiredService<IOptions<EmailOptions>>().Value, startupLogger);
}

var systemConfig = app.Services.GetRequiredService<ISystemConfigService>();
await systemConfig.LoadCacheAsync();

try
{
    app.Services.ApplyStartupKSeFEnvironment(app.Configuration);
}
catch (InvalidOperationException ex)
{
    Log.Fatal("{Message}", ex.Message);
    await Log.CloseAndFlushAsync();
    return 1;
}

var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = app.Configuration.GetValue("ForwardedHeaders:ForwardLimit", 1)
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
var trustedNetworks = app.Configuration["ForwardedHeaders:KnownNetworks"];
foreach (var network in (string.IsNullOrWhiteSpace(trustedNetworks) ? "127.0.0.0/8,::1/128,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16" : trustedNetworks)
    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
    forwardedHeaders.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
app.UseForwardedHeaders(forwardedHeaders);

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
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge && !context.Response.HasStarted)
    {
        var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize;
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        await context.Response.WriteAsJsonAsync(new
        {
            error = limit is <= 1024 * 1024
                ? $"Plik jest za duży. Maksymalny rozmiar to {limit / 1024} KB."
                : "Żądanie jest za duże."
        });
    }
    catch (EncryptionKeyUnavailableException) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { error = CredentialsController.EncryptionKeyUnavailableMessage });
    }
});
app.UseAuthentication();
// After authentication so the manual-sync policy can partition by user; anonymous requests are still limited before the 401.
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHealthChecks("/health");

app.Run();
return 0;

public partial class Program { }
