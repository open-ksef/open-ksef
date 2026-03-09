using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Portal.Authorization;
using OpenKSeF.Portal.Components;
using OpenKSeF.Portal.Authentication;
using OpenKSeF.Portal.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var connectionString = builder.Configuration.GetConnectionString("Db");
ArgumentException.ThrowIfNullOrWhiteSpace(connectionString, "ConnectionStrings:Db");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString);
    }

    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
    }
});

builder.Services.AddPortalAuthentication(builder.Configuration);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("TenantAccess", policy => policy.Requirements.Add(new TenantAccessRequirement()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantResolver, TenantResolver>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ITenantCrudService, TenantCrudService>();
builder.Services.AddScoped<ICredentialStatusService, CredentialStatusService>();
builder.Services.AddScoped<IInvoiceListService, InvoiceListService>();
builder.Services.AddScoped<IInvoiceDetailService, InvoiceDetailService>();
builder.Services.AddScoped<IDeviceTokenOverviewService, DeviceTokenOverviewService>();
builder.Services.AddScoped<IAuthorizationHandler, TenantAccessHandler>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var startupLogger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("OpenKSeF.Portal.Migrations");
    await MigrationHelper.ApplyMigrationsIdempotentlyAsync(db, startupLogger);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapPortalAuthenticationEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
