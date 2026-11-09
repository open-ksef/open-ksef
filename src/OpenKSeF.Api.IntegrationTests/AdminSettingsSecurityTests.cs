using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public sealed class AdminSettingsSecurityTests : IAsyncLifetime
{
    private readonly TestcontainersFixture _fixture;
    private readonly string _database = $"admin_settings_{Guid.NewGuid():N}";
    private string _connection = null!;

    public AdminSettingsSecurityTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await using var admin = new NpgsqlConnection(_fixture.PostgresConnectionString);
        await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE {_database}", admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_fixture.PostgresConnectionString) { Database = _database }.ConnectionString;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private WebApplicationFactory<Program> Factory(int? forwardLimit = null, string? knownNetworks = null) => new OpenKSeFWebApplicationFactory(_fixture)
        .WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Db", _connection);
            if (forwardLimit is not null)
                builder.UseSetting("ForwardedHeaders:ForwardLimit", forwardLimit.ToString());
            if (knownNetworks is not null)
                builder.UseSetting("ForwardedHeaders:KnownNetworks", knownNetworks);
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, GatewayAddressFilter>());
        });

    private static async Task InitializeAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<ISystemConfigService>();
        await config.SetValuesAsync(new Dictionary<string, string> { [SystemConfigKeys.IsInitialized] = "true" });
    }

    private static Task<HttpResponseMessage> PostSettingsAsync(HttpClient client, string forwardedFor, string? token = null, string password = "wrong")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/system/settings")
        {
            Content = JsonContent.Create(new { kcAdminUsername = "admin", kcAdminPassword = password }),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        if (token is not null)
            request.Headers.Authorization = new("Bearer", token);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task UserWithoutAdminRole_Gets403_BeforeThePasswordCheck()
    {
        await using var factory = Factory();
        await InitializeAsync(factory);
        using var client = factory.CreateClient();

        var plain = await PostSettingsAsync(client, "203.0.113.1", await _fixture.GetAccessTokenAsync("plainuser"));
        var admin = await PostSettingsAsync(client, "203.0.113.2", await _fixture.GetAccessTokenAsync());

        Assert.Equal(HttpStatusCode.Forbidden, plain.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, admin.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0.0.0.0/0,::/0")]
    public async Task ForgedForwardedForBeyondTheLimit_SharesThePartition_OtherClientsAreNotLimited(string? knownNetworks)
    {
        await using var factory = Factory(knownNetworks: knownNetworks);
        using var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await PostSettingsAsync(client, $"198.51.100.{i}, 203.0.113.10")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostSettingsAsync(client, "198.51.100.99, 203.0.113.10")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostSettingsAsync(client, "203.0.113.11")).StatusCode);
    }

    [Fact]
    public async Task ForwardLimit3_TakesTheClientBehindTwoTrustedProxies()
    {
        await using var factory = Factory(forwardLimit: 3);
        using var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
            await PostSettingsAsync(client, "203.0.113.20, 10.0.0.2, 10.0.0.3");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostSettingsAsync(client, "203.0.113.20, 10.0.0.2, 10.0.0.3")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostSettingsAsync(client, "203.0.113.21, 10.0.0.2, 10.0.0.3")).StatusCode);
    }

    private sealed class GatewayAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("172.18.0.10");
                return nextMiddleware();
            });
            next(app);
        };
    }
}
