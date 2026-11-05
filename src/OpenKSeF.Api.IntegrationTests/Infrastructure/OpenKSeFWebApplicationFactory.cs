using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Sync;

namespace OpenKSeF.Api.IntegrationTests.Infrastructure;

public sealed class OpenKSeFWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestEncryptionKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    private readonly TestcontainersFixture _fixture;
    private readonly TimeProvider? _timeProvider;

    public StubExchangeRateSource ExchangeRates { get; } = new();

    public OpenKSeFWebApplicationFactory(TestcontainersFixture fixture, TimeProvider? timeProvider = null)
    {
        _fixture = fixture;
        _timeProvider = timeProvider;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Db", _fixture.PostgresConnectionString);
        builder.UseSetting("Auth:Authority", _fixture.KeycloakAuthority);
        builder.UseSetting("Auth:PublicAuthority", _fixture.KeycloakAuthority);
        builder.UseSetting("Auth:ServiceAccount:ClientId", "openksef-api");
        builder.UseSetting("Auth:ServiceAccount:ClientSecret", "test-api-client-secret");
        builder.UseSetting("KSeF:Environment", "test");
        builder.UseSetting("Auth:RequireHttpsMetadata", "false");
        builder.UseSetting("ENCRYPTION_KEY", TestEncryptionKey);
        builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Production");

        builder.UseEnvironment("Production");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IExchangeRateSource>(ExchangeRates);
            services.ConfigureHttpClientDefaults(client => client.AddHttpMessageHandler(() => new KSeFProductionGuard()));
        });

        if (_timeProvider is not null)
            builder.ConfigureTestServices(services => services.AddSingleton(_timeProvider));
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });

        return base.CreateHost(builder);
    }
}

/// <summary>Replaces NBP in every test host, so no test reaches api.nbp.pl. A null quote behaves like NBP being down.</summary>
public sealed class StubExchangeRateSource : IExchangeRateSource
{
    public ExchangeRateQuote? Quote { get; set; }

    public List<(string Currency, DateOnly Date)> Calls { get; } = [];

    public Task<ExchangeRateQuote?> LatestMidBeforeAsync(string currency, DateOnly date, CancellationToken ct = default)
    {
        Calls.Add((currency, date));
        return Task.FromResult(Quote);
    }
}
