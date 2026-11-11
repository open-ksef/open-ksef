using System.Net;
using KSeF.Client.Api.Services;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.DI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Domain.Data;
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
        Assert.Equal(url, Sync.DependencyInjection.ResolveKSeFEnvironment(value));
        Assert.Equal(key, Sync.DependencyInjection.NormalizeKSeFEnvironmentKey(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoValue_IsTest(string? value)
    {
        Assert.Equal(KsefEnvironmentsUris.TEST, Sync.DependencyInjection.ResolveKSeFEnvironment(value));
        Assert.Equal("test", Sync.DependencyInjection.NormalizeKSeFEnvironmentKey(value));
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
        var resolve = Assert.Throws<InvalidOperationException>(() => Sync.DependencyInjection.ResolveKSeFEnvironment(value));
        Assert.Contains($"Unknown KSeF environment '{value}'", resolve.Message);
        Assert.Throws<InvalidOperationException>(() => Sync.DependencyInjection.NormalizeKSeFEnvironmentKey(value));
    }

    [Theory]
    [InlineData("https://api.ksef.mf.gov.pl/v2/invoices", true)]
    [InlineData("https://API.KSEF.MF.GOV.PL/v2", true)]
    [InlineData("https://qr.ksef.mf.gov.pl/invoice", true)]
    [InlineData("https://api-latarnia.ksef.mf.gov.pl/status", true)]
    [InlineData("https://ksef.mf.gov.pl/api", true)]
    [InlineData("https://api-test.ksef.mf.gov.pl/v2", false)]
    [InlineData("https://api-demo.ksef.mf.gov.pl/v2", false)]
    [InlineData("https://qr-test.ksef.mf.gov.pl/invoice", false)]
    [InlineData("https://api-latarnia-demo.ksef.mf.gov.pl/status", false)]
    [InlineData("https://ksef-test.mf.gov.pl/api", false)]
    [InlineData("https://api.ksef.mf.gov.pl.example.com/", false)]
    [InlineData("http://keycloak:8080/auth", false)]
    public async Task Guard_BlocksEveryProductionHost_AndNothingElse(string url, bool blocked)
    {
        using var invoker = new HttpMessageInvoker(new KSeFProductionGuard { InnerHandler = new AlwaysOk() });
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (blocked)
            await Assert.ThrowsAsync<InvalidOperationException>(() => invoker.SendAsync(request, CancellationToken.None));
        else
            Assert.Equal(HttpStatusCode.OK, (await invoker.SendAsync(request, CancellationToken.None)).StatusCode);
    }

    private sealed class AlwaysOk : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private static IConfiguration ForbidProduction(string? value) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [Sync.DependencyInjection.ForbidProductionKey] = value })
        .Build();

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public void AddSyncServices_GuardsHttpClients_OnlyWithTheSignal(string? signal, bool guarded)
    {
        using var provider = new ServiceCollection().AddLogging().AddSyncServices(ForbidProduction(signal)).BuildServiceProvider();

        HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler();
        var found = false;
        while (handler is DelegatingHandler delegating)
        {
            found |= delegating is KSeFProductionGuard;
            handler = delegating.InnerHandler;
        }

        Assert.Equal(guarded, found);
    }

    [Theory]
    [InlineData("production")]
    [InlineData("prod")]
    [InlineData("https://api.ksef.mf.gov.pl/v2")]
    [InlineData("https://ksef.podatki.gov.pl/api")]
    public void StartupResolve_ProductionWithTheSignal_Refuses_WithoutItIsProduction(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Sync.DependencyInjection.ResolveStartupKSeFEnvironment(value, ForbidProduction("true")));

        Assert.Contains(Sync.DependencyInjection.ForbidProductionKey, error.Message);
        Assert.Equal(KsefEnvironmentsUris.PROD, Sync.DependencyInjection.ResolveStartupKSeFEnvironment(value, ForbidProduction(null)));
        Assert.Equal(KsefEnvironmentsUris.PROD, Sync.DependencyInjection.ResolveStartupKSeFEnvironment(value, ForbidProduction("false")));
    }

    [Theory]
    [InlineData("test", KsefEnvironmentsUris.TEST)]
    [InlineData("demo", KsefEnvironmentsUris.DEMO)]
    public void StartupResolve_TestOrDemoWithTheSignal_Starts(string value, string url)
    {
        Assert.Equal(url, Sync.DependencyInjection.ResolveStartupKSeFEnvironment(value, ForbidProduction("true")));
    }

    [Fact]
    public void ForbidProductionSignal_IsOnlyInTheDevCompose()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repo.FullName, "docker-compose.dev.yml")))
            repo = repo.Parent!;

        Assert.Equal(2, File.ReadLines(Path.Combine(repo.FullName, "docker-compose.dev.yml")).Count(l => l.Contains("KSeF__ForbidProduction: \"true\"")));
        Assert.DoesNotContain("ForbidProduction", File.ReadAllText(Path.Combine(repo.FullName, "docker-compose.yml")));
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
}
