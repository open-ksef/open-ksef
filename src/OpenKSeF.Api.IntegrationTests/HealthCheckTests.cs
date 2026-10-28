using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Domain.Data;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class HealthCheckTests : IDisposable
{
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public HealthCheckTests(TestcontainersFixture fixture)
    {
        _factory = new OpenKSeFWebApplicationFactory(fixture);
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_ReturnsNotFound_InProduction()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void SensitiveDataLogging_IsOff_ByDefault()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();

        Assert.False(options.FindExtension<CoreOptionsExtension>()!.IsSensitiveDataLoggingEnabled);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
