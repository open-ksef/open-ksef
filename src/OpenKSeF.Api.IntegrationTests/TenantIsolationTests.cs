using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public sealed class TenantIsolationTests : IAsyncLifetime
{
    private const string OtherUser = "otheruser";

    private static readonly object Draft = new
    {
        buyer = new { kind = "Company", nip = "1111111111", name = "Nabywca Testowy" },
        paymentMethod = "Transfer",
        splitPayment = false,
    };

    private static readonly Dictionary<string, object?> Cases = new()
    {
        ["GET api/tenants/{id}"] = null,
        ["PUT api/tenants/{id}"] = new { displayName = "Hijacked" },
        ["DELETE api/tenants/{id}"] = null,
        ["POST api/tenants/{tenantId}/credentials"] = new { type = 0, token = "fake-ksef-token" },
        ["DELETE api/tenants/{tenantId}/credentials"] = null,
        ["GET api/tenants/{tenantId}/credentials/status"] = null,
        ["POST api/tenants/{tenantId}/credentials/sync"] = null,
        ["POST api/tenants/{tenantId}/credentials/full-resync"] = null,
        ["GET api/tenants/{tenantId}/invoices"] = null,
        ["GET api/tenants/{tenantId}/invoices/{id}"] = null,
        ["GET api/tenants/{tenantId}/invoices/by-number/{ksefInvoiceNumber}"] = null,
        ["GET api/tenants/{tenantId}/invoices/{id}/transfer"] = null,
        ["PATCH api/tenants/{tenantId}/invoices/{id}/paid"] = new { isPaid = true },
        ["GET api/invoices"] = null,
        ["GET api/invoices/by-number/{ksefInvoiceNumber}"] = null,
        ["GET api/tenants/{tenantId}/issued-invoices"] = null,
        ["GET api/tenants/{tenantId}/issued-invoices/feed"] = null,
        ["GET api/tenants/{tenantId}/issued-invoices/exchange-rate"] = null,
        ["GET api/tenants/{tenantId}/issued-invoices/{id}"] = null,
        ["POST api/tenants/{tenantId}/issued-invoices"] = Draft,
        ["PUT api/tenants/{tenantId}/issued-invoices/{id}"] = Draft,
        ["DELETE api/tenants/{tenantId}/issued-invoices/{id}"] = null,
        ["POST api/tenants/{tenantId}/issued-invoices/{id}/issue"] = null,
        ["POST api/tenants/{tenantId}/issued-invoices/{id}/copy"] = null,
        ["GET api/tenants/{tenantId}/issued-invoices/{id}/xml"] = null,
        ["GET api/tenants/{tenantId}/issued-invoices/{id}/upo"] = null,
        ["GET api/tenants/{tenantId}/seller-profile"] = null,
        ["PUT api/tenants/{tenantId}/seller-profile"] = new { legalName = "Hijacked", cashAccounting = false },
        ["POST api/devices/{id}/test-notification"] = null,
        ["DELETE api/devices/{id}"] = null,
    };

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _owner;
    private readonly HttpClient _other;
    private Seed _seed = null!;

    public TenantIsolationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture);
        _owner = _factory.CreateClient();
        _other = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        _owner.SetBearerToken(await _fixture.GetAccessTokenAsync());
        _other.SetBearerToken(await _fixture.GetAccessTokenAsync(OtherUser));

        var tenantA = await CreateTenantAsync(_owner);
        var tenantB = await CreateTenantAsync(_other);

        var issued = await _owner.PostAsJsonAsync($"/api/tenants/{tenantA}/issued-invoices", Draft);
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var issuedId = (await issued.Content.ReadFromJsonAsync<IdDto>())!.Id;

        var now = DateTime.UtcNow;
        var invoice = new InvoiceHeader
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            KSeFInvoiceNumber = $"ISOLATION-{Guid.NewGuid():N}",
            KSeFReferenceNumber = "ISOLATION",
            VendorName = "Dostawca Testowy",
            VendorNip = "1111111111",
            Direction = InvoiceDirection.Received,
            IssueDate = now,
            FirstSeenAt = now,
            LastUpdatedAt = now,
        };
        var device = Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            var ownerId = (await db.Tenants.SingleAsync(t => t.Id == tenantA)).UserId;
            db.InvoiceHeaders.Add(invoice);
            db.DeviceTokens.Add(new DeviceToken
            {
                Id = device, UserId = ownerId, Token = $"isolation-{device:N}", Platform = Platform.Android,
                TenantId = tenantA, CreatedAt = now, UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        });

        _seed = new Seed(tenantA, tenantB, invoice.Id, invoice.KSeFInvoiceNumber, issuedId, device);
    }

    public Task DisposeAsync()
    {
        _owner.Dispose();
        _other.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    private static bool IsTenantScoped(ApiRouteCatalog.ApiRoute route) =>
        route.Parameters.Contains("tenantId")
        || route.Template.StartsWith("api/tenants/{id}", StringComparison.Ordinal)
        || route.Template.StartsWith("api/devices/{id}", StringComparison.Ordinal);

    [Fact]
    public void EveryTenantScopedEndpoint_HasAnIsolationCase()
    {
        var discovered = ApiRouteCatalog.Discover().Where(IsTenantScoped).Select(r => r.Key).ToHashSet();

        Assert.Empty(discovered.Except(Cases.Keys));
        Assert.Empty(Cases.Keys.Except(discovered));
    }

    [Fact]
    public async Task NonMember_GetsForbiddenOrNotFound_OnEveryTenantScopedEndpoint()
    {
        var routes = ApiRouteCatalog.Discover().Where(IsTenantScoped).ToList();
        Assert.NotEmpty(routes);

        var failures = new List<string>();
        foreach (var route in routes)
        {
            var response = await SendAsync(_other, route, _seed.TenantA);
            if (response.StatusCode != HttpStatusCode.Forbidden && !IsActionNotFound(response))
                failures.Add($"{route.Key}: {(int)response.StatusCode} {response.Content.Headers.ContentType?.MediaType}");
        }

        Assert.Empty(failures);

        var tenant = await _owner.GetFromJsonAsync<NameDto>($"/api/tenants/{_seed.TenantA}");
        Assert.NotEqual("Hijacked", tenant!.DisplayName);
        Assert.Equal(HttpStatusCode.OK, (await _owner.GetAsync($"/api/tenants/{_seed.TenantA}/issued-invoices/{_seed.IssuedId}")).StatusCode);
        await WithDbAsync(async db =>
        {
            Assert.False((await db.InvoiceHeaders.SingleAsync(i => i.Id == _seed.InvoiceId)).IsPaid);
            Assert.True(await db.DeviceTokens.AnyAsync(d => d.Id == _seed.DeviceId));
            Assert.False(await db.SellerProfiles.AnyAsync(p => p.TenantId == _seed.TenantA));
        });
    }

    [Fact]
    public async Task ForeignResourceIdUnderOwnTenant_Returns404()
    {
        var routes = ApiRouteCatalog.Discover().Where(r =>
            r.Parameters.Contains("tenantId") && (r.Parameters.Contains("id") || r.Parameters.Contains("ksefInvoiceNumber"))).ToList();
        Assert.NotEmpty(routes);

        var failures = new List<string>();
        foreach (var route in routes)
        {
            var response = await SendAsync(_other, route, _seed.TenantB);
            if (!IsActionNotFound(response))
                failures.Add($"{route.Key}: {(int)response.StatusCode} {response.Content.Headers.ContentType?.MediaType}");
        }

        Assert.Empty(failures);
    }

    private Task<HttpResponseMessage> SendAsync(HttpClient client, ApiRouteCatalog.ApiRoute route, Guid tenantId)
    {
        var path = route.Template
            .Replace("{tenantId}", tenantId.ToString())
            .Replace("{ksefInvoiceNumber}", _seed.KsefNumber)
            .Replace("{id}", ResourceId(route.Template).ToString());
        if (path.Contains('{'))
            throw new InvalidOperationException($"Unfilled route parameter in {route.Key}");
        if (route.Parameters.Contains("tenantId") && !route.Template.Contains("{tenantId}"))
            path += $"?tenantId={tenantId}";

        var request = new HttpRequestMessage(new HttpMethod(route.Method), "/" + path);
        if (Cases[route.Key] is { } body)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    private static bool IsActionNotFound(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.NotFound
        && response.Content.Headers.ContentType?.MediaType == "application/problem+json";

    private Guid ResourceId(string template) => template switch
    {
        _ when !template.Contains("{id}") => Guid.Empty,
        _ when template.StartsWith("api/tenants/{id}", StringComparison.Ordinal) => _seed.TenantA,
        _ when template.StartsWith("api/devices/{id}", StringComparison.Ordinal) => _seed.DeviceId,
        _ when template.Contains("/issued-invoices/{id}", StringComparison.Ordinal) => _seed.IssuedId,
        _ when template.Contains("/invoices/{id}", StringComparison.Ordinal) => _seed.InvoiceId,
        _ => throw new InvalidOperationException($"No seeded resource for {template}"),
    };

    private static async Task<Guid> CreateTenantAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), displayName = "Isolation test" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private async Task WithDbAsync(Func<ApplicationDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private sealed record Seed(Guid TenantA, Guid TenantB, Guid InvoiceId, string KsefNumber, Guid IssuedId, Guid DeviceId);

    private sealed record IdDto(Guid Id);

    private sealed record NameDto(string DisplayName);
}
