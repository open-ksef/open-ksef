using System.Buffers;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Api.Hubs;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Push;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.IntegrationTests;

/// <summary>Pins the JSON names and kinds installed mobile versions read; golden file lists only fields mobile Models deserialize.</summary>
[Collection(TestcontainersCollection.Name)]
public class MobileContractApiTests : IDisposable
{
    private const string FakeNip = "1111111111";

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly WhiteListStub _whiteList = new();

    public MobileContractApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _baseFactory = new OpenKSeFWebApplicationFactory(fixture);
        _factory = _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient<WhiteListClient>().ConfigurePrimaryHttpMessageHandler(() => _whiteList)));
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _baseFactory.Dispose();
    }

    [Fact]
    public async Task MobileConsumedEndpoints_KeepGoldenPropertyNamesAndKinds()
    {
        _client.SetBearerToken(await _fixture.GetAccessTokenAsync());
        var actual = new Dictionary<string, Dictionary<string, HashSet<string>>>();
        async Task<JsonElement> Read(string key, HttpResponseMessage response, HttpStatusCode? expected = null)
        {
            if (expected is null)
                response.EnsureSuccessStatusCode();
            else
                Assert.Equal(expected, response.StatusCode);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            actual[key] = Flatten(json);
            return json;
        }

        var nip = TestNip.New();
        var tenant = await Read("POST /api/tenants", await _client.PostAsJsonAsync("/api/tenants", new { nip, displayName = "Mobile contract", notificationEmail = "mobile@example.com" }));
        var tenantId = tenant.GetProperty("id").GetGuid();
        var invoiceId = await SeedInvoiceAsync(tenantId, "PLN");
        var eurInvoiceId = await SeedInvoiceAsync(tenantId, "EUR");

        var me = await Read("GET /api/account/me", await _client.GetAsync("/api/account/me"));
        Assert.Equal("testuser@openksef.test", me.GetProperty("email").GetString());
        await Read("GET /api/account/onboarding-status", await _client.GetAsync("/api/account/onboarding-status"));
        await Read("GET /api/tenants", await _client.GetAsync("/api/tenants"));
        await Read("PUT /api/tenants/{id}", await _client.PutAsJsonAsync($"/api/tenants/{tenantId}", new { displayName = "Mobile contract 2", notificationEmail = "mobile@example.com" }));
        await Read("GET /api/tenants/{id}/invoices", await _client.GetAsync($"/api/tenants/{tenantId}/invoices?page=1&pageSize=20"));
        var detail = await Read("GET /api/tenants/{id}/invoices/{invoiceId}", await _client.GetAsync($"/api/tenants/{tenantId}/invoices/{invoiceId}"));
        await Read("GET /api/tenants/{id}/invoices/{invoiceId}/transfer", await _client.GetAsync($"/api/tenants/{tenantId}/invoices/{invoiceId}/transfer"));
        await Read("GET /api/tenants/{id}/invoices/{invoiceId}/transfer (QR blocked)", await _client.GetAsync($"/api/tenants/{tenantId}/invoices/{eurInvoiceId}/transfer"));

        var sellerResponse = await _client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), displayName = "Mobile contract seller" });
        var sellerId = (await sellerResponse.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await Read("PUT /api/tenants/{id}/seller-profile", await _client.PutAsJsonAsync($"/api/tenants/{sellerId}/seller-profile", new SellerProfileRequest(
            "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "61109010140000071219812874", false, "FV/{nr}/{MM}/{RRRR}")));
        await Read("GET /api/tenants/{id}/seller-profile", await _client.GetAsync($"/api/tenants/{sellerId}/seller-profile"));

        var issued = $"/api/tenants/{sellerId}/issued-invoices";
        var today = IssuedInvoice.PolandDate(DateTimeOffset.UtcNow);
        var draft = await Read("POST /api/tenants/{id}/issued-invoices", await _client.PostAsJsonAsync(issued, new IssuedInvoiceDraftRequest(
            new IssuedInvoiceBuyerModel("Company", FakeNip, "Nabywca sp. z o.o.", "ul. Krótka 2", "00-002 Warszawa"),
            today, today, today.AddDays(14), "Transfer", false, new ExemptionBasisModel("Act", "art. 113 ust. 1 ustawy o VAT"),
            [new IssuedInvoiceLineRequest("Usługa", "szt.", 1, 100m, "23"), new IssuedInvoiceLineRequest("Usługa zwolniona", "szt.", 1, 50m, "zw")])));
        var draftId = draft.GetProperty("id").GetGuid();
        await Read("GET /api/tenants/{id}/issued-invoices/{invoiceId}", await _client.GetAsync($"{issued}/{draftId}"));
        await Read("POST /api/tenants/{id}/issued-invoices/{invoiceId}/copy", await _client.PostAsync($"{issued}/{draftId}/copy", null));
        await Read("POST /api/tenants/{id}/issued-invoices/{invoiceId}/issue", await _client.PostAsync($"{issued}/{draftId}/issue", null));
        await Read("GET /api/tenants/{id}/issued-invoices", await _client.GetAsync($"{issued}?page=1&pageSize=20"));

        await Read("GET /api/nip-lookup/{nip}", await _client.GetAsync($"/api/nip-lookup/{FakeNip}"));
        await Read("GET /api/nip-lookup/{nip} (invalid)", await _client.GetAsync("/api/nip-lookup/1234567890"), HttpStatusCode.BadRequest);
        Assert.Single(_whiteList.Requests);

        (await _client.PostAsJsonAsync("/api/devices/register", new { token = $"mobile-contract-{Guid.NewGuid():N}", platform = 1, tenantId })).EnsureSuccessStatusCode();
        await Read("GET /api/devices", await _client.GetAsync("/api/devices"));
        actual["SignalR ReceiveNotification"] = Flatten(await HubFrameAsync(n => n.NotifyNewInvoiceAsync(new NewInvoiceDetectedEvent(tenantId, invoiceId, "Vendor", "FV/1/2026", 123m, "PLN"))));
        actual["SignalR ReceiveNotification (newInvoicesSummary)"] = Flatten(await HubFrameAsync(n => n.NotifyNewInvoicesSummaryAsync(tenantId, 15)));
        (await _client.DeleteAsync($"/api/tenants/{tenantId}")).EnsureSuccessStatusCode();

        var golden = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
            await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MobileContract", "responses.json")))!;
        var broken = golden
            .SelectMany(endpoint => endpoint.Value
                .Where(field => !actual[endpoint.Key].TryGetValue(field.Key, out var kinds) || !kinds.IsSubsetOf(field.Value.Split('|')))
                .Select(field => $"{endpoint.Key} {field.Key}: expected {field.Value}, got {string.Join('|', actual[endpoint.Key].GetValueOrDefault(field.Key, ["missing"]))}"))
            .ToList();
        Assert.True(broken.Count == 0, string.Join(Environment.NewLine, broken));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", detail.GetProperty("dueDate").GetString());
    }

    private async Task<JsonElement> HubFrameAsync(Func<NotificationService, Task> notify)
    {
        var proxy = Substitute.For<IClientProxy>();
        var hub = Substitute.For<IHubContext<NotificationHub>>();
        hub.Clients.User(Arg.Any<string>()).Returns(proxy);
        using (var scope = _factory.Services.CreateScope())
        {
            await notify(new NotificationService(
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                [],
                Substitute.For<IEmailService>(),
                NullLogger<NotificationService>.Instance,
                new SignalRPushProvider(hub, _factory.Services.GetRequiredService<ISystemConfigService>(), NullLogger<SignalRPushProvider>.Instance)));
        }
        var call = proxy.ReceivedCalls().Single().GetArguments();
        var protocol = _factory.Services.GetServices<IHubProtocol>().Single(p => p.Name == "json");
        var frame = new ArrayBufferWriter<byte>();
        protocol.WriteMessage(new InvocationMessage((string)call[0]!, (object?[])call[1]!), frame);
        return JsonDocument.Parse(frame.WrittenMemory[..^1]).RootElement;
    }

    private async Task<Guid> SeedInvoiceAsync(Guid tenantId, string currency)
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
            Currency = currency,
            IssueDate = now,
            AcquisitionDate = now,
            InvoiceType = "Vat",
            DueDate = DateOnly.FromDateTime(now).AddDays(14),
            Settlement = SettlementState.None,
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

    private sealed class WhiteListStub : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$$"""
                    {"result":{"subject":{"name":"Nabywca sp. z o.o.","nip":"{{{FakeNip}}}","statusVat":"Czynny",
                    "residenceAddress":null,"workingAddress":"ul. Krótka 2, 00-002 Warszawa",
                    "accountNumbers":["61109010140000071219812874"]},"requestId":"x"}}
                    """, Encoding.UTF8, "application/json")
            });
        }
    }
}
