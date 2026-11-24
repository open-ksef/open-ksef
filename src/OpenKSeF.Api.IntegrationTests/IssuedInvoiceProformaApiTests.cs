using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class IssuedInvoiceProformaApiTests : IDisposable
{
    private const string BuyerNip = "3333333333";
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly IssuedInvoiceBuyerModel Buyer = new("Company", BuyerNip, "Nabywca sp. z o.o.", "ul. Krótka 2", "00-002 Warszawa");

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public IssuedInvoiceProformaApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture, new FixedTime());
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static string Base(Guid tenantId) => $"/api/tenants/{tenantId}/issued-invoices";

    private static SellerProfileRequest Profile(string? proformaPattern = null, string pattern = "FV/{nr}/{MM}/{RRRR}") => new(
        "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "93000000002026111700000815", false, pattern,
        ProformaNumberingPattern: proformaPattern);

    private async Task<Guid> CreateTenantAsync()
    {
        _client.SetBearerToken(await _fixture.GetAccessTokenAsync());
        var response = await _client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), displayName = "Proforma test" });
        response.EnsureSuccessStatusCode();
        var tenantId = (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        (await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", Profile())).EnsureSuccessStatusCode();
        return tenantId;
    }

    private static IssuedInvoiceDraftRequest Draft(DateOnly? issueDate = null, int paymentDays = 7, bool splitPayment = false, IssuedInvoiceBuyerModel? buyer = null, string? currency = null) =>
        new(buyer ?? Buyer, issueDate ?? Today, issueDate ?? Today, (issueDate ?? Today).AddDays(paymentDays), "Transfer", splitPayment, null,
            [new("Projekt strony", "szt.", 2, 500m, buyer is null ? "23" : "np I"), new("Hosting", "mies.", 12, 50m, buyer is null ? "8" : "np I")], currency);

    private static async Task<IssuedInvoiceResponse> Read(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.True(expected == response.StatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
    }

    private async Task<IssuedInvoiceResponse> CreateProformaAsync(Guid tenantId, IssuedInvoiceDraftRequest? draft = null) =>
        await Read(await _client.PostAsJsonAsync($"{Base(tenantId)}?invoiceType=PRO", draft ?? Draft()), HttpStatusCode.Created);

    private async Task<IssuedInvoiceResponse> IssueAsync(Guid tenantId, Guid id, string type = "PRO") =>
        await Read(await _client.PostAsync($"{Base(tenantId)}/{id}/issue?invoiceType={type}", null), HttpStatusCode.Accepted);

    private async Task<IssuedInvoiceResponse> IssuedProformaAsync(Guid tenantId, IssuedInvoiceDraftRequest? draft = null) =>
        await IssueAsync(tenantId, (await CreateProformaAsync(tenantId, draft)).Id);

    private async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private Task<IssuedInvoice> LoadAsync(Guid id) =>
        WithDbAsync(db => db.IssuedInvoices.AsNoTracking().Include(i => i.Lines).SingleAsync(i => i.Id == id));

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]?.GetValue<string>();

    private static async Task<List<string>> ErrorCodesOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["errors"]!.AsArray().Select(e => e!["code"]!.GetValue<string>()).ToList();

    private async Task<List<(string? Number, string Type, string Status)>> ListAsync(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["items"]!.AsArray()
            .Select(i => (i!["number"]?.GetValue<string>(), i["invoiceType"]!.GetValue<string>(), i["status"]!.GetValue<string>()))
            .ToList();
    }

    [Fact]
    public async Task Issue_NumbersInProSeries_NoXml_FvCounterUnchanged()
    {
        var tenantId = await CreateTenantAsync();

        var issued = await IssuedProformaAsync(tenantId);

        Assert.Equal(("PRO/1/11/2026", "Issued", "PRO"), (issued.Number, issued.Status, issued.InvoiceType));
        Assert.Null(issued.Ksef.KsefNumber);
        Assert.Null(issued.Ksef.VerificationUrl);
        Assert.False(issued.Ksef.HasUpo);
        var row = await LoadAsync(issued.Id);
        Assert.Equal((IssuedInvoiceStatus.Issued, null, null, null, null, null), (row.Status, row.SentXml, row.UpoXml, row.KsefNumber, row.NextAttemptAt, row.KsefEnvironment));

        var vat = await Read(await _client.PostAsJsonAsync(Base(tenantId), Draft()), HttpStatusCode.Created);
        Assert.Equal("FV/1/11/2026", (await IssueAsync(tenantId, vat.Id, "VAT")).Number);
        Assert.Equal("PRO/2/11/2026", (await IssuedProformaAsync(tenantId)).Number);
    }

    [Fact]
    public async Task ForeignCurrencyProformaWithoutRate_IsIssuedInThatCurrency()
    {
        var tenantId = await CreateTenantAsync();
        var foreign = new IssuedInvoiceBuyerModel("Foreign", null, "Buyer Ltd", "1 Street", "London", "GB", "Other", "GB123");

        var draft = await CreateProformaAsync(tenantId, Draft(buyer: foreign, currency: "EUR"));
        Assert.Null(draft.ExchangeRate);
        var issued = await IssueAsync(tenantId, draft.Id);

        Assert.Equal(("Issued", "EUR", null), (issued.Status, issued.Currency, issued.ExchangeRate));
    }

    [Fact]
    public async Task OldPastIssueDate_NeedsNoConfirmation()
    {
        var tenantId = await CreateTenantAsync();

        var issued = await IssuedProformaAsync(tenantId, Draft(Today.AddDays(-60)));

        Assert.Equal("PRO/1/09/2026", issued.Number);
    }

    [Fact]
    public async Task DefaultListAndFeed_ExcludeProformas_InvoiceTypePro_ListsOnlyThem()
    {
        var tenantId = await CreateTenantAsync();
        await IssuedProformaAsync(tenantId);
        await CreateProformaAsync(tenantId);
        var vat = await Read(await _client.PostAsJsonAsync(Base(tenantId), Draft()), HttpStatusCode.Created);
        await IssueAsync(tenantId, vat.Id, "VAT");

        Assert.Equal([("FV/1/11/2026", "VAT", "Sending")], await ListAsync(Base(tenantId)));
        Assert.Equal([("FV/1/11/2026", "VAT", "Sending")], await ListAsync($"{Base(tenantId)}/feed"));
        Assert.Empty(await ListAsync($"{Base(tenantId)}?status=Issued"));
        Assert.Equal(["PRO", "PRO"], (await ListAsync($"{Base(tenantId)}?invoiceType=PRO")).Select(i => i.Type));
        Assert.Equal([("PRO/1/11/2026", "PRO", "Issued")], await ListAsync($"{Base(tenantId)}?invoiceType=PRO&status=Issued"));
        Assert.Equal(["Draft"], (await ListAsync($"{Base(tenantId)}/feed?invoiceType=PRO&status=Draft")).Select(i => i.Status));

        var wrong = await _client.GetAsync($"{Base(tenantId)}?invoiceType=KOR");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(["INV-VAL-082"], await ErrorCodesOf(wrong));
    }

    [Fact]
    public async Task IssuedProforma_RefusesSendCorrectionAdvanceFinalReissueAndUntypedWrites()
    {
        var tenantId = await CreateTenantAsync();
        var issued = await IssuedProformaAsync(tenantId);
        var draft = await CreateProformaAsync(tenantId);
        var before = await WithDbAsync(db => db.IssuedInvoices.AsNoTracking().Where(i => i.TenantId == tenantId).OrderBy(i => i.CreatedAt)
            .Select(i => new { i.Id, i.Status, i.UpdatedAt, i.Number }).ToListAsync());

        var refusals = new (string Name, HttpResponseMessage Response, string? Code)[]
        {
            ("send", await _client.PostAsync($"{Base(tenantId)}/{issued.Id}/issue", null), IssuedInvoiceService.KindMismatchCode),
            ("send draft", await _client.PostAsync($"{Base(tenantId)}/{draft.Id}/issue?invoiceType=VAT", null), IssuedInvoiceService.KindMismatchCode),
            ("correction", await _client.PostAsJsonAsync($"{Base(tenantId)}/{issued.Id}/corrections", new CorrectionCreateRequest()), IssuedInvoiceService.CorrectionSourceCode),
            ("advance", await _client.PostAsync($"{Base(tenantId)}/{issued.Id}/advances", null), IssuedInvoiceService.ChainSourceCode),
            ("final", await _client.PostAsync($"{Base(tenantId)}/{issued.Id}/final", null), IssuedInvoiceService.ChainSourceCode),
            ("reissue", await _client.PostAsync($"{Base(tenantId)}/{issued.Id}/reissue", null), IssuedInvoiceService.ReissueCode),
            ("template", await _client.PostAsJsonAsync($"{Base(tenantId)}/{issued.Id}/template", new InvoiceTemplateRequest("x")), IssuedInvoiceService.TemplateKindCode),
            ("untyped update", await _client.PutAsJsonAsync($"{Base(tenantId)}/{draft.Id}", Draft()), IssuedInvoiceService.KindMismatchCode),
            ("untyped copy", await _client.PostAsync($"{Base(tenantId)}/{issued.Id}/copy", null), IssuedInvoiceService.KindMismatchCode),
            ("edit issued", await _client.PutAsJsonAsync($"{Base(tenantId)}/{issued.Id}?invoiceType=PRO", Draft()), null),
            ("convert draft", await _client.PostAsync($"{Base(tenantId)}/{draft.Id}/convert", null), IssuedInvoiceService.ProformaStateCode),
        };

        foreach (var (name, response, code) in refusals)
        {
            Assert.True(response.StatusCode == HttpStatusCode.Conflict, $"{name}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            Assert.True(code == await CodeOf(response), name);
        }

        var after = await WithDbAsync(db => db.IssuedInvoices.AsNoTracking().Where(i => i.TenantId == tenantId).OrderBy(i => i.CreatedAt)
            .Select(i => new { i.Id, i.Status, i.UpdatedAt, i.Number }).ToListAsync());
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Convert_CreatesVatDraft_OneLiveConversion_ReenabledAfterDraftDelete_IssuedInFvSeries()
    {
        var tenantId = await CreateTenantAsync();
        var proforma = await IssuedProformaAsync(tenantId, Draft(Today.AddDays(-2), paymentDays: 10, splitPayment: true));

        var created = await _client.PostAsync($"{Base(tenantId)}/{proforma.Id}/convert", null);
        var vat = await Read(created, HttpStatusCode.Created);
        Assert.Equal(("VAT", "Draft", Today, Today, Today.AddDays(10)), (vat.InvoiceType, vat.Status, vat.IssueDate, vat.SaleDate, vat.DueDate));
        Assert.Equal((proforma.Buyer, proforma.PaymentMethod, true, proforma.Currency), (vat.Buyer, vat.PaymentMethod, vat.SplitPayment, vat.Currency));
        Assert.Equal(proforma.Lines.Select(l => (l.Name, l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate)), vat.Lines.Select(l => (l.Name, l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate)));
        Assert.Equal((proforma.Id, "PRO/1/11/2026", "PRO"), (vat.Proforma!.Id, vat.Proforma.Number, vat.Proforma.InvoiceType));

        var again = await Read(await _client.PostAsync($"{Base(tenantId)}/{proforma.Id}/convert", null), HttpStatusCode.OK);
        Assert.Equal(vat.Id, again.Id);
        Assert.Equal(1, await WithDbAsync(db => db.IssuedInvoices.CountAsync(i => i.ProformaId == proforma.Id)));
        var converted = await Read(await _client.GetAsync($"{Base(tenantId)}/{proforma.Id}"), HttpStatusCode.OK);
        Assert.Equal((vat.Id, "Draft", "VAT"), (converted.ConvertedTo!.Id, converted.ConvertedTo.Status, converted.ConvertedTo.InvoiceType));

        var refused = await _client.DeleteAsync($"{Base(tenantId)}/{proforma.Id}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(IssuedInvoiceService.ProformaConvertedCode, await CodeOf(refused));

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{Base(tenantId)}/{vat.Id}")).StatusCode);
        Assert.Null((await Read(await _client.GetAsync($"{Base(tenantId)}/{proforma.Id}"), HttpStatusCode.OK)).ConvertedTo);
        var second = await Read(await _client.PostAsync($"{Base(tenantId)}/{proforma.Id}/convert", null), HttpStatusCode.Created);
        Assert.NotEqual(vat.Id, second.Id);

        var sent = await IssueAsync(tenantId, second.Id, "VAT");
        Assert.Equal(("FV/1/11/2026", "Sending"), (sent.Number, sent.Status));
        var row = await LoadAsync(second.Id);
        Assert.Equal((IssuedInvoiceStatus.Queued, proforma.Id), (row.Status, row.ProformaId));
        Assert.DoesNotContain("PRO/", Encoding.UTF8.GetString(row.SentXml!));
        Assert.Equal(IssuedInvoiceStatus.Issued, (await LoadAsync(proforma.Id)).Status);
    }

    [Fact]
    public async Task ConcurrentConverts_CreateOneDraft()
    {
        var tenantId = await CreateTenantAsync();
        var proforma = await IssuedProformaAsync(tenantId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _client.PostAsync($"{Base(tenantId)}/{proforma.Id}/convert", null)));

        Assert.Equal([HttpStatusCode.Created], responses.Select(r => r.StatusCode).Where(s => s != HttpStatusCode.OK));
        Assert.Equal(1, await WithDbAsync(db => db.IssuedInvoices.CountAsync(i => i.ProformaId == proforma.Id)));
    }

    [Fact]
    public async Task Delete_DraftAndUnconvertedIssued_NumberNotReused()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateProformaAsync(tenantId);
        var issued = await IssuedProformaAsync(tenantId);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{Base(tenantId)}/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{Base(tenantId)}/{issued.Id}")).StatusCode);

        Assert.False(await WithDbAsync(db => db.IssuedInvoices.AnyAsync(i => i.TenantId == tenantId)));
        Assert.Equal("PRO/2/11/2026", (await IssuedProformaAsync(tenantId)).Number);
    }

    [Fact]
    public async Task Copy_OfProforma_IsProformaDraft()
    {
        var tenantId = await CreateTenantAsync();
        var issued = await IssuedProformaAsync(tenantId);

        var copy = await Read(await _client.PostAsync($"{Base(tenantId)}/{issued.Id}/copy?invoiceType=PRO", null), HttpStatusCode.Created);

        Assert.Equal(("PRO", "Draft", null), (copy.InvoiceType, copy.Status, copy.Number));
        Assert.Null((await LoadAsync(copy.Id)).ProformaId);
    }

    [Fact]
    public async Task Export_ContainsNoProforma()
    {
        var tenantId = await CreateTenantAsync();
        await IssuedProformaAsync(tenantId);

        var response = await _client.GetAsync($"/api/tenants/{tenantId}/invoices/export?purchases=false");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);

        Assert.Equal(["README.txt", "indeks.csv"], zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        using var index = new StreamReader(zip.GetEntry("indeks.csv")!.Open());
        Assert.Single((await index.ReadToEndAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task TenantDelete_WithOnlyProformasAndDrafts_RemovesThem()
    {
        var tenantId = await CreateTenantAsync();
        await IssuedProformaAsync(tenantId);
        await CreateProformaAsync(tenantId);
        await Read(await _client.PostAsJsonAsync(Base(tenantId), Draft()), HttpStatusCode.Created);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/tenants/{tenantId}")).StatusCode);

        Assert.False(await WithDbAsync(db => db.IssuedInvoices.AnyAsync(i => i.TenantId == tenantId)));
    }

    [Theory]
    [InlineData("FV/{nr}/{MM}/{RRRR}")]
    [InlineData("KOR/{nr}/{MM}/{RRRR}")]
    [InlineData("ZAL/{nr}/{MM}/{RRRR}")]
    public async Task SellerProfile_ProformaPatternEqualToOtherSeries_IsRejectedAndNothingSaved(string pattern)
    {
        var tenantId = await CreateTenantAsync();

        var response = await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", Profile(pattern) with { LegalName = "Changed" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["SP-VAL-012"], await ErrorCodesOf(response));
        var saved = (await _client.GetFromJsonAsync<SellerProfileResponse>($"/api/tenants/{tenantId}/seller-profile"))!;
        Assert.Equal(("OpenKSeF Test A sp. z o.o.", InvoiceNumbering.DefaultProformaPattern), (saved.LegalName, saved.ProformaNumberingPattern));
    }

    [Fact]
    public async Task SellerProfile_SaveWithoutProformaFields_KeepsStoredPattern_NextNumberApplies()
    {
        var tenantId = await CreateTenantAsync();
        var custom = await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", Profile("PF/{nr}/{RRRR}") with { ProformaNextNumber = 7, ProformaNextNumberPeriod = "2026" });
        Assert.Equal(HttpStatusCode.OK, custom.StatusCode);

        var mobileShaped = await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new
        {
            legalName = "OpenKSeF Test A sp. z o.o.",
            addressLine1 = "ul. Prosta 1",
            addressLine2 = "00-001 Warszawa",
            bankAccount = "93000000002026111700000815",
            cashAccounting = false,
            numberingPattern = "FV/{nr}/{MM}/{RRRR}"
        });
        var saved = (await mobileShaped.Content.ReadFromJsonAsync<SellerProfileResponse>())!;

        Assert.Equal(HttpStatusCode.OK, mobileShaped.StatusCode);
        Assert.Equal(("PF/{nr}/{RRRR}", 7, "2026", "PF/7/2026"), (saved.ProformaNumberingPattern, saved.ProformaNextNumber, saved.ProformaNextNumberPeriod, saved.ProformaNextNumberPreview));
        Assert.Equal("PF/7/2026", (await IssuedProformaAsync(tenantId)).Number);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    }
}
