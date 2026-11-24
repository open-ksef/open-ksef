using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class InvoiceTemplatesApiTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 12, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 12, 20);

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public InvoiceTemplatesApiTests(TestcontainersFixture fixture)
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
        var response = await _client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), displayName = "Templates test" });
        response.EnsureSuccessStatusCode();
        var tenantId = (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        (await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
            "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "93000000002026111700000815", false,
            "FV/{nr}/{MM}/{RRRR}"))).EnsureSuccessStatusCode();
        return tenantId;
    }

    private async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private static Action<IssuedInvoice> Company(string nip, string name, string address, params (string Name, string Unit, decimal Price, VatRate Rate)[] lines) => invoice =>
    {
        invoice.SetBuyer(BuyerKind.Company, nip, name, address, "00-002 Warszawa");
        invoice.ReplaceLines((lines.Length > 0 ? lines : [("Usługa", "szt.", 100m, VatRate.Rate23)])
            .Select(l => ((string?)l.Name, (string?)l.Unit, 1m, l.Price, l.Rate)));
    };

    private static Action<IssuedInvoice> Foreign(string country, string? foreignId, string name, params (string Name, string Unit, decimal Price, VatRate Rate)[] lines) => invoice =>
    {
        invoice.SetBuyer(BuyerKind.Foreign, null, name, "1 Test Street", "Testville");
        invoice.SetForeignBuyer(country, foreignId ?? "");
        invoice.SetCurrency("GBP", 5.2m, null);
        invoice.ReplaceLines((lines.Length > 0 ? lines : [("Consulting", "h", 100m, VatRate.NotTaxable)])
            .Select(l => ((string?)l.Name, (string?)l.Unit, 1m, l.Price, l.Rate)));
    };

    private Task<Guid> SeedAsync(Guid tenantId, DateOnly issueDate, Action<IssuedInvoice> fill, IssuedInvoiceStatus status = IssuedInvoiceStatus.Accepted, int paymentDays = 14) =>
        WithDbAsync(async db =>
        {
            var invoice = IssuedInvoice.Draft(Guid.NewGuid(), tenantId, issueDate, Now);
            fill(invoice);
            invoice.SetDates(issueDate, issueDate, issueDate.AddDays(paymentDays));
            if (status != IssuedInvoiceStatus.Draft)
            {
                var profile = await db.SellerProfiles.SingleAsync(p => p.TenantId == tenantId);
                var nip = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Nip).SingleAsync();
                invoice.Issue(profile, nip, $"FV/{Guid.NewGuid():N}", Now);
                invoice.MarkSendStarted("session-1", Now);
                if (status == IssuedInvoiceStatus.Accepted)
                    invoice.MarkAccepted($"{nip}-{issueDate:yyyyMMdd}-ABCDEF123456-AB", Now, 200, "Sukces", Now);
                else
                    invoice.MarkRejected(450, "Błąd", null, Now);
            }

            db.IssuedInvoices.Add(invoice);
            await db.SaveChangesAsync();
            return invoice.Id;
        });

    private async Task<IssuedInvoiceResponse> CreateDraftAsync(Guid tenantId, IssuedInvoiceDraftRequest request)
    {
        var response = await _client.PostAsJsonAsync(Base(tenantId), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
    }

    private async Task<InvoiceTemplateResponse> SaveTemplateAsync(Guid tenantId, Guid sourceId, string name)
    {
        var response = await _client.PostAsJsonAsync($"{Base(tenantId)}/{sourceId}/template", new InvoiceTemplateRequest(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InvoiceTemplateResponse>())!;
    }

    private async Task<IssuedInvoiceResponse> PostCreatedAsync(string url)
    {
        var response = await _client.PostAsync(url, null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
    }

    private Task<List<InvoiceTemplateResponse>> TemplatesAsync(Guid tenantId) =>
        _client.GetFromJsonAsync<List<InvoiceTemplateResponse>>($"{Base(tenantId)}/templates")!;

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]?.GetValue<string>();

    private static (int, string, string, decimal, decimal, string)[] LinesOf(IssuedInvoiceResponse invoice) =>
        invoice.Lines.Select(l => (l.LineNo, l.Name, l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate)).ToArray();

    [Fact]
    public async Task RecentBuyers_OnePerKeyWithNewestData_OnlyAcceptedSales()
    {
        var tenantId = await CreateTenantAsync();
        var nip = TestNip.New();
        await SeedAsync(tenantId, new DateOnly(2026, 11, 3), Company(nip, "Klient sp. z o.o.", "ul. Stara 1"));
        await SeedAsync(tenantId, new DateOnly(2026, 11, 10), Company(nip, "Klient sp. z o.o.", "ul. Nowa 2"));
        await SeedAsync(tenantId, new DateOnly(2026, 11, 12), Company(TestNip.New(), "Szkic sp. z o.o.", "ul. Szkicowa 1"), IssuedInvoiceStatus.Draft);
        await SeedAsync(tenantId, new DateOnly(2026, 11, 12), Company(TestNip.New(), "Odrzucony sp. z o.o.", "ul. Błędna 1"), IssuedInvoiceStatus.Rejected);

        var buyers = (await _client.GetFromJsonAsync<List<RecentBuyerResponse>>($"{Base(tenantId)}/recent-buyers"))!;

        var buyer = Assert.Single(buyers);
        Assert.Equal(new IssuedInvoiceBuyerModel("Company", nip, "Klient sp. z o.o.", "ul. Nowa 2", "00-002 Warszawa"), buyer.Buyer);
        Assert.Equal(new DateOnly(2026, 11, 10), buyer.LastIssueDate);
    }

    [Fact]
    public async Task RecentBuyers_ForeignWithoutId_SearchOnNewNameAndNip_CappedAt20()
    {
        var tenantId = await CreateTenantAsync();
        await SeedAsync(tenantId, new DateOnly(2026, 11, 1), Foreign("GB", null, "Example Buyer Ltd"));
        var renamedNip = TestNip.New();
        await SeedAsync(tenantId, new DateOnly(2026, 10, 1), Company(renamedNip, "Dawna Nazwa sp. z o.o.", "ul. Krótka 2"));
        await SeedAsync(tenantId, new DateOnly(2026, 10, 2), Company(renamedNip, "Nowa Firma sp. z o.o.", "ul. Krótka 2"));
        for (var i = 0; i < 25; i++)
            await SeedAsync(tenantId, new DateOnly(2026, 9, 1).AddDays(i), Company(TestNip.New(), $"Masowy {i:D2}", "ul. Długa 1"));

        var all = (await _client.GetFromJsonAsync<List<RecentBuyerResponse>>($"{Base(tenantId)}/recent-buyers"))!;
        Assert.Equal(20, all.Count);
        Assert.Equal(new IssuedInvoiceBuyerModel("Foreign", null, "Example Buyer Ltd", "1 Test Street", "Testville", "GB", "None", null), all[0].Buyer);

        var byOldName = (await _client.GetFromJsonAsync<List<RecentBuyerResponse>>($"{Base(tenantId)}/recent-buyers?q=dawna"))!;
        Assert.Empty(byOldName);
        var byName = Assert.Single((await _client.GetFromJsonAsync<List<RecentBuyerResponse>>($"{Base(tenantId)}/recent-buyers?q=nowa%20firma"))!);
        Assert.Equal(renamedNip, byName.Buyer.Nip);
        var byNip = Assert.Single((await _client.GetFromJsonAsync<List<RecentBuyerResponse>>($"{Base(tenantId)}/recent-buyers?q={renamedNip}"))!);
        Assert.Equal("Nowa Firma sp. z o.o.", byNip.Buyer.Name);
    }

    [Fact]
    public async Task LineSuggestions_NewestPerName_RatesOfBuyerKind_MinTwoChars()
    {
        var tenantId = await CreateTenantAsync();
        await SeedAsync(tenantId, new DateOnly(2026, 10, 1), Company(TestNip.New(), "A", "ul. A 1", ("usługa KSIĘGOWA - pakiet s", "szt.", 450m, VatRate.Rate23)));
        await SeedAsync(tenantId, new DateOnly(2026, 11, 1), Company(TestNip.New(), "B", "ul. B 1", ("Usługa księgowa - pakiet S", "usł.", 500m, VatRate.Rate23)));
        await SeedAsync(tenantId, new DateOnly(2026, 11, 2), Foreign("GB", "GB-TEST-1", "C", ("Usługa księgowa - eksport", "h", 120m, VatRate.NotTaxable)));

        var company = (await _client.GetFromJsonAsync<List<LineSuggestionResponse>>($"{Base(tenantId)}/line-suggestions?q=księg&buyerKind=Company"))!;
        Assert.Equal([new LineSuggestionResponse("Usługa księgowa - pakiet S", "usł.", 500m, "23")], company);
        var foreign = (await _client.GetFromJsonAsync<List<LineSuggestionResponse>>($"{Base(tenantId)}/line-suggestions?q=KSIĘG&buyerKind=Foreign"))!;
        Assert.Equal([new LineSuggestionResponse("Usługa księgowa - eksport", "h", 120m, "np I")], foreign);
        Assert.Empty((await _client.GetFromJsonAsync<List<LineSuggestionResponse>>($"{Base(tenantId)}/line-suggestions?q=k"))!);

        var bad = await _client.GetAsync($"{Base(tenantId)}/line-suggestions?q=księg&buyerKind=Martian");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var error = (await bad.Content.ReadFromJsonAsync<ValidationEnvelope>())!.Errors.Single();
        Assert.Equal(("INV-VAL-070", "buyerKind"), (error.Code, error.Field));
    }

    [Fact]
    public async Task Template_RoundTrip_IsASnapshot_RenameKeepsContent_DeleteRemoves()
    {
        var tenantId = await CreateTenantAsync();
        var buyerNip = TestNip.New();
        var source = await CreateDraftAsync(tenantId, new IssuedInvoiceDraftRequest(
            new IssuedInvoiceBuyerModel("Company", buyerNip, "Klient X", "ul. Krótka 2", "00-002 Warszawa"),
            new DateOnly(2026, 12, 1), new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 22),
            "Cash", true, new ExemptionBasisModel("Act", "art. 113 ust. 1 ustawy o VAT"),
            [new("Abonament", "mies.", 1, 300m, "23"), new("Wsparcie", "godz.", 2.5m, 120m, "zw")]));

        var template = await SaveTemplateAsync(tenantId, source.Id, "  Abonament X ");
        Assert.Equal(("Abonament X", 21, 2, "PLN"), (template.Name, template.PaymentDays, template.LineCount, template.Currency));
        Assert.Equal(source.Buyer, template.Buyer);

        var draft = await PostCreatedAsync($"{Base(tenantId)}/templates/{template.Id}/draft");
        Assert.Equal(("Draft", (string?)null), (draft.Status, draft.Number));
        Assert.Equal((Today, Today, Today.AddDays(21)), (draft.IssueDate, draft.SaleDate, draft.DueDate));
        Assert.Equal(source.Buyer, draft.Buyer);
        Assert.Equal(("Cash", true), (draft.PaymentMethod, draft.SplitPayment));
        Assert.Equal(source.ExemptionBasis, draft.ExemptionBasis);
        Assert.Equal(LinesOf(source), LinesOf(draft));

        (await _client.PutAsJsonAsync($"{Base(tenantId)}/{draft.Id}", new IssuedInvoiceDraftRequest(
            new IssuedInvoiceBuyerModel("Company", buyerNip, "Zmieniony", "ul. Inna 9", null),
            Today, Today, Today, "Transfer", false, null, [new("Inna", "szt.", 1, 1m, "23")]))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{Base(tenantId)}/{source.Id}")).StatusCode);

        Assert.Equal([template], await TemplatesAsync(tenantId));
        Assert.Equal(LinesOf(source), LinesOf(await PostCreatedAsync($"{Base(tenantId)}/templates/{template.Id}/draft")));

        var renamed = await _client.PutAsJsonAsync($"{Base(tenantId)}/templates/{template.Id}", new InvoiceTemplateRequest("Abonament Y"));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(template with { Name = "Abonament Y" }, await renamed.Content.ReadFromJsonAsync<InvoiceTemplateResponse>());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{Base(tenantId)}/templates/{template.Id}")).StatusCode);
        Assert.Empty(await TemplatesAsync(tenantId));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"{Base(tenantId)}/templates/{template.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync($"{Base(tenantId)}/templates/{template.Id}/draft", null)).StatusCode);
        Assert.Equal(0, await WithDbAsync(db => db.InvoiceTemplateLines.CountAsync(l => l.InvoiceTemplateId == template.Id)));
    }

    [Fact]
    public async Task Template_ForeignGbp_NewDraftGetsTodaysNbpRate()
    {
        var tenantId = await CreateTenantAsync();
        var source = await CreateDraftAsync(tenantId, new IssuedInvoiceDraftRequest(
            new IssuedInvoiceBuyerModel("Foreign", null, "Example Buyer Ltd", "1 Test Street", "Testville", "GB", null, null),
            new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 20), "Transfer", false, null,
            [new("Consulting", "h", 10, 200m, "np I")], "GBP", 5.5m));
        var template = await SaveTemplateAsync(tenantId, source.Id, "GB");
        _factory.ExchangeRates.Quote = new ExchangeRateQuote(5.3m, new DateOnly(2026, 12, 18));

        var draft = await PostCreatedAsync($"{Base(tenantId)}/templates/{template.Id}/draft");

        Assert.Equal(("Foreign", "GB", "None"), (draft.Buyer.Kind, draft.Buyer.CountryCode, draft.Buyer.IdType));
        Assert.Equal(("GBP", 5.3m, new DateOnly(2026, 12, 18)), (draft.Currency, draft.ExchangeRate, draft.ExchangeRateDate));
        Assert.Equal(Today.AddDays(14), draft.DueDate);
    }

    [Fact]
    public async Task TemplateAndCopy_OfAcceptedInvoiceWithAcceptedKor_UseCorrectedPrice()
    {
        var tenantId = await CreateTenantAsync();
        var originalId = await SeedAsync(tenantId, new DateOnly(2026, 11, 3), Company(TestNip.New(), "Klient", "ul. Krótka 2", ("Usługa", "szt.", 1000m, VatRate.Rate23)));
        var created = await _client.PostAsJsonAsync($"{Base(tenantId)}/{originalId}/corrections", new CorrectionCreateRequest("Edit"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var korId = (await created.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!.Id;
        (await _client.PutAsJsonAsync($"{Base(tenantId)}/{korId}/correction", new CorrectionDraftRequest(
            CorrectionReason: "Rabat", Lines: [new CorrectionLineRequest(1, "Usługa", "szt.", 1, 900m, "23")]))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"{Base(tenantId)}/{korId}/issue?invoiceType=KOR&confirmPastIssueDate=true", null)).StatusCode);
        await WithDbAsync(async db =>
        {
            var kor = await db.IssuedInvoices.SingleAsync(i => i.Id == korId);
            kor.MarkSendStarted("session-2", Now);
            kor.MarkAccepted("1111111111-20261220-ABCDEF123456-CD", Now, 200, "Sukces", Now);
            return await db.SaveChangesAsync();
        });

        var template = await SaveTemplateAsync(tenantId, originalId, "Po rabacie");
        var fromTemplate = await PostCreatedAsync($"{Base(tenantId)}/templates/{template.Id}/draft");
        var copy = await PostCreatedAsync($"{Base(tenantId)}/{originalId}/copy");

        Assert.Equal(900m, Assert.Single(fromTemplate.Lines).UnitNetPrice);
        Assert.Equal(900m, Assert.Single(copy.Lines).UnitNetPrice);

        var fromKor = await _client.PostAsJsonAsync($"{Base(tenantId)}/{korId}/template", new InvoiceTemplateRequest("KOR"));
        Assert.Equal(HttpStatusCode.Conflict, fromKor.StatusCode);
        Assert.Equal("INV-TEMPLATE-KIND", await CodeOf(fromKor));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("101")]
    public async Task TemplateName_EmptyOrTooLong_Returns400OnCreateAndRename(string? name)
    {
        name = name == "101" ? new string('x', 101) : name;
        var tenantId = await CreateTenantAsync();
        var source = await SeedAsync(tenantId, new DateOnly(2026, 11, 3), Company(TestNip.New(), "Klient", "ul. Krótka 2"), IssuedInvoiceStatus.Draft);
        var template = await SaveTemplateAsync(tenantId, source, "OK");

        foreach (var response in new[]
                 {
                     await _client.PostAsJsonAsync($"{Base(tenantId)}/{source}/template", new InvoiceTemplateRequest(name)),
                     await _client.PutAsJsonAsync($"{Base(tenantId)}/templates/{template.Id}", new InvoiceTemplateRequest(name)),
                 })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = (await response.Content.ReadFromJsonAsync<ValidationEnvelope>())!.Errors.Single();
            Assert.Equal(("INV-VAL-099", "name"), (error.Code, error.Field));
        }

        Assert.Equal("OK", Assert.Single(await TemplatesAsync(tenantId)).Name);
    }

    [Fact]
    public async Task Template_FromZalOrRoz_Returns409_WritesNothing()
    {
        var tenantId = await CreateTenantAsync();
        var advance = await _client.PostAsJsonAsync($"{Base(tenantId)}/advances", new AdvanceDraftRequest());
        Assert.Equal(HttpStatusCode.Created, advance.StatusCode);
        var zalId = (await advance.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!.Id;
        var rozId = await WithDbAsync(async db =>
        {
            var zal = await db.IssuedInvoices.Include(i => i.Lines).SingleAsync(i => i.Id == zalId);
            var roz = IssuedInvoice.ChainDocument(Guid.NewGuid(), IssuedInvoiceType.Roz, zal, zal, Today, Now);
            db.IssuedInvoices.Add(roz);
            await db.SaveChangesAsync();
            return roz.Id;
        });

        foreach (var id in new[] { zalId, rozId })
        {
            var response = await _client.PostAsJsonAsync($"{Base(tenantId)}/{id}/template", new InvoiceTemplateRequest("Zaliczka"));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("INV-TEMPLATE-KIND", await CodeOf(response));
        }

        Assert.Equal(0, await WithDbAsync(db => db.InvoiceTemplates.CountAsync(t => t.TenantId == tenantId)));
    }

    [Fact]
    public async Task CopyAndReissue_KeepSourcePaymentTerm()
    {
        var tenantId = await CreateTenantAsync();
        var sourceId = await SeedAsync(tenantId, new DateOnly(2026, 10, 1), Company(TestNip.New(), "Klient", "ul. Krótka 2"), paymentDays: 30);

        var copy = await PostCreatedAsync($"{Base(tenantId)}/{sourceId}/copy");
        Assert.Equal((Today, Today, Today.AddDays(30)), (copy.IssueDate, copy.SaleDate, copy.DueDate));

        var created = await _client.PostAsJsonAsync($"{Base(tenantId)}/{sourceId}/corrections", new CorrectionCreateRequest("ToZero"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var korId = (await created.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!.Id;
        (await _client.PutAsJsonAsync($"{Base(tenantId)}/{korId}/correction", new CorrectionDraftRequest(CorrectionReason: "Zwrot"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"{Base(tenantId)}/{korId}/issue?invoiceType=KOR&confirmPastIssueDate=true", null)).StatusCode);
        await WithDbAsync(async db =>
        {
            var kor = await db.IssuedInvoices.SingleAsync(i => i.Id == korId);
            kor.MarkSendStarted("session-2", Now);
            kor.MarkAccepted("1111111111-20261220-ABCDEF123456-CD", Now, 200, "Sukces", Now);
            return await db.SaveChangesAsync();
        });

        var reissue = await PostCreatedAsync($"{Base(tenantId)}/{sourceId}/reissue");
        Assert.Equal(Today.AddDays(30), reissue.DueDate);
        Assert.Equal(100m, reissue.TotalNet);
    }

    [Fact]
    public async Task DeletingCompany_RemovesItsTemplates()
    {
        var tenantId = await CreateTenantAsync();
        var source = await SeedAsync(tenantId, new DateOnly(2026, 11, 3), Company(TestNip.New(), "Klient", "ul. Krótka 2"), IssuedInvoiceStatus.Draft);
        var template = await SaveTemplateAsync(tenantId, source, "Do usunięcia");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/tenants/{tenantId}")).StatusCode);

        Assert.Equal(0, await WithDbAsync(db => db.InvoiceTemplates.CountAsync(t => t.TenantId == tenantId)));
        Assert.Equal(0, await WithDbAsync(db => db.InvoiceTemplateLines.CountAsync(l => l.InvoiceTemplateId == template.Id)));
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
