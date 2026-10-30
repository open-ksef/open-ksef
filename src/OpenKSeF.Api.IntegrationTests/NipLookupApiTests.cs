using System.Net;
using OpenKSeF.Api.IntegrationTests.Infrastructure;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class NipLookupApiTests(TestcontainersFixture fixture)
{
    [Fact]
    public async Task Lookup_WithoutToken_Returns401()
    {
        using var factory = new OpenKSeFWebApplicationFactory(fixture);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/nip-lookup/5261040828");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Lookup_InvalidNip_Returns400()
    {
        using var factory = new OpenKSeFWebApplicationFactory(fixture);
        using var client = factory.CreateClient();
        client.SetBearerToken(await fixture.GetAccessTokenAsync());

        var response = await client.GetAsync("/api/nip-lookup/1234567890");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
