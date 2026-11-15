using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class InvoiceXmlApiTests(TestcontainersFixture fixture) : IAsyncLifetime
{
    private readonly OpenKSeFWebApplicationFactory _factory = new(fixture);
    private HttpClient _client = null!;
    private Guid _tenantId;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        _client.SetBearerToken(await fixture.GetAccessTokenAsync());
        var response = await _client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), displayName = "Invoice XML test" });
        response.EnsureSuccessStatusCode();
        _tenantId = (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    private async Task<Guid> SeedAsync(string number, byte[]? xml)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invoice = new InvoiceHeader
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            KSeFInvoiceNumber = number,
            KSeFReferenceNumber = number,
            VendorName = "Dostawca Testowy",
            VendorNip = "1111111111",
            IssueDate = DateTime.UtcNow,
            FirstSeenAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
        };
        db.InvoiceHeaders.Add(invoice);
        if (xml is not null)
            db.InvoiceXmls.Add(new InvoiceXml { InvoiceHeaderId = invoice.Id, Content = xml, Sha256 = Convert.ToBase64String(SHA256.HashData(xml)), StoredAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    [Fact]
    public async Task GetXml_ReturnsStoredBytesAsXmlFile_NamedByKSeFNumber_AndHasXmlIsTrue()
    {
        byte[] xml = [0xEF, 0xBB, 0xBF, .. "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Faktura>Zażółć</Faktura>"u8];
        var number = $"1111111111-20261115-{Guid.NewGuid():N}"[..40];
        var id = await SeedAsync(number, xml);

        var response = await _client.GetAsync($"/api/tenants/{_tenantId}/invoices/{id}/xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"{number}.xml", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(SHA256.HashData(xml), SHA256.HashData(await response.Content.ReadAsByteArrayAsync()));

        Assert.True((await _client.GetFromJsonAsync<InvoiceResponse>($"/api/tenants/{_tenantId}/invoices/{id}"))!.HasXml);
        var list = await _client.GetFromJsonAsync<PagedResult<InvoiceResponse>>($"/api/tenants/{_tenantId}/invoices");
        Assert.True(list!.Items.Single(i => i.Id == id).HasXml);
    }

    [Fact]
    public async Task GetXml_NoStoredXml_Returns404_AndHasXmlIsFalse()
    {
        var id = await SeedAsync($"NO-XML-{Guid.NewGuid():N}", null);

        var response = await _client.GetAsync($"/api/tenants/{_tenantId}/invoices/{id}/xml");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False((await _client.GetFromJsonAsync<InvoiceResponse>($"/api/tenants/{_tenantId}/invoices/{id}"))!.HasXml);
    }

    private sealed record IdDto(Guid Id);
}
