using System.Net;
using OpenKSeF.Api.IntegrationTests.Infrastructure;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class AuthenticationTests : IDisposable
{
    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthenticationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture);
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/tenants");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidToken_Returns200()
    {
        var token = await _fixture.GetAccessTokenAsync();
        _client.SetBearerToken(token);

        var response = await _client.GetAsync("/api/tenants");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
