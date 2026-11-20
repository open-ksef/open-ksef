using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace OpenKSeF.Api.Tests;

public class AppSettingsLoggingTests
{
    [Fact]
    public void ShippedAppSettings_DropHealthCheckWarnings_KeepErrorsAndOtherWarnings()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .Build();
        var sink = new CapturingSink();
        using var serilog = new LoggerConfiguration().ReadFrom.Configuration(configuration).WriteTo.Sink(sink).CreateLogger();
        using var factory = new SerilogLoggerFactory(serilog);

        var healthChecks = factory.CreateLogger("Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService");
        healthChecks.LogWarning("Health check sync-freshness with status Degraded");
        healthChecks.LogError("Health check database with status Unhealthy");
        factory.CreateLogger("OpenKSeF.Api.Services.SyncFreshnessHealthCheck").LogWarning("Sync freshness degraded");

        Assert.Equal(
            ["Health check database with status Unhealthy", "Sync freshness degraded"],
            sink.Events.Select(e => e.MessageTemplate.Text));
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("worker.appsettings.json")]
    public void ShippedAppSettings_DropHttpClientRequestUris(string file)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, file))
            .Build();
        var sink = new CapturingSink();
        using var serilog = new LoggerConfiguration().ReadFrom.Configuration(configuration).WriteTo.Sink(sink).CreateLogger();
        using var factory = new SerilogLoggerFactory(serilog);

        factory.CreateLogger("System.Net.Http.HttpClient.Default.LogicalHandler").LogInformation("Start processing HTTP request GET https://api.ksef.mf.gov.pl/v2/tokens/REF");
        factory.CreateLogger("System.Net.Http.HttpClient.Default.LogicalHandler").LogWarning("kept");

        Assert.Equal(["kept"], sink.Events.Select(e => e.MessageTemplate.Text));
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("worker.appsettings.json")]
    public void ShippedAppSettings_DropEfCommandText_KeepEfErrorsAndAppInformation(string file)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, file))
            .Build();
        var sink = new CapturingSink();
        using var serilog = new LoggerConfiguration().ReadFrom.Configuration(configuration).WriteTo.Sink(sink).CreateLogger();
        using var factory = new SerilogLoggerFactory(serilog);

        var efCommand = factory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");
        efCommand.LogInformation("Executed DbCommand (2ms) SELECT i.\"Id\" FROM \"IssuedInvoices\" AS i");
        efCommand.LogError("Failed executing DbCommand");
        factory.CreateLogger("OpenKSeF.Worker.Services.InvoiceSyncService").LogInformation("Sync cycle processed 0 tenants");

        Assert.Equal(
            ["Failed executing DbCommand", "Sync cycle processed 0 tenants"],
            sink.Events.Select(e => e.MessageTemplate.Text));
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
