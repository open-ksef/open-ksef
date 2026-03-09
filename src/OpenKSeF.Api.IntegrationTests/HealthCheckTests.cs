using System.Net;
using OpenKSeF.Api.IntegrationTests.Infrastructure;

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
    public async Task Swagger_ReturnsOk_InDevelopment()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
