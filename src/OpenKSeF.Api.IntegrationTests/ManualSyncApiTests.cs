using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public sealed class ManualSyncApiTests : IAsyncLifetime
{
    private const string Fa3 = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly IKSeFGateway _gateway = Substitute.For<IKSeFGateway>();
    private readonly TaskCompletionSource _slowGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _slow;

    public ManualSyncApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _baseFactory = new OpenKSeFWebApplicationFactory(fixture);
        _factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sync:ManualSyncHold", "00:00:02");
            builder.ConfigureTestServices(services => services.AddSingleton(_gateway));
        });
        _gateway.QueryInvoicesAsync(Arg.Any<KSeFSession>(), Arg.Any<KSeFQueryCriteria>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var criteria = call.Arg<KSeFQueryCriteria>();
                if (criteria.Direction != InvoiceDirection.Received || criteria.DateTo < DateTime.UtcNow.AddMinutes(-5))
                    return new KSeFInvoiceQueryResult([], criteria.DateTo);
                if (_slow)
                    await _slowGate.Task;
                return new KSeFInvoiceQueryResult(
                [
                    new KSeFInvoiceHeader($"OLD-CONTRACT-{criteria.SubjectNip}", "FV 1/2026", "7740001454", "Dostawca", criteria.SubjectNip, "Moja firma",
                        100m, 23m, 123m, "PLN", DateTime.UtcNow.AddDays(-3), DateTime.UtcNow, "Vat", BuyerIdType: "Nip")
                ], null);
            });
        _gateway.DownloadInvoiceAsync(Arg.Any<KSeFSession>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes($"""<Faktura xmlns="{Fa3}"><Fa><FaWiersz><NrWierszaFa>1</NrWierszaFa><P_7>Towar</P_7><P_11>100</P_11><P_12>23</P_12></FaWiersz></Fa></Faktura>"""));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _slowGate.TrySetResult();
        await _factory.DisposeAsync();
        await _baseFactory.DisposeAsync();
    }

    private async Task<HttpClient> ClientAsync(string username = "testuser")
    {
        var client = _factory.CreateClient();
        client.SetBearerToken(await _fixture.GetAccessTokenAsync(username));
        return client;
    }

    private static async Task<Guid> TenantAsync(HttpClient client, bool withCredential)
    {
        var created = await client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), displayName = "Manual sync" });
        var tenantId = (await created.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        if (withCredential)
            (await client.PostAsJsonAsync($"/api/tenants/{tenantId}/credentials", new { type = 0, token = "fake-ksef-token" })).EnsureSuccessStatusCode();
        return tenantId;
    }

    [Fact]
    public async Task OldClientWithoutPrefer_FastSync_GetsTodaysSynchronous200Body()
    {
        using var client = await ClientAsync();
        var tenantId = await TenantAsync(client, withCredential: true);

        var response = await client.PostAsync($"/api/tenants/{tenantId}/credentials/sync", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(tenantId, body.GetProperty("tenantId").GetGuid());
        Assert.Equal(1, body.GetProperty("fetchedInvoices").GetInt32());
        Assert.Equal(1, body.GetProperty("newInvoices").GetInt32());
        Assert.Equal(JsonValueKind.String, body.GetProperty("syncedAtUtc").ValueKind);

        var status = await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{tenantId}/credentials/sync");
        Assert.Equal("succeeded", status.GetProperty("state").GetString());
        Assert.Equal(1, status.GetProperty("result").GetProperty("fetchedInvoices").GetInt32());
        Assert.Equal(JsonValueKind.String, status.GetProperty("lastSuccessfulSyncUtc").ValueKind);
    }

    [Fact]
    public async Task OldClientWithoutPrefer_SlowSync_Gets202WithEmptyBody_AndTheSyncFinishesInBackground()
    {
        _slow = true;
        using var client = await ClientAsync();
        var tenantId = await TenantAsync(client, withCredential: true);

        var response = await client.PostAsync($"/api/tenants/{tenantId}/credentials/sync", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());

        _slowGate.SetResult();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        JsonElement status;
        do
        {
            await Task.Delay(100);
            status = await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{tenantId}/credentials/sync");
        }
        while (status.GetProperty("state").GetString() is "waiting" or "running" && DateTime.UtcNow < deadline);

        Assert.Equal("succeeded", status.GetProperty("state").GetString());
        Assert.Equal(1, status.GetProperty("result").GetProperty("newInvoices").GetInt32());
    }

    [Fact]
    public async Task PreferRespondAsync_Returns202WithLocationAndStatus()
    {
        using var client = await ClientAsync();
        var tenantId = await TenantAsync(client, withCredential: true);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tenants/{tenantId}/credentials/full-resync");
        request.Headers.Add("Prefer", "respond-async");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal($"/api/tenants/{tenantId}/credentials/sync", response.Headers.Location?.OriginalString);
        var status = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(tenantId, status.GetProperty("tenantId").GetGuid());
        Assert.True(status.GetProperty("fullResync").GetBoolean());
    }

    [Fact]
    public async Task SeventhPostInTheWindow_Returns429_PerUser()
    {
        using var client = await ClientAsync();
        using var other = await ClientAsync("otheruser");
        var tenantId = await TenantAsync(client, withCredential: false);
        var otherTenantId = await TenantAsync(other, withCredential: false);

        for (var i = 0; i < 6; i++)
        {
            var path = i % 2 == 0 ? "sync" : "full-resync";
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/tenants/{tenantId}/credentials/{path}", null)).StatusCode);
        }

        var limited = await client.PostAsync($"/api/tenants/{tenantId}/credentials/sync", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", (await limited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.InRange(limited.Headers.RetryAfter!.Delta!.Value, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(15));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/tenants/{tenantId}/credentials/sync")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await other.PostAsync($"/api/tenants/{otherTenantId}/credentials/sync", null)).StatusCode);
    }

    [Fact]
    public void BothSyncPosts_UseTheManualSyncPolicy()
    {
        foreach (var action in new[] { nameof(CredentialsController.SyncNow), nameof(CredentialsController.FullResync) })
        {
            var limit = Assert.Single(typeof(CredentialsController).GetMethod(action)!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true));
            Assert.Equal(CredentialsController.ManualSyncRateLimitPolicy, ((EnableRateLimitingAttribute)limit).PolicyName);
        }
    }
}
