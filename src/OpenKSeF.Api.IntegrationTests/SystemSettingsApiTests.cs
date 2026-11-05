using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using KSeF.Client.Api.Services;
using KSeF.Client.DI;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class SystemSettingsApiTests : IAsyncLifetime
{
    private const string SmtpUser = "mailer";
    private const string SmtpPassword = "stored-smtp-password";
    private const string RelayKey = "stored-relay-key";

    // Accepts any login, keeps every AUTH line in a file and takes one mail per connection.
    private const string FakeSmtpServer = """
        cat > /h.sh <<'E'
        printf '220 t\r\n'
        while read -r l; do
          l=$(printf %s "$l" | tr -d '\r')
          case "$l" in
            EHLO*|HELO*) printf '250-t\r\n250 AUTH PLAIN\r\n';;
            AUTH*) echo "$l" >> /tmp/auth.log; printf '235 ok\r\n';;
            DATA*) printf '354 go\r\n'; while read -r d; do [ "$(printf %s "$d" | tr -d '\r')" = "." ] && break; done; printf '250 ok\r\n';;
            QUIT*) printf '221 bye\r\n'; exit 0;;
            *) printf '250 ok\r\n';;
          esac
        done
        E
        touch /tmp/auth.log
        echo ready
        while true; do nc -l -p 1025 -e sh /h.sh; done
        """;

    private readonly TestcontainersFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly string _database = $"settings_{Guid.NewGuid():N}";

    public SystemSettingsApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await using var admin = new NpgsqlConnection(_fixture.PostgresConnectionString);
        await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE {_database}", admin).ExecuteNonQueryAsync();

        var connection = new NpgsqlConnectionStringBuilder(_fixture.PostgresConnectionString) { Database = _database }.ConnectionString;
        _factory = new OpenKSeFWebApplicationFactory(_fixture)
            .WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:Db", connection));
        _client = _factory.CreateClient();
        _client.SetBearerToken(await _fixture.GetAccessTokenAsync());
        await SetConfigAsync(new() { [SystemConfigKeys.IsInitialized] = "true", [SystemConfigKeys.KSeFEnvironment] = "test" });
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task SetConfigAsync(Dictionary<string, string> values)
    {
        using var scope = _factory.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<ISystemConfigService>();
        await config.SetValuesAsync(values);
        await config.RefreshCacheAsync();
    }

    private Task<HttpResponseMessage> PutSettingsAsync(object body) => _client.PutAsJsonAsync("/api/system/settings", body);

    private async Task<Guid> SeedCredentialAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var tenant = new Tenant { Id = Guid.NewGuid(), UserId = "settings-user", Nip = "1234563218", CreatedAt = now, UpdatedAt = now };
        db.Tenants.Add(tenant);
        db.KSeFCredentials.Add(new KSeFCredential { Id = Guid.NewGuid(), TenantId = tenant.Id, EncryptedToken = "encrypted", CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private int CredentialCount()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().KSeFCredentials.Count();
    }

    [Fact]
    public async Task GetSettings_SaysTheRelayKeyIsConfigured_WithoutReturningIt()
    {
        await SetConfigAsync(new() { [SystemConfigKeys.PushRelayUrl] = "https://relay.example", [SystemConfigKeys.PushRelayApiKey] = RelayKey });

        var body = await (await _client.PostAsJsonAsync("/api/system/settings", new { kcAdminUsername = "admin", kcAdminPassword = "admin" })).Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;

        Assert.True(json.GetProperty("pushRelayApiKeyConfigured").GetBoolean());
        Assert.False(json.TryGetProperty("pushRelayApiKey", out _));
        Assert.DoesNotContain(RelayKey, body);

        await SetConfigAsync(new() { [SystemConfigKeys.PushRelayApiKey] = "" });
        var cleared = await (await _client.PostAsJsonAsync("/api/system/settings", new { kcAdminUsername = "admin", kcAdminPassword = "admin" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(cleared.GetProperty("pushRelayApiKeyConfigured").GetBoolean());
    }

    [Fact]
    public async Task SavingWithoutTheRelayKey_KeepsIt()
    {
        await SetConfigAsync(new() { [SystemConfigKeys.PushRelayUrl] = "https://relay.example", [SystemConfigKeys.PushRelayApiKey] = RelayKey });

        var response = await PutSettingsAsync(new { kcAdminUsername = "admin", kcAdminPassword = "admin", pushRelayUrl = "https://relay.example" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(RelayKey, scope.ServiceProvider.GetRequiredService<ISystemConfigService>().GetValue(SystemConfigKeys.PushRelayApiKey));
    }

    [Fact]
    public async Task ChangingEnvironment_WithCredentials_AsksFirst_ThenWipesOnConfirmation()
    {
        await SeedCredentialAsync();

        var asked = await PutSettingsAsync(new { kcAdminUsername = "admin", kcAdminPassword = "admin", ksefEnvironment = "production" });

        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        var askedBody = await asked.Content.ReadFromJsonAsync<SettingsUpdateResponse>();
        Assert.True(askedBody!.RequiresConfirmation);
        Assert.False(askedBody.Success);
        Assert.Equal(1, CredentialCount());

        var confirmed = await PutSettingsAsync(new { kcAdminUsername = "admin", kcAdminPassword = "admin", ksefEnvironment = "production", confirmCredentialWipe = true });

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var confirmedBody = await confirmed.Content.ReadFromJsonAsync<SettingsUpdateResponse>();
        Assert.True(confirmedBody!.Success);
        Assert.False(confirmedBody.RequiresConfirmation);
        Assert.Equal(0, CredentialCount());
    }

    [Fact]
    public async Task ChangingEnvironment_WithInvoices_IsARefusal_NotAQuestion()
    {
        var tenantId = await SeedCredentialAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.InvoiceHeaders.Add(new InvoiceHeader
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                KSeFInvoiceNumber = "K-1",
                KSeFReferenceNumber = "R-1",
                VendorName = "V",
                VendorNip = "1112223344",
                IssueDate = DateTime.UtcNow,
                FirstSeenAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await PutSettingsAsync(new { kcAdminUsername = "admin", kcAdminPassword = "admin", ksefEnvironment = "production", confirmCredentialWipe = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<SettingsUpdateResponse>())!.RequiresConfirmation);
        Assert.Equal(1, CredentialCount());
    }

    private static object EnvironmentPut(string environment) =>
        new { kcAdminUsername = "admin", kcAdminPassword = "admin", ksefEnvironment = environment };

    private string? StoredEnvironment()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SystemConfigs
            .Single(c => c.Key == SystemConfigKeys.KSeFEnvironment).Value;
    }

    private WebApplicationFactory<Program> RestartedHost(params (string Key, string Value)[] settings) =>
        new OpenKSeFWebApplicationFactory(_fixture).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Db", new NpgsqlConnectionStringBuilder(_fixture.PostgresConnectionString) { Database = _database }.ConnectionString);
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        });

    [Theory]
    [InlineData("tset")]
    [InlineData("https://ksef.mf.gov.pl/api")]
    public async Task UnknownEnvironment_Is400_RowAndCredentialsIntact_AndTheNextStartWorks(string environment)
    {
        await SetConfigAsync(new() { [SystemConfigKeys.KSeFEnvironment] = "production" });
        await SeedCredentialAsync();

        var response = await PutSettingsAsync(EnvironmentPut(environment));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Unknown KSeF environment", (await response.Content.ReadFromJsonAsync<SettingsUpdateResponse>())!.Error);
        Assert.Equal("production", StoredEnvironment());
        Assert.Equal(1, CredentialCount());

        await using var restarted = RestartedHost();
        Assert.Equal(KsefEnvironmentsUris.PROD, restarted.Services.GetRequiredService<KSeFClientOptions>().BaseUrl);
    }

    [Theory]
    [InlineData("prod", "production", KsefEnvironmentsUris.PROD)]
    [InlineData("PRODUCTION", "production", KsefEnvironmentsUris.PROD)]
    [InlineData("https://ksef-test.mf.gov.pl/api", "test", KsefEnvironmentsUris.TEST)]
    public async Task KnownEnvironment_IsStoredAsItsCanonicalKey(string environment, string stored, string baseUrl)
    {
        var response = await PutSettingsAsync(EnvironmentPut(environment));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(stored, StoredEnvironment());
        Assert.Equal(baseUrl, _factory.Services.GetRequiredService<KSeFClientOptions>().BaseUrl);
    }

    [Fact]
    public async Task HostStart_ProductionRow_KSeFWarmupStopsAtTheTestGuard()
    {
        await SetConfigAsync(new() { [SystemConfigKeys.KSeFEnvironment] = "production" });
        var before = KSeFProductionGuard.Blocked;

        await using var restarted = RestartedHost();
        _ = restarted.Services;

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (KSeFProductionGuard.Blocked == before && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.True(KSeFProductionGuard.Blocked > before);
    }

    [Fact]
    public async Task HostStart_UnknownStoredEnvironment_Throws()
    {
        await SetConfigAsync(new() { [SystemConfigKeys.KSeFEnvironment] = "tset" });

        await using var restarted = RestartedHost();
        var error = Assert.ThrowsAny<Exception>(() => restarted.Services);

        Assert.Contains("Unknown KSeF environment 'tset'", error.Message);
    }

    [Theory]
    [InlineData("production", "test", KsefEnvironmentsUris.PROD)]
    [InlineData("test", "production", KsefEnvironmentsUris.TEST)]
    public async Task HostStart_StoredEnvironmentWinsOverEnvVariable(string row, string envVariable, string baseUrl)
    {
        await SetConfigAsync(new() { [SystemConfigKeys.KSeFEnvironment] = row });

        await using var restarted = RestartedHost(("KSeF:Environment", envVariable), ("KSeF:BaseUrl", envVariable));

        Assert.Equal(baseUrl, restarted.Services.GetRequiredService<KSeFClientOptions>().BaseUrl);
    }

    [Fact]
    public async Task HostStart_LegacyBaseUrlRow_ResolvesAndReadsAsItsKey_SavingTheSameKeepsCredentials()
    {
        await SetConfigAsync(new() { [SystemConfigKeys.KSeFEnvironment] = "", [SystemConfigKeys.KSeFBaseUrl] = "https://ksef.podatki.gov.pl/api" });

        await using var restarted = RestartedHost(("KSeF:Environment", ""));
        Assert.Equal(KsefEnvironmentsUris.PROD, restarted.Services.GetRequiredService<KSeFClientOptions>().BaseUrl);

        await SeedCredentialAsync();
        using var client = restarted.CreateClient();
        client.SetBearerToken(await _fixture.GetAccessTokenAsync());
        var settings = await (await client.PostAsJsonAsync("/api/system/settings", new { kcAdminUsername = "admin", kcAdminPassword = "admin" })).Content.ReadFromJsonAsync<SettingsResponse>();
        Assert.Equal("production", settings!.KSeFEnvironment);

        var response = await client.PutAsJsonAsync("/api/system/settings", EnvironmentPut("production"));

        Assert.False((await response.Content.ReadFromJsonAsync<SettingsUpdateResponse>())!.RequiresConfirmation);
        Assert.Equal(1, CredentialCount());
    }

    [Fact]
    public async Task HostStart_NoEnvironmentAnywhere_RunsOnTest()
    {
        await SetConfigAsync(new() { [SystemConfigKeys.KSeFEnvironment] = "" });

        await using var restarted = RestartedHost(("KSeF:Environment", ""));

        Assert.Equal(KsefEnvironmentsUris.TEST, restarted.Services.GetRequiredService<KSeFClientOptions>().BaseUrl);
    }

    [Fact]
    public async Task SavingWithoutTheSmtpPassword_KeycloakStillLogsInWithTheStoredOne()
    {
        var smtp = new ContainerBuilder()
            .WithImage("alpine:3.20")
            // A Windows checkout gives this literal CRLF line ends, and the script then drops the connection after its banner.
            .WithEntrypoint("sh", "-c", FakeSmtpServer.ReplaceLineEndings("\n"))
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("ready"))
            .Build();
        await smtp.StartAsync();
        try
        {
            object Config(string? password) => new
            {
                host = smtp.IpAddress,
                port = "1025",
                from = "noreply@openksef.test",
                starttls = false,
                ssl = false,
                auth = true,
                user = SmtpUser,
                password,
            };
            async Task<string[]> SendAndReadAuthLinesAsync()
            {
                await TriggerMailAsync();
                var log = await smtp.ExecAsync(["cat", "/tmp/auth.log"]);
                return log.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }
            async Task SaveAsync(string? password, bool clear = false)
            {
                var response = await PutSettingsAsync(new { kcAdminUsername = "admin", kcAdminPassword = "admin", smtp = clear ? null : Config(password), clearSmtp = clear });
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            static string Login(string password) => $"AUTH PLAIN {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{SmtpUser}\0{SmtpUser}\0{password}"))}";

            await SaveAsync(SmtpPassword);
            Assert.Equal([Login(SmtpPassword)], await SendAndReadAuthLinesAsync());

            await SaveAsync(null);
            Assert.Equal([Login(SmtpPassword), Login(SmtpPassword)], await SendAndReadAuthLinesAsync());

            await SaveAsync("");
            Assert.Equal([Login(SmtpPassword), Login(SmtpPassword), Login(SmtpPassword)], await SendAndReadAuthLinesAsync());

            await SaveAsync("typed-new-password");
            Assert.Equal(Login("typed-new-password"), (await SendAndReadAuthLinesAsync())[^1]);

            await SaveAsync(null, clear: true);
            await SaveAsync(null);
            await TriggerMailAsync(tolerateFailure: true);
            var afterClear = (await smtp.ExecAsync(["cat", "/tmp/auth.log"])).Stdout;
            Assert.DoesNotContain(Login("**********"), afterClear);
        }
        finally
        {
            await PutSettingsAsync(new { kcAdminUsername = "admin", kcAdminPassword = "admin", clearSmtp = true });
            await smtp.DisposeAsync();
        }
    }

    private async Task TriggerMailAsync(bool tolerateFailure = false)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _fixture.GetAdminTokenAsync());
        var realm = $"{_fixture.Keycloak.GetBaseAddress().ToString().TrimEnd('/')}/admin/realms/openksef";
        var user = (await http.GetFromJsonAsync<JsonElement[]>($"{realm}/users?username=testuser&exact=true"))!.Single();
        var url = $"{realm}/users/{user.GetProperty("id").GetString()}/execute-actions-email";
        var response = await http.PutAsJsonAsync(url, new[] { "UPDATE_PASSWORD" });
        // The fake server's nc is not listening yet at "ready", nor between connections.
        for (var attempt = 0; !tolerateFailure && !response.IsSuccessStatusCode && attempt < 10; attempt++)
        {
            await Task.Delay(500);
            response = await http.PutAsJsonAsync(url, new[] { "UPDATE_PASSWORD" });
        }

        if (!tolerateFailure)
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
}
