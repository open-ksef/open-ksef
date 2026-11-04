using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class ForeignInvoicesApiTests : IDisposable
{
    private const string IssueDay = "2026-11-06";
    private const string ReleasedMobileBuyer = """{"kind":"Foreign","nip":null,"name":"Example Buyer Ltd","addressLine1":"1 Test Street","addressLine2":"Testville"}""";

    private static readonly ExchangeRateQuote Thursday = new(5.2133m, new DateOnly(2026, 11, 5));

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ForeignInvoicesApiTests(TestcontainersFixture fixture)
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

    private async Task<Guid> CreateTenantAsync()
    {
        _client.SetBearerToken(await _fixture.GetAccessTokenAsync());
        var nip = $"521{Random.Shared.Next(1_000_000, 9_999_999)}";
        var response = await _client.PostAsJsonAsync("/api/tenants", new { nip, displayName = "Foreign invoices test" });
        response.EnsureSuccessStatusCode();
        var tenantId = (await response.Content.ReadFromJsonAsync<TenantIdDto>())!.Id;

        var profile = await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
            "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "61109010140000071219812874", false, "FV/{nr}/{MM}/{RRRR}"));
        profile.EnsureSuccessStatusCode();
        return tenantId;
    }

    private static IssuedInvoiceDraftRequest ForeignDraft(
        string country = "GB",
        string? foreignId = null,
        string vatRate = "np I",
        string currency = "GBP",
        decimal? exchangeRate = null,
        DateOnly? exchangeRateDate = null,
        string issueDate = IssueDay,
        string? saleDate = null) =>
        new(
            new IssuedInvoiceBuyerModel("Foreign", null, "Example Buyer Ltd", "1 Test Street", "Testville", country, null, foreignId),
            DateOnly.Parse(issueDate),
            DateOnly.Parse(saleDate ?? issueDate),
            DateOnly.Parse(issueDate).AddDays(14),
            "Transfer",
            false,
            null,
            [new IssuedInvoiceLineRequest("Consulting / Konsultacje", "h", 10, 2000m, vatRate)],
            currency,
            exchangeRate,
            exchangeRateDate);

    private async Task<IssuedInvoiceResponse> CreateAsync(Guid tenantId, IssuedInvoiceDraftRequest request)
    {
        var response = await _client.PostAsJsonAsync(Base(tenantId), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
    }

    private async Task<IssuedInvoiceResponse> UpdateAsync(Guid tenantId, Guid id, IssuedInvoiceDraftRequest request)
    {
        var response = await _client.PutAsJsonAsync($"{Base(tenantId)}/{id}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
    }

    private async Task<ValidationEnvelope> IssueRejectedAsync(Guid tenantId, Guid id)
    {
        var response = await _client.PostAsync($"{Base(tenantId)}/{id}/issue", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ValidationEnvelope>())!;
    }

    private async Task<string> SentXmlAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.IssuedInvoices.AsNoTracking().SingleAsync(i => i.Id == id);
        return Encoding.UTF8.GetString(stored.SentXml!);
    }

    [Fact]
    public async Task ThirdCountryNpI_IssuesWithProposedRateAndFa3Fields()
    {
        var tenantId = await CreateTenantAsync();
        _factory.ExchangeRates.Quote = Thursday;

        var draft = await CreateAsync(tenantId, ForeignDraft());
        Assert.Equal("GBP", draft.Currency);
        Assert.Equal(5.2133m, draft.ExchangeRate);
        Assert.Equal(Thursday.TableDate, draft.ExchangeRateDate);
        Assert.Equal("None", draft.Buyer.IdType);
        Assert.Equal("Foreign", draft.Buyer.Kind);
        Assert.DoesNotContain(draft.Warnings, w => w.Code == "INV-W-MPP");
        Assert.True(draft.TotalGross > 15000m);

        var issue = await _client.PostAsync($"{Base(tenantId)}/{draft.Id}/issue", null);
        Assert.Equal(HttpStatusCode.Accepted, issue.StatusCode);
        var issued = (await issue.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
        Assert.Null(issued.Ksef.KsefNumber);
        Assert.Equal("PL61109010140000071219812874", issued.Seller.Iban);
        Assert.Equal("61109010140000071219812874", issued.Seller.BankAccount);

        var xml = await SentXmlAsync(draft.Id);
        Assert.Contains("<BrakID>1</BrakID>", xml);
        Assert.Contains("<KodKraju>GB</KodKraju>", xml);
        Assert.Contains("<KodWaluty>GBP</KodWaluty>", xml);
        Assert.Contains("<P_13_8>20000.00</P_13_8>", xml);
        Assert.Contains("<P_18>1</P_18>", xml);
        Assert.Contains("<KursWaluty>5.2133</KursWaluty>", xml);
        Assert.Contains("<NrRB>PL61109010140000071219812874</NrRB>", xml);
    }

    [Fact]
    public async Task EuNpII_RequiresVatNumber_ThenIssuesWithPrefiksPodatnika()
    {
        var tenantId = await CreateTenantAsync();

        var draft = await CreateAsync(tenantId, ForeignDraft("DE", null, "np II", "EUR", 4.25m));
        var rejected = await IssueRejectedAsync(tenantId, draft.Id);
        Assert.Contains(rejected.Errors, e => e is { Code: "INV-VAL-032", Field: "buyer.vatUe" });

        var fixedDraft = await UpdateAsync(tenantId, draft.Id, ForeignDraft("DE", "DE123456789", "np II", "EUR", 4.25m));
        Assert.Equal("VatUe", fixedDraft.Buyer.IdType);
        Assert.Equal("123456789", fixedDraft.Buyer.ForeignId);

        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"{Base(tenantId)}/{draft.Id}/issue", null)).StatusCode);
        var xml = await SentXmlAsync(draft.Id);
        Assert.Contains("<PrefiksPodatnika>PL</PrefiksPodatnika>", xml);
        Assert.Contains("<KodUE>DE</KodUE>", xml);
        Assert.Contains("<NrVatUE>123456789</NrVatUE>", xml);
        Assert.Contains("<P_13_9>20000.00</P_13_9>", xml);
    }

    [Fact]
    public async Task ForeignIdIsNeverStoredInBuyerNip_ThirdCountryTaxIdGoesToNrId()
    {
        var tenantId = await CreateTenantAsync();

        var draft = await CreateAsync(tenantId, ForeignDraft("GB", "GB-LONG-TAX-ID-000000000000111", exchangeRate: 5m));

        Assert.Null(draft.Buyer.Nip);
        Assert.Equal("Other", draft.Buyer.IdType);
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"{Base(tenantId)}/{draft.Id}/issue", null)).StatusCode);
        var xml = await SentXmlAsync(draft.Id);
        Assert.Contains("<NrID>GB-LONG-TAX-ID-000000000000111</NrID>", xml);
        Assert.DoesNotContain("<BrakID>", xml);
    }

    [Fact]
    public async Task ProposedRateUsesTheEarlierOfIssueAndSaleDate()
    {
        var tenantId = await CreateTenantAsync();
        _factory.ExchangeRates.Quote = Thursday;

        await CreateAsync(tenantId, ForeignDraft(issueDate: "2026-11-06", saleDate: "2026-10-30"));

        Assert.Equal(("GBP", new DateOnly(2026, 10, 30)), Assert.Single(_factory.ExchangeRates.Calls));
    }

    [Fact]
    public async Task NbpFailure_LeavesRateEmptyAndDraftSaved_ThenIssueIsBlockedOnRate()
    {
        var tenantId = await CreateTenantAsync();
        _factory.ExchangeRates.Quote = null;

        var draft = await CreateAsync(tenantId, ForeignDraft());
        Assert.Null(draft.ExchangeRate);
        Assert.Null(draft.ExchangeRateDate);

        var reloaded = (await _client.GetFromJsonAsync<IssuedInvoiceResponse>($"{Base(tenantId)}/{draft.Id}"))!;
        Assert.Equal("GBP", reloaded.Currency);

        var rejected = await IssueRejectedAsync(tenantId, draft.Id);
        Assert.Contains(rejected.Errors, e => e is { Code: "INV-VAL-092", Field: "exchangeRate" });
    }

    [Fact]
    public async Task TypedRate_IsNeverReplaced()
    {
        var tenantId = await CreateTenantAsync();
        _factory.ExchangeRates.Quote = Thursday;

        var draft = await CreateAsync(tenantId, ForeignDraft(exchangeRate: 5.5m));
        Assert.Equal(5.5m, draft.ExchangeRate);
        Assert.Null(draft.ExchangeRateDate);

        var moved = await UpdateAsync(tenantId, draft.Id, ForeignDraft(exchangeRate: null, issueDate: "2026-11-02"));

        Assert.Equal(5.5m, moved.ExchangeRate);
        Assert.Empty(_factory.ExchangeRates.Calls);
    }

    [Fact]
    public async Task UpdateFromReleasedClient_KeepsForeignFields()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateAsync(tenantId, ForeignDraft("GB", "GB-TEST-1", exchangeRate: 5.5m));

        var body = $$"""
            { "buyer": {{ReleasedMobileBuyer}}, "issueDate": "{{IssueDay}}", "saleDate": "{{IssueDay}}", "dueDate": "2026-11-20",
              "paymentMethod": "Transfer", "splitPayment": false, "exemptionBasis": null,
              "lines": [ { "name": "Renamed", "unit": "h", "quantity": 1, "unitNetPrice": 10.00, "vatRate": "np I" } ] }
            """;
        var response = await _client.PutAsync($"{Base(tenantId)}/{draft.Id}", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = (await response.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
        Assert.Equal("Renamed", Assert.Single(updated.Lines).Name);
        Assert.Equal("Foreign", updated.Buyer.Kind);
        Assert.Equal("GB", updated.Buyer.CountryCode);
        Assert.Equal("GB-TEST-1", updated.Buyer.ForeignId);
        Assert.Equal("Other", updated.Buyer.IdType);
        Assert.Equal("GBP", updated.Currency);
        Assert.Equal(5.5m, updated.ExchangeRate);
    }

    [Fact]
    public async Task Copy_KeepsForeignFieldsAndProposesRateAgain()
    {
        var tenantId = await CreateTenantAsync();
        var source = await CreateAsync(tenantId, ForeignDraft("GB", "GB-TEST-1", exchangeRate: 5.5m));
        _factory.ExchangeRates.Quote = new ExchangeRateQuote(5.3m, new DateOnly(2026, 12, 18));

        var response = await _client.PostAsync($"{Base(tenantId)}/{source.Id}/copy", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var copy = (await response.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
        Assert.Equal("Foreign", copy.Buyer.Kind);
        Assert.Equal("GB", copy.Buyer.CountryCode);
        Assert.Equal("GB-TEST-1", copy.Buyer.ForeignId);
        Assert.Equal("GBP", copy.Currency);
        Assert.Equal(5.3m, copy.ExchangeRate);
        Assert.Equal(new DateOnly(2026, 12, 18), copy.ExchangeRateDate);
        Assert.Equal(("GBP", new DateOnly(2026, 12, 20)), Assert.Single(_factory.ExchangeRates.Calls));
    }

    [Fact]
    public async Task Copy_WhenNbpIsDown_LeavesRateEmpty()
    {
        var tenantId = await CreateTenantAsync();
        var source = await CreateAsync(tenantId, ForeignDraft(exchangeRate: 5.5m));
        _factory.ExchangeRates.Quote = null;

        var response = await _client.PostAsync($"{Base(tenantId)}/{source.Id}/copy", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!.ExchangeRate);
    }

    [Fact]
    public async Task ExchangeRateEndpoint_ProposesAndReportsNbpFailureAsEmpty()
    {
        var tenantId = await CreateTenantAsync();
        _factory.ExchangeRates.Quote = Thursday;

        var quote = (await _client.GetFromJsonAsync<ExchangeRateProposalResponse>($"{Base(tenantId)}/exchange-rate?currency=gbp&issueDate=2026-11-06"))!;
        Assert.Equal(new ExchangeRateProposalResponse("GBP", 5.2133m, Thursday.TableDate), quote);
        Assert.Equal(("GBP", new DateOnly(2026, 11, 6)), Assert.Single(_factory.ExchangeRates.Calls));

        _factory.ExchangeRates.Quote = null;
        var empty = (await _client.GetFromJsonAsync<ExchangeRateProposalResponse>($"{Base(tenantId)}/exchange-rate?currency=GBP"))!;
        Assert.Null(empty.Rate);

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"{Base(tenantId)}/exchange-rate?currency=x/y")).StatusCode);
    }

    [Fact]
    public async Task InvalidDraftFields_AreRejectedAsDraftErrors()
    {
        var tenantId = await CreateTenantAsync();

        var response = await _client.PostAsJsonAsync(Base(tenantId), ForeignDraft("GBR", new string('1', 51), currency: "POUND", exchangeRate: 1.1234567m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<ValidationEnvelope>())!.Errors;
        Assert.Contains(errors, e => e is { Code: "INV-VAL-030", Field: "buyer.countryCode" });
        Assert.Contains(errors, e => e is { Code: "INV-VAL-073", Field: "buyer.foreignId" });
        Assert.Contains(errors, e => e is { Code: "INV-VAL-090", Field: "currency" });
        Assert.Contains(errors, e => e is { Code: "INV-VAL-093", Field: "exchangeRate" });
    }

    [Fact]
    public async Task GbpRow_ShowsInFeedAndList_WithItsCurrencyAndBuyerFields()
    {
        var tenantId = await CreateTenantAsync();
        await CreateAsync(tenantId, ForeignDraft("GB", "GB-TEST-1", exchangeRate: 5.5m));
        await CreateAsync(tenantId, new IssuedInvoiceDraftRequest(
            new IssuedInvoiceBuyerModel("Company", "5261040828", "Nabywca", "ul. Krótka 2", null),
            DateOnly.Parse(IssueDay), DateOnly.Parse(IssueDay), DateOnly.Parse(IssueDay).AddDays(14),
            "Transfer", false, null, [new IssuedInvoiceLineRequest("Usługa", "szt.", 1, 100m, "23")]));

        var feed = (await _client.GetFromJsonAsync<PagedResult<IssuedFeedItemResponse>>($"{Base(tenantId)}/feed"))!;
        Assert.Equal(["GBP", "PLN"], feed.Items.Select(i => i.Currency).Order());
        var feedRow = feed.Items.Single(i => i.Currency == "GBP");
        Assert.Equal("OpenKSeF", feedRow.Source);
        Assert.Null(feedRow.BuyerNip);
        Assert.Equal("GB", feedRow.BuyerCountryCode);
        Assert.Equal("Other", feedRow.BuyerIdType);
        Assert.Equal("GB-TEST-1", feedRow.BuyerForeignId);
        Assert.Equal(20000m, feedRow.TotalGross);

        var list = (await _client.GetFromJsonAsync<PagedResult<IssuedInvoiceListItemResponse>>(Base(tenantId)))!;
        var listRow = list.Items.Single(i => i.Currency == "GBP");
        Assert.Equal("GB", listRow.BuyerCountryCode);
        Assert.Equal("Other", listRow.BuyerIdType);
        Assert.Equal("GB-TEST-1", listRow.BuyerForeignId);
        Assert.Equal("PLN", list.Items.Single(i => i.BuyerNip == "5261040828").Currency);

        var found = (await _client.GetFromJsonAsync<PagedResult<IssuedInvoiceListItemResponse>>($"{Base(tenantId)}?q=gb-test"))!;
        Assert.Equal("GBP", Assert.Single(found.Items).Currency);
    }

    [Fact]
    public async Task SellerProfile_NewFieldsRoundTrip_AndOlderRequestKeepsThem()
    {
        var tenantId = await CreateTenantAsync();
        var path = $"/api/tenants/{tenantId}/seller-profile";

        var saved = await _client.PutAsJsonAsync(path, new SellerProfileRequest(
            "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "61109010140000071219812874", false, "FV/{nr}/{MM}/{RRRR}",
            BankSwift: "testplpw", BankName: "Bank Testowy S.A.", ForeignBankAccount: "gb82 west 1234 5698 7654 32", ForeignBankSwift: "TESTGB2L", ForeignBankName: "West Test Bank"));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var older = await _client.PutAsJsonAsync(path, new
        {
            legalName = "Nowa nazwa sp. z o.o.",
            addressLine1 = "ul. Prosta 1",
            addressLine2 = "00-001 Warszawa",
            bankAccount = "61109010140000071219812874",
            cashAccounting = false,
            numberingPattern = "FV/{nr}/{MM}/{RRRR}"
        });
        Assert.Equal(HttpStatusCode.OK, older.StatusCode);

        var profile = (await older.Content.ReadFromJsonAsync<SellerProfileResponse>())!;
        Assert.Equal("Nowa nazwa sp. z o.o.", profile.LegalName);
        Assert.Equal("TESTPLPW", profile.BankSwift);
        Assert.Equal("Bank Testowy S.A.", profile.BankName);
        Assert.Equal("GB82WEST12345698765432", profile.ForeignBankAccount);
        Assert.Equal("TESTGB2L", profile.ForeignBankSwift);
        Assert.Equal("West Test Bank", profile.ForeignBankName);

        var draft = await CreateAsync(tenantId, ForeignDraft(exchangeRate: 5.5m));
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"{Base(tenantId)}/{draft.Id}/issue", null)).StatusCode);
        var xml = await SentXmlAsync(draft.Id);
        Assert.Contains("<NrRB>GB82WEST12345698765432</NrRB>", xml);
        Assert.Contains("<SWIFT>TESTGB2L</SWIFT>", xml);
        Assert.Contains("<NazwaBanku>West Test Bank</NazwaBanku>", xml);
    }

    [Fact]
    public async Task SellerProfile_RejectsBadForeignAccountAndSwift()
    {
        var tenantId = await CreateTenantAsync();

        var response = await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
            "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "61109010140000071219812874", false, "FV/{nr}/{MM}/{RRRR}",
            BankSwift: "SHORT", ForeignBankAccount: "GB00WEST12345698765432"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<ValidationEnvelope>())!.Errors;
        Assert.Contains(errors, e => e is { Code: "SP-VAL-008", Field: "foreignBankAccount" });
        Assert.Contains(errors, e => e is { Code: "SP-VAL-009", Field: "bankSwift" });
    }

    private sealed record TenantIdDto(Guid Id);

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 12, 20, 12, 0, 0, TimeSpan.Zero);
    }
}
