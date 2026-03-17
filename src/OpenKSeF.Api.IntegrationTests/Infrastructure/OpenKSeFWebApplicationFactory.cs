using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OpenKSeF.Api.IntegrationTests.Infrastructure;

public sealed class OpenKSeFWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly TestcontainersFixture _fixture;

    public OpenKSeFWebApplicationFactory(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Db", _fixture.PostgresConnectionString);
        builder.UseSetting("Auth:Authority", _fixture.KeycloakAuthority);
        builder.UseSetting("Auth:PublicAuthority", _fixture.KeycloakAuthority);
        builder.UseSetting("Auth:ServiceAccount:ClientId", "openksef-api");
        builder.UseSetting("Auth:ServiceAccount:ClientSecret", "test-api-client-secret");
        builder.UseSetting("KSeF:Environment", "test");
        builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");

        builder.UseEnvironment("Development");
    }
}
