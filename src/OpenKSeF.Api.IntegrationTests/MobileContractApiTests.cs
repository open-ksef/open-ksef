using System.Buffers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Api.Hubs;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Push;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Api.IntegrationTests;

/// <summary>Pins the JSON names and kinds installed mobile versions read; golden file lists only fields mobile Models deserialize.</summary>
[Collection(TestcontainersCollection.Name)]
public class MobileContractApiTests : IDisposable
{
    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public MobileContractApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task MobileConsumedEndpoints_KeepGoldenPropertyNamesAndKinds()
    {
        _client.SetBearerToken(await _fixture.GetAccessTokenAsync());
        var actual = new Dictionary<string, Dictionary<string, HashSet<string>>>();
        async Task<JsonElement> Read(string key, HttpResponseMessage response)
        {
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            actual[key] = Flatten(json);
            return json;
        }

        var nip = Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString();
        var tenant = await Read("POST /api/tenants", await _client.PostAsJsonAsync("/api/tenants", new { nip, displayName = "Mobile contract", notificationEmail = "mobile@example.com" }));
        var tenantId = tenant.GetProperty("id").GetGuid();
        var invoiceId = await SeedInvoiceAsync(tenantId);

        await Read("GET /api/account/me", await _client.GetAsync("/api/account/me"));
        await Read("GET /api/account/onboarding-status", await _client.GetAsync("/api/account/onboarding-status"));
        await Read("GET /api/tenants", await _client.GetAsync("/api/tenants"));
        await Read("PUT /api/tenants/{id}", await _client.PutAsJsonAsync($"/api/tenants/{tenantId}", new { displayName = "Mobile contract 2", notificationEmail = "mobile@example.com" }));
        await Read("GET /api/tenants/{id}/invoices", await _client.GetAsync($"/api/tenants/{tenantId}/invoices?page=1&pageSize=20"));
        await Read("GET /api/tenants/{id}/invoices/{invoiceId}", await _client.GetAsync($"/api/tenants/{tenantId}/invoices/{invoiceId}"));
        (await _client.PostAsJsonAsync("/api/devices/register", new { token = $"mobile-contract-{Guid.NewGuid():N}", platform = 1, tenantId })).EnsureSuccessStatusCode();
        await Read("GET /api/devices", await _client.GetAsync("/api/devices"));
        (await _client.DeleteAsync($"/api/tenants/{tenantId}")).EnsureSuccessStatusCode();
        actual["SignalR ReceiveNotification"] = Flatten(await HubFrameAsync());

        var golden = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
            await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MobileContract", "responses.json")))!;
        var broken = golden
            .SelectMany(endpoint => endpoint.Value
                .Where(field => !actual[endpoint.Key].TryGetValue(field.Key, out var kinds) || !kinds.IsSubsetOf(field.Value.Split('|')))
                .Select(field => $"{endpoint.Key} {field.Key}: expected {field.Value}, got {string.Join('|', actual[endpoint.Key].GetValueOrDefault(field.Key, ["missing"]))}"))
            .ToList();
        Assert.True(broken.Count == 0, string.Join(Environment.NewLine, broken));
    }

    private async Task<JsonElement> HubFrameAsync()
    {
        var proxy = Substitute.For<IClientProxy>();
        var hub = Substitute.For<IHubContext<NotificationHub>>();
        hub.Clients.User("user-1").Returns(proxy);
        await new SignalRPushProvider(hub, NullLogger<SignalRPushProvider>.Instance).SendToUserAsync("user-1", new PushNotification
        {
            Title = "Nowa faktura",
            Body = "Vendor: 123 PLN",
            Data = new() { ["tenantId"] = Guid.NewGuid().ToString(), ["invoiceId"] = Guid.NewGuid().ToString() }
        });
        var call = proxy.ReceivedCalls().Single().GetArguments();
        var protocol = _factory.Services.GetServices<IHubProtocol>().Single(p => p.Name == "json");
        var frame = new ArrayBufferWriter<byte>();
        protocol.WriteMessage(new InvocationMessage((string)call[0]!, (object?[])call[1]!), frame);
        return JsonDocument.Parse(frame.WrittenMemory[..^1]).RootElement;
    }

    private async Task<Guid> SeedInvoiceAsync(Guid tenantId)
    {
        var now = DateTime.UtcNow;
        var invoice = new InvoiceHeader
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            KSeFInvoiceNumber = $"MOBILE-{Guid.NewGuid():N}",
            KSeFReferenceNumber = "REF-1",
            InvoiceNumber = "FV/1/2026",
            VendorName = "Vendor",
            VendorNip = "1112223344",
            VendorBankAccount = "61109010140000071219812874",
            BuyerName = "Buyer",
            BuyerNip = "2223334455",
            AmountNet = 100m,
            AmountVat = 23m,
            AmountGross = 123m,
            Currency = "PLN",
            IssueDate = now,
            AcquisitionDate = now,
            InvoiceType = "Vat",
            IsPaid = true,
            PaidAt = now,
            FirstSeenAt = now,
            LastUpdatedAt = now
        };
        invoice.Lines.Add(new InvoiceLine
        {
            Id = Guid.NewGuid(),
            InvoiceHeaderId = invoice.Id,
            LineNumber = 1,
            Name = "Towar",
            Unit = "szt",
            Quantity = 1m,
            UnitPriceNet = 100m,
            UnitPriceGross = 123m,
            AmountNet = 100m,
            AmountGross = 123m,
            AmountVat = 23m,
            VatRate = "23"
        });
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.InvoiceHeaders.Add(invoice);
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    private static Dictionary<string, HashSet<string>> Flatten(JsonElement json)
    {
        var fields = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        void Walk(JsonElement node, string path)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in node.EnumerateObject())
                        Walk(property.Value, path.Length == 0 ? property.Name : $"{path}.{property.Name}");
                    break;
                case JsonValueKind.Array:
                    foreach (var item in node.EnumerateArray())
                        Walk(item, path.Length == 0 ? "[]" : $"{path}[]");
                    break;
                default:
                    if (!fields.TryGetValue(path, out var kinds))
                        fields[path] = kinds = [];
                    kinds.Add(node.ValueKind is JsonValueKind.True or JsonValueKind.False ? "boolean" : node.ValueKind.ToString().ToLowerInvariant());
                    break;
            }
        }
        Walk(json, "");
        return fields;
    }
}
