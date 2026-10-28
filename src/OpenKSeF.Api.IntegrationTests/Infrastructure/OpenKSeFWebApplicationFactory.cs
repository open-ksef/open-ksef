using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace OpenKSeF.Api.IntegrationTests.Infrastructure;

public sealed class OpenKSeFWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestEncryptionKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

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
        builder.UseSetting("Auth:RequireHttpsMetadata", "false");
        builder.UseSetting("ENCRYPTION_KEY", TestEncryptionKey);
        builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Production");

        builder.UseEnvironment("Production");
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
