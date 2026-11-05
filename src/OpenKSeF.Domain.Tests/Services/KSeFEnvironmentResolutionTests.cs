using System.Diagnostics;
using KSeF.Client.Api.Services;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.DI;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;
using OpenKSeF.Worker.Services;

namespace OpenKSeF.Domain.Tests.Services;

public class KSeFEnvironmentResolutionTests
{
    [Theory]
    [InlineData("test", KsefEnvironmentsUris.TEST, "test")]
    [InlineData("TEST", KsefEnvironmentsUris.TEST, "test")]
    [InlineData("  test  ", KsefEnvironmentsUris.TEST, "test")]
    [InlineData("demo", KsefEnvironmentsUris.DEMO, "demo")]
    [InlineData("Demo", KsefEnvironmentsUris.DEMO, "demo")]
    [InlineData("production", KsefEnvironmentsUris.PROD, "production")]
    [InlineData("PRODUCTION", KsefEnvironmentsUris.PROD, "production")]
    [InlineData(" production ", KsefEnvironmentsUris.PROD, "production")]
    [InlineData("prod", KsefEnvironmentsUris.PROD, "production")]
    [InlineData("https://ksef-test.mf.gov.pl/api", KsefEnvironmentsUris.TEST, "test")]
    [InlineData("HTTPS://KSEF-TEST.MF.GOV.PL/API/", KsefEnvironmentsUris.TEST, "test")]
    [InlineData("https://ksef-test.mf.gov.pl", KsefEnvironmentsUris.TEST, "test")]
    [InlineData("ksef-test.mf.gov.pl", KsefEnvironmentsUris.TEST, "test")]
    [InlineData("https://api-test.ksef.mf.gov.pl", KsefEnvironmentsUris.TEST, "test")]
    [InlineData("https://api-test.ksef.mf.gov.pl/v2", KsefEnvironmentsUris.TEST, "test")]
    [InlineData("api-test.ksef.mf.gov.pl/v2", KsefEnvironmentsUris.TEST, "test")]
    [InlineData("https://api-demo.ksef.mf.gov.pl", KsefEnvironmentsUris.DEMO, "demo")]
    [InlineData("https://api-demo.ksef.mf.gov.pl/v2", KsefEnvironmentsUris.DEMO, "demo")]
    [InlineData("https://ksef-demo.mf.gov.pl", KsefEnvironmentsUris.DEMO, "demo")]
    [InlineData("https://ksef-demo.mf.gov.pl/api", KsefEnvironmentsUris.DEMO, "demo")]
    [InlineData("ksef-demo.mf.gov.pl", KsefEnvironmentsUris.DEMO, "demo")]
    [InlineData("https://ksef.podatki.gov.pl/api", KsefEnvironmentsUris.PROD, "production")]
    [InlineData("https://ksef.podatki.gov.pl", KsefEnvironmentsUris.PROD, "production")]
    [InlineData("ksef.podatki.gov.pl", KsefEnvironmentsUris.PROD, "production")]
    [InlineData("https://api.ksef.mf.gov.pl", KsefEnvironmentsUris.PROD, "production")]
    [InlineData("https://api.ksef.mf.gov.pl/v2", KsefEnvironmentsUris.PROD, "production")]
    [InlineData("api.ksef.mf.gov.pl/v2", KsefEnvironmentsUris.PROD, "production")]
    public void KnownValue_ResolvesToItsUrlAndKey(string value, string url, string key)
    {
        Assert.Equal(url, DependencyInjection.ResolveKSeFEnvironment(value));
        Assert.Equal(key, DependencyInjection.NormalizeKSeFEnvironmentKey(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoValue_IsTest(string? value)
    {
        Assert.Equal(KsefEnvironmentsUris.TEST, DependencyInjection.ResolveKSeFEnvironment(value));
        Assert.Equal("test", DependencyInjection.NormalizeKSeFEnvironmentKey(value));
    }

    [Theory]
    [InlineData("https://ksef.mf.gov.pl/api")]
    [InlineData("https://ksef.mf.gov.pl")]
    [InlineData("ksef.mf.gov.pl")]
    [InlineData("tset")]
    [InlineData("produkcja")]
    [InlineData("https://proxy.local")]
    [InlineData("https://proxy-demo.local")]
    [InlineData("https://api.ksef.mf.gov.pl.example.com")]
    [InlineData("https://prod")]
    [InlineData("https://production/api")]
    [InlineData("https://test")]
    [InlineData("https://demo")]
    [InlineData("production/")]
    [InlineData("not a url")]
    public void UnknownValue_Throws(string value)
    {
        var resolve = Assert.Throws<InvalidOperationException>(() => DependencyInjection.ResolveKSeFEnvironment(value));
        Assert.Contains($"Unknown KSeF environment '{value}'", resolve.Message);
        Assert.Throws<InvalidOperationException>(() => DependencyInjection.NormalizeKSeFEnvironmentKey(value));
    }

    private static async Task<(KSeFRuntimeConfig Runtime, KSeFClientOptions Options, ICryptographyService Cryptography)> RuntimeAsync(
        Dictionary<string, string>? rows = null,
        Dictionary<string, string?>? settings = null)
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName), ServiceLifetime.Transient)
            .BuildServiceProvider();
        var systemConfig = new SystemConfigService(
            services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build(),
            NullLogger<SystemConfigService>.Instance);
        if (rows is not null)
            await systemConfig.SetValuesAsync(rows);

        var options = new KSeFClientOptions { BaseUrl = KsefEnvironmentsUris.TEST };
        var cryptography = Substitute.For<ICryptographyService>();
        return (new KSeFRuntimeConfig(systemConfig, options, cryptography, NullLogger<KSeFRuntimeConfig>.Instance), options, cryptography);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("test")]
    public async Task Refresh_ProductionRow_IsProduction_WhateverTheEnvVariableSays(string? envBaseUrl)
    {
        var (runtime, options, _) = await RuntimeAsync(
            new() { [SystemConfigKeys.KSeFEnvironment] = "production" },
            new() { ["KSeF:BaseUrl"] = envBaseUrl });

        Assert.Equal("production", await runtime.RefreshAsync(CancellationToken.None));
        Assert.Equal(KsefEnvironmentsUris.PROD, options.BaseUrl);
    }

    [Fact]
    public async Task Refresh_RowWinsOverEnvironmentVariable()
    {
        var (runtime, options, _) = await RuntimeAsync(
            new() { [SystemConfigKeys.KSeFEnvironment] = "test" },
            new() { ["KSeF:Environment"] = "production", ["KSeF:BaseUrl"] = "production" });

        Assert.Equal("test", await runtime.RefreshAsync(CancellationToken.None));
        Assert.Equal(KsefEnvironmentsUris.TEST, options.BaseUrl);
    }

    [Fact]
    public async Task Refresh_KeyPrecedence_EnvironmentVariableKeyWinsOverLegacyBaseUrlRow()
    {
        var (runtime, options, _) = await RuntimeAsync(
            new() { [SystemConfigKeys.KSeFBaseUrl] = "https://ksef.podatki.gov.pl/api" },
            new() { ["KSeF:Environment"] = "demo" });

        Assert.Equal("demo", await runtime.RefreshAsync(CancellationToken.None));
        Assert.Equal(KsefEnvironmentsUris.DEMO, options.BaseUrl);
    }

    [Fact]
    public async Task Refresh_LegacyBaseUrlRowWinsOverBaseUrlVariable()
    {
        var (runtime, options, _) = await RuntimeAsync(
            new() { [SystemConfigKeys.KSeFBaseUrl] = "https://ksef.podatki.gov.pl/api" },
            new() { ["KSeF:BaseUrl"] = "test" });

        Assert.Equal("production", await runtime.RefreshAsync(CancellationToken.None));
        Assert.Equal(KsefEnvironmentsUris.PROD, options.BaseUrl);
    }

    [Fact]
    public async Task Refresh_NoConfigAtAll_StaysOnTest()
    {
        var (runtime, options, _) = await RuntimeAsync();

        Assert.Equal("test", await runtime.RefreshAsync(CancellationToken.None));
        Assert.Equal(KsefEnvironmentsUris.TEST, options.BaseUrl);
    }

    [Fact]
    public async Task Refresh_UnknownRow_Throws_BaseUrlUnchanged()
    {
        var (runtime, options, cryptography) = await RuntimeAsync(new() { [SystemConfigKeys.KSeFEnvironment] = "tset" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RefreshAsync(CancellationToken.None));

        Assert.Equal(KsefEnvironmentsUris.TEST, options.BaseUrl);
        await cryptography.DidNotReceiveWithAnyArgs().ForceRefreshAsync(default);
    }

    [Theory]
    [InlineData("tset", null)]
    [InlineData(null, "https://ksef.mf.gov.pl/api")]
    public async Task WorkerProcess_UnknownEnvironment_ExitsNonZero_BeforeAnyKSeFCall(string? row, string? envBaseUrl)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"openksef-worker-env-{Guid.NewGuid():N}.db");
        try
        {
            await using (var services = new ServiceCollection()
                .AddDbContext<ApplicationDbContext>(o => o
                    .UseSqlite($"Data Source={dbPath}")
                    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)))
                .BuildServiceProvider())
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.MigrateAsync();
                db.SystemConfigs.Add(new SystemConfig { Key = SystemConfigKeys.IsInitialized, Value = "true", UpdatedAt = DateTime.UtcNow });
                if (row is not null)
                    db.SystemConfigs.Add(new SystemConfig { Key = SystemConfigKeys.KSeFEnvironment, Value = row, UpdatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }

            var start = new ProcessStartInfo("dotnet")
            {
                ArgumentList = { typeof(KSeFRuntimeConfig).Assembly.Location },
                WorkingDirectory = Path.GetDirectoryName(typeof(KSeFRuntimeConfig).Assembly.Location)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Environment =
                {
                    ["ConnectionStrings__Db"] = $"Data Source={dbPath}",
                    ["ENCRYPTION_KEY"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
                    ["KSeF__BaseUrl"] = envBaseUrl ?? "",
                },
            };
            using var worker = Process.Start(start)!;
            var stdout = worker.StandardOutput.ReadToEndAsync();
            var stderr = worker.StandardError.ReadToEndAsync();
            try
            {
                await worker.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);
            }
            finally
            {
                worker.Kill(entireProcessTree: true);
            }

            Assert.NotEqual(0, worker.ExitCode);
            Assert.Contains("Unknown KSeF environment", await stdout + await stderr);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }
}
