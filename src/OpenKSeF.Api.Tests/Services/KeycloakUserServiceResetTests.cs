using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Tests.Services;

public class KeycloakUserServiceResetTests
{
    private const string Admin = "http://keycloak/auth/admin/realms/openksef/users";

    [Fact]
    public async Task ResetTwoFactor_DeleteFailsAfterOneRemoved_WritesPartialAuditRow_ReturnsUnavailable()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"resource_access":{"realm-management":{"roles":["manage-users"]}}}""")).TrimEnd('=');
        var handler = new StubHandler(request => (request.Method.Method, request.RequestUri!.ToString()) switch
        {
            ("POST", _) => (HttpStatusCode.OK, $$"""{"access_token":"h.{{payload}}.s"}"""),
            ("GET", $"{Admin}/u1") => (HttpStatusCode.OK, """{"id":"u1","email":"target@openksef.test"}"""),
            ("GET", $"{Admin}/u1/credentials") => (HttpStatusCode.OK, """[{"id":"c1","type":"otp"},{"id":"c2","type":"recovery-authn-codes"}]"""),
            ("DELETE", $"{Admin}/u1/credentials/c1") => (HttpStatusCode.NoContent, ""),
            _ => (HttpStatusCode.InternalServerError, ""),
        });
        var database = Guid.NewGuid().ToString();
        var services = new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(database))
            .BuildServiceProvider();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("keycloak").Returns(new HttpClient(handler));
        var systemConfig = Substitute.For<ISystemConfigService>();
        systemConfig.GetValue(SystemConfigKeys.ApiClientSecret).Returns("secret");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Authority"] = "http://keycloak/auth/realms/openksef" })
            .Build();
        var service = new KeycloakUserService(factory, configuration, systemConfig,
            services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<KeycloakUserService>.Instance);

        var result = await service.ResetTwoFactorAsync("u1", "actor", "admin@openksef.test");

        Assert.Equal(TwoFactorResetResult.KeycloakUnavailable, result);
        using var scope = services.CreateScope();
        var audit = Assert.Single(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditEvents);
        Assert.Equal(AuditEvent.TwoFactorResetPartial, audit.Action);
        Assert.Equal("target@openksef.test", audit.TargetEmail);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = respond(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
