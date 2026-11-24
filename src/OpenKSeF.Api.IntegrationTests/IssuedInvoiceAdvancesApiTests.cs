using System.Net;
using System.Net.Http.Json;
using System.Text;
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
public class IssuedInvoiceAdvancesApiTests : IDisposable
{
    private const string BuyerNip = "3333333333";
    private const string OtherUser = "otheruser";
    private const string Zal1Ksef = "5213456789-20261206-ABCDEF123456-AB";
    private const string Zal2Ksef = "5213456789-20261210-ABCDEF123457-AC";
    private const string RozKsef = "5213456789-20261220-ABCDEF123458-AD";

    private static readonly IssuedInvoiceBuyerModel Buyer = new("Company", BuyerNip, "Nabywca sp. z o.o.", "ul. Krótka 2", "00-002 Warszawa");
    private static readonly IssuedInvoiceLineRequest[] Order = [new("Projekt strony", "szt.", 1, 1000m, "23")];

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public IssuedInvoiceAdvancesApiTests(TestcontainersFixture fixture)
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

    private async Task<Guid> CreateTenantAsync(HttpClient? client = null, string? user = null)
    {
        client ??= _client;
        client.SetBearerToken(user is null ? await _fixture.GetAccessTokenAsync() : await _fixture.GetAccessTokenAsync(user));
        var response = await client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), displayName = "Advances test" });
        response.EnsureSuccessStatusCode();
        var tenantId = (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        (await client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
            "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "93000000002026111700000815", false,
            "FV/{nr}/{MM}/{RRRR}"))).EnsureSuccessStatusCode();
        return tenantId;
    }

    private static AdvanceDraftRequest Advance(decimal amount, int paymentDay, int issueDay, IssuedInvoiceBuyerModel? buyer = null, IssuedInvoiceLineRequest[]? lines = null, string? currency = null) =>
        new(buyer, new DateOnly(2026, 12, issueDay), new DateOnly(2026, 12, paymentDay), amount, "Transfer", currency, Lines: lines ?? Order);

    private async Task<IssuedInvoiceResponse> Read(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.True(expected == response.StatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
    }

    private async Task<IssuedInvoiceResponse> PutAdvanceAsync(Guid tenantId, Guid id, AdvanceDraftRequest request) =>
        await Read(await _client.PutAsJsonAsync($"{Base(tenantId)}/{id}/advance", request), HttpStatusCode.OK);

    private async Task<IssuedInvoiceResponse> IssueAsync(Guid tenantId, Guid id, string type) =>
        await Read(await _client.PostAsync($"{Base(tenantId)}/{id}/issue?invoiceType={type}&confirmPastIssueDate=true", null), HttpStatusCode.Accepted);

    private async Task<IssuedInvoiceResponse> AcceptedFirstAdvanceAsync(Guid tenantId, decimal amount = 369m)
    {
        var draft = await Read(await _client.PostAsJsonAsync($"{Base(tenantId)}/advances", Advance(amount, 5, 6, Buyer)), HttpStatusCode.Created);
        await IssueAsync(tenantId, draft.Id, "ZAL");
        await AcceptAsync(draft.Id, Zal1Ksef);
        return draft;
    }

    private async Task<IssuedInvoiceResponse> AcceptedNextAdvanceAsync(Guid tenantId, Guid fromId, decimal amount, string ksef = Zal2Ksef)
    {
        var next = await Read(await _client.PostAsync($"{Base(tenantId)}/{fromId}/advances", null), HttpStatusCode.Created);
        await PutAdvanceAsync(tenantId, next.Id, Advance(amount, 10, 10));
        await IssueAsync(tenantId, next.Id, "ZAL");
        await AcceptAsync(next.Id, ksef);
        return next;
    }

    private Task AcceptAsync(Guid id, string ksefNumber) => WithDbAsync(async db =>
    {
        var invoice = await db.IssuedInvoices.SingleAsync(i => i.Id == id);
        var now = DateTime.UtcNow;
        invoice.MarkSendStarted("session-1", now);
        invoice.MarkAccepted(ksefNumber, now, 200, "Sukces", now);
        return await db.SaveChangesAsync();
    });

    private async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private Task<string> XmlOf(Guid id) =>
        WithDbAsync(async db => Encoding.UTF8.GetString((await db.IssuedInvoices.SingleAsync(i => i.Id == id)).SentXml!));

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]?.GetValue<string>();

    private static async Task<List<string>> ErrorCodesOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["errors"]!.AsArray().Select(e => e!["code"]!.GetValue<string>()).ToList();

    [Fact]
    public async Task Chain_TwoAdvancesFinalAndKorRoz_SplitRemainingNumbersAndFixtures()
    {
        var tenantId = await CreateTenantAsync();
        var created = await _client.PostAsJsonAsync($"{Base(tenantId)}/advances", Advance(369m, 5, 6, Buyer));
        var draftJson = JsonNode.Parse(await created.Content.ReadAsStringAsync())!;
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var zalId = draftJson["id"]!.GetValue<Guid>();
        Assert.Equal((300m, 69m, 369m), (draftJson["totalNet"]!.GetValue<decimal>(), draftJson["totalVat"]!.GetValue<decimal>(), draftJson["totalGross"]!.GetValue<decimal>()));
        Assert.Equal((zalId, "ZAL"), (draftJson["orderChainId"]!.GetValue<Guid>(), draftJson["invoiceType"]!.GetValue<string>()));
        IssuedInvoicesApiTests.AssertMatchesFixture("issued-invoice-zal.json", draftJson);

        Assert.Equal("ZAL/1/12/2026", (await IssueAsync(tenantId, zalId, "ZAL")).Number);
        var zalXml = await XmlOf(zalId);
        Assert.Contains("<RodzajFaktury>ZAL</RodzajFaktury>", zalXml);
        Assert.Contains("<P_6>2026-12-05</P_6>", zalXml);
        Assert.Contains("<WartoscZamowienia>1230.00</WartoscZamowienia>", zalXml);
        Assert.DoesNotContain("<FaWiersz>", zalXml);
        await AcceptAsync(zalId, Zal1Ksef);

        var second = await Read(await _client.PostAsync($"{Base(tenantId)}/{zalId}/advances", null), HttpStatusCode.Created);
        Assert.Equal(("Nabywca sp. z o.o.", 1000m, 861m), (second.Buyer.Name, second.Lines.Single().NetAmount, second.Chain!.Remaining));
        var secondSaved = await PutAdvanceAsync(tenantId, second.Id, Advance(246m, 10, 10));
        Assert.Equal((200m, 46m, 246m), (secondSaved.TotalNet, secondSaved.TotalVat, secondSaved.TotalGross));
        Assert.Equal("ZAL/2/12/2026", (await IssueAsync(tenantId, second.Id, "ZAL")).Number);
        await AcceptAsync(second.Id, Zal2Ksef);

        var third = await Read(await _client.PostAsync($"{Base(tenantId)}/{zalId}/advances", null), HttpStatusCode.Created);
        Assert.Equal(615m, third.Chain!.Remaining);
        var tooMuch = await _client.PutAsJsonAsync($"{Base(tenantId)}/{third.Id}/advance", Advance(700m, 15, 15));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMuch.StatusCode);
        Assert.Equal([IssueChecks.AdvanceAmountCode], await ErrorCodesOf(tooMuch));
        var latePayment = await _client.PutAsJsonAsync($"{Base(tenantId)}/{third.Id}/advance", Advance(100m, 16, 15));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, latePayment.StatusCode);
        Assert.Equal([IssueChecks.PaymentDateCode], await ErrorCodesOf(latePayment));
        Assert.Equal("INV-CHAIN-OPEN", await CodeOf(await _client.PostAsync($"{Base(tenantId)}/{zalId}/final", null)));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{Base(tenantId)}/{third.Id}")).StatusCode);

        var final = await Read(await _client.PostAsync($"{Base(tenantId)}/{second.Id}/final", null), HttpStatusCode.Created);
        Assert.Equal(("ROZ", 500m, 115m, 615m), (final.InvoiceType, final.TotalNet, final.TotalVat, final.TotalGross));
        Assert.Equal([Zal1Ksef, Zal2Ksef], final.AdvanceInvoices!.Select(a => a.KsefNumber));
        Assert.Equal("FV/1/12/2026", (await IssueAsync(tenantId, final.Id, "ROZ")).Number);
        var rozXml = await XmlOf(final.Id);
        Assert.Contains($"<NrKSeFFaZaliczkowej>{Zal1Ksef}</NrKSeFFaZaliczkowej>", rozXml);
        Assert.Contains($"<NrKSeFFaZaliczkowej>{Zal2Ksef}</NrKSeFFaZaliczkowej>", rozXml);
        Assert.Contains("<P_15>615.00</P_15>", rozXml);
        Assert.DoesNotContain("<Zamowienie>", rozXml);
        await AcceptAsync(final.Id, RozKsef);
        IssuedInvoicesApiTests.AssertMatchesFixture("issued-invoice-roz.json", JsonNode.Parse(await _client.GetStringAsync($"{Base(tenantId)}/{final.Id}"))!);

        Assert.Equal("INV-CHAIN-CLOSED", await CodeOf(await _client.PostAsync($"{Base(tenantId)}/{zalId}/advances", null)));
        Assert.Equal("INV-ROZ-EXISTS", await CodeOf(await _client.PostAsync($"{Base(tenantId)}/{zalId}/final", null)));
        var useRoz = await _client.PostAsJsonAsync($"{Base(tenantId)}/{zalId}/corrections", new CorrectionCreateRequest());
        Assert.Equal(HttpStatusCode.Conflict, useRoz.StatusCode);
        Assert.Equal("INV-KOR-USE-ROZ", await CodeOf(useRoz));

        var korRoz = await Read(await _client.PostAsJsonAsync($"{Base(tenantId)}/{final.Id}/corrections", new CorrectionCreateRequest()), HttpStatusCode.Created);
        var korRozSaved = await Read(await _client.PutAsJsonAsync($"{Base(tenantId)}/{korRoz.Id}/correction", new CorrectionDraftRequest(
            CorrectionReason: "Rabat", Lines: [new CorrectionLineRequest(1, "Projekt strony", "szt.", 1, 900m, "23")])), HttpStatusCode.OK);
        Assert.Equal(("KOR_ROZ", -100m, -23m, -123m, 615m), (korRozSaved.InvoiceType, korRozSaved.TotalNet, korRozSaved.TotalVat, korRozSaved.TotalGross, korRozSaved.AmountBefore));
        Assert.Equal("KOR/1/12/2026", (await IssueAsync(tenantId, korRoz.Id, "KOR_ROZ")).Number);
        var korRozXml = await XmlOf(korRoz.Id);
        Assert.Contains("<RodzajFaktury>KOR_ROZ</RodzajFaktury>", korRozXml);
        Assert.Contains("<P_15ZK>615.00</P_15ZK>", korRozXml);
        await AcceptAsync(korRoz.Id, "5213456789-20261220-ABCDEF123459-AE");
        IssuedInvoicesApiTests.AssertMatchesFixture("issued-invoice-kor-roz.json", JsonNode.Parse(await _client.GetStringAsync($"{Base(tenantId)}/{korRoz.Id}"))!);
        IssuedInvoicesApiTests.AssertMatchesFixture("issued-invoice-chain.json", JsonNode.Parse(await _client.GetStringAsync($"{Base(tenantId)}/{zalId}"))!);

        var feed = await _client.GetFromJsonAsync<JsonObject>($"{Base(tenantId)}/feed?pageSize=20");
        var zalRow = feed!["items"]!.AsArray().First(i => i!["invoiceType"]!.GetValue<string>() == "ZAL")!;
        Assert.Null(zalRow["dueDate"]);
    }

    [Fact]
    public async Task KorZal_AmountDownThenFinalDeductsCorrectedAdvance()
    {
        var tenantId = await CreateTenantAsync();
        var zal = await AcceptedFirstAdvanceAsync(tenantId);

        var korZal = await Read(await _client.PostAsJsonAsync($"{Base(tenantId)}/{zal.Id}/corrections", new CorrectionCreateRequest()), HttpStatusCode.Created);
        Assert.Equal(("KOR_ZAL", 369m, 369m), (korZal.InvoiceType, korZal.AdvanceAmount, korZal.AmountBefore));
        var tooMuch = await _client.PutAsJsonAsync($"{Base(tenantId)}/{korZal.Id}/correction", new CorrectionDraftRequest(CorrectionReason: "x", AdvanceAmount: 1300m));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMuch.StatusCode);
        var saved = await Read(await _client.PutAsJsonAsync($"{Base(tenantId)}/{korZal.Id}/correction", new CorrectionDraftRequest(
            CorrectionReason: "Zwrot części zaliczki", AdvanceAmount: 246m)), HttpStatusCode.OK);
        Assert.Equal((-100m, -23m, -123m, 369m), (saved.TotalNet, saved.TotalVat, saved.TotalGross, saved.AmountBefore));
        Assert.Equal([true, false], saved.CorrectionRows!.Select(r => r.IsBefore));

        Assert.Equal("KOR/1/12/2026", (await IssueAsync(tenantId, korZal.Id, "KOR_ZAL")).Number);
        var xml = await XmlOf(korZal.Id);
        Assert.Contains("<RodzajFaktury>KOR_ZAL</RodzajFaktury>", xml);
        Assert.Contains("<P_15ZK>369.00</P_15ZK>", xml);
        Assert.Contains("<StanPrzedZ>1</StanPrzedZ>", xml);
        await AcceptAsync(korZal.Id, "5213456789-20261220-ABCDEF12345A-AF");
        IssuedInvoicesApiTests.AssertMatchesFixture("issued-invoice-kor-zal.json", JsonNode.Parse(await _client.GetStringAsync($"{Base(tenantId)}/{korZal.Id}"))!);
        var zalJson = JsonNode.Parse(await _client.GetStringAsync($"{Base(tenantId)}/{zal.Id}"))!;
        Assert.Equal((369m, 246m), (zalJson["totalGross"]!.GetValue<decimal>(), zalJson["currentState"]!["advanceAmount"]!.GetValue<decimal>()));

        var final = await Read(await _client.PostAsync($"{Base(tenantId)}/{zal.Id}/final", null), HttpStatusCode.Created);
        Assert.Equal((800m, 184m, 984m), (final.TotalNet, final.TotalVat, final.TotalGross));
    }

    [Fact]
    public async Task LastAdvanceListsEarlier_ZeroFinal_AndFinalBelowAdvancesBlocked()
    {
        var tenantId = await CreateTenantAsync();
        var zal = await AcceptedFirstAdvanceAsync(tenantId);
        var last = await AcceptedNextAdvanceAsync(tenantId, zal.Id, 861m);
        Assert.Contains($"<NrKSeFFaZaliczkowej>{Zal1Ksef}</NrKSeFFaZaliczkowej>", await XmlOf(last.Id));

        var final = await Read(await _client.PostAsync($"{Base(tenantId)}/{zal.Id}/final", null), HttpStatusCode.Created);
        Assert.Equal(0m, final.TotalGross);
        var below = await _client.PutAsJsonAsync($"{Base(tenantId)}/{final.Id}/final", new FinalDraftRequest(Lines: [new("Projekt strony", "szt.", 1, 400m, "23")]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, below.StatusCode);
        var error = (await below.Content.ReadFromJsonAsync<JsonObject>())!["errors"]![0]!;
        Assert.Equal((IssueChecks.AdvancesExceedCode, Advances.ExceedsMessage), (error["code"]!.GetValue<string>(), error["message"]!.GetValue<string>()));

        await IssueAsync(tenantId, final.Id, "ROZ");
        var xml = await XmlOf(final.Id);
        Assert.Contains("<P_15>0.00</P_15>", xml);
        Assert.Contains("<P_11>1000.00</P_11>", xml);
        Assert.DoesNotContain("<Platnosc>", xml);
    }

    [Fact]
    public async Task ChainLock_OtherBuyerOrCurrency422AndNothingChanges()
    {
        var tenantId = await CreateTenantAsync();
        var zal = await AcceptedFirstAdvanceAsync(tenantId);
        var next = await Read(await _client.PostAsync($"{Base(tenantId)}/{zal.Id}/advances", null), HttpStatusCode.Created);

        var otherBuyer = await _client.PutAsJsonAsync($"{Base(tenantId)}/{next.Id}/advance", Advance(100m, 10, 10, Buyer with { Name = "Inny nabywca" }));
        var otherCurrency = await _client.PutAsJsonAsync($"{Base(tenantId)}/{next.Id}/advance", Advance(100m, 10, 10, currency: "EUR"));

        foreach (var response in new[] { otherBuyer, otherCurrency })
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal([IssueChecks.ChainLockedCode], await ErrorCodesOf(response));
        }

        var reloaded = (await _client.GetFromJsonAsync<IssuedInvoiceResponse>($"{Base(tenantId)}/{next.Id}"))!;
        Assert.Equal((next.Version, "Nabywca sp. z o.o.", "PLN", 0m), (reloaded.Version, reloaded.Buyer.Name, reloaded.Currency, reloaded.AdvanceAmount));
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"{Base(tenantId)}/{next.Id}/advance", Advance(100m, 10, 10, Buyer, currency: "PLN"))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{Base(tenantId)}/{next.Id}")).StatusCode);
        var final = await Read(await _client.PostAsync($"{Base(tenantId)}/{zal.Id}/final", null), HttpStatusCode.Created);
        var finalOtherBuyer = await _client.PutAsJsonAsync($"{Base(tenantId)}/{final.Id}/final", new FinalDraftRequest(Buyer: Buyer with { Nip = "1111111111" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, finalOtherBuyer.StatusCode);
        Assert.Equal([IssueChecks.ChainLockedCode], await ErrorCodesOf(finalOtherBuyer));
    }

    [Fact]
    public async Task KorZalBuyerAddress_LaterDocumentsTakeTheCorrectedBuyer()
    {
        var tenantId = await CreateTenantAsync();
        var zal = await AcceptedFirstAdvanceAsync(tenantId);
        var korZal = await Read(await _client.PostAsJsonAsync($"{Base(tenantId)}/{zal.Id}/corrections", new CorrectionCreateRequest()), HttpStatusCode.Created);
        await Read(await _client.PutAsJsonAsync($"{Base(tenantId)}/{korZal.Id}/correction", new CorrectionDraftRequest(
            CorrectionReason: "Zmiana adresu nabywcy", Buyer: Buyer with { AddressLine1 = "ul. Nowa 5" })), HttpStatusCode.OK);
        await IssueAsync(tenantId, korZal.Id, "KOR_ZAL");
        Assert.Contains("<Podmiot2K>", await XmlOf(korZal.Id));
        await AcceptAsync(korZal.Id, "5213456789-20261220-ABCDEF12345B-AA");

        var next = await Read(await _client.PostAsync($"{Base(tenantId)}/{zal.Id}/advances", null), HttpStatusCode.Created);
        Assert.Equal("ul. Nowa 5", next.Buyer.AddressLine1);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{Base(tenantId)}/{next.Id}")).StatusCode);
        var final = await Read(await _client.PostAsync($"{Base(tenantId)}/{zal.Id}/final", null), HttpStatusCode.Created);
        Assert.Equal("ul. Nowa 5", final.Buyer.AddressLine1);
    }

    [Fact]
    public async Task ChainLock_ParallelNextAdvanceAndFinal_One201One409()
    {
        var tenantId = await CreateTenantAsync();
        var zal = await AcceptedFirstAdvanceAsync(tenantId);
        using var second = _factory.CreateClient();
        second.SetBearerToken(await _fixture.GetAccessTokenAsync());

        var responses = await Task.WhenAll(
            _client.PostAsync($"{Base(tenantId)}/{zal.Id}/advances", null),
            second.PostAsync($"{Base(tenantId)}/{zal.Id}/final", null));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        Assert.Equal("INV-CHAIN-OPEN", await CodeOf(responses.Single(r => r.StatusCode == HttpStatusCode.Conflict)));
        Assert.Equal(2, await WithDbAsync(db => db.IssuedInvoices.CountAsync(i => i.OrderChainId == zal.Id)));
    }

    [Fact]
    public async Task GbpNp1Advance_RatePrefilledFromDayBeforePayment()
    {
        var tenantId = await CreateTenantAsync();
        _factory.ExchangeRates.Quote = new ExchangeRateQuote(5.1234m, new DateOnly(2026, 12, 3));
        var foreign = new IssuedInvoiceBuyerModel("Foreign", null, "Fictitious Trading Ltd", "1 Example Street", "London", "GB", null, "GB000000000");

        var created = await Read(await _client.PostAsJsonAsync($"{Base(tenantId)}/advances",
            Advance(2000m, 4, 4, foreign, [new("Consulting", "szt.", 1, 5000m, "np I")], "GBP")), HttpStatusCode.Created);

        Assert.Contains(("GBP", new DateOnly(2026, 12, 4)), _factory.ExchangeRates.Calls);
        Assert.Equal((5.1234m, new DateOnly(2026, 12, 3)), (created.ExchangeRate, created.ExchangeRateDate));
        await IssueAsync(tenantId, created.Id, "ZAL");
        var xml = await XmlOf(created.Id);
        Assert.Contains("<KursWalutyZ>5.1234</KursWalutyZ>", xml);
        Assert.Contains("<P_13_8>2000.00</P_13_8>", xml);
        Assert.DoesNotContain("<P_14_", xml);
        Assert.Contains("<P_18>1</P_18>", xml);
    }

    [Fact]
    public async Task OlderClient_VatUpdateIssueAndCopyOnAdvance_Return409AndChangeNothing()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await Read(await _client.PostAsJsonAsync($"{Base(tenantId)}/advances", Advance(369m, 5, 6, Buyer)), HttpStatusCode.Created);
        const string mobileBody = """
            {"buyer":{"kind":"Company","nip":"3333333333","name":"Nabywca sp. z o.o.","addressLine1":"ul. Krótka 2","addressLine2":"00-002 Warszawa","countryCode":null,"idType":null,"foreignId":null},
             "issueDate":"2026-12-20","saleDate":"2026-12-20","dueDate":"2027-01-03","paymentMethod":"Transfer","splitPayment":false,"exemptionBasis":null,
             "lines":[{"name":"Projekt strony","unit":"szt.","quantity":1,"unitNetPrice":1000,"vatRate":"23"}]}
            """;

        var update = await _client.PutAsync($"{Base(tenantId)}/{draft.Id}?version={draft.Version}", new StringContent(mobileBody, Encoding.UTF8, "application/json"));
        var issue = await _client.PostAsync($"{Base(tenantId)}/{draft.Id}/issue?version={draft.Version}", null);
        var copy = await _client.PostAsync($"{Base(tenantId)}/{draft.Id}/copy", null);

        foreach (var response in new[] { update, issue, copy })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("INV-KIND-MISMATCH", await CodeOf(response));
        }

        var after = await WithDbAsync(db => db.IssuedInvoices.AsNoTracking().Select(i => new { i.Id, Version = EF.Property<uint>(i, "Version"), i.Status }).SingleAsync(i => i.Id == draft.Id));
        Assert.Equal((draft.Version, IssuedInvoiceStatus.Draft), (after.Version, after.Status));
        Assert.Equal(1, await WithDbAsync(db => db.IssuedInvoices.CountAsync(i => i.TenantId == tenantId)));
    }

    [Fact]
    public async Task TenantIsolation_OtherTenantGets404OnEveryChainEndpoint()
    {
        var tenantId = await CreateTenantAsync();
        var zal = await AcceptedFirstAdvanceAsync(tenantId);
        var next = await Read(await _client.PostAsync($"{Base(tenantId)}/{zal.Id}/advances", null), HttpStatusCode.Created);
        using var other = _factory.CreateClient();
        await CreateTenantAsync(other, OtherUser);

        var responses = new[]
        {
            await other.PostAsJsonAsync($"{Base(tenantId)}/advances", Advance(1m, 5, 6, Buyer)),
            await other.PostAsync($"{Base(tenantId)}/{zal.Id}/advances", null),
            await other.PutAsJsonAsync($"{Base(tenantId)}/{next.Id}/advance", Advance(1m, 10, 10)),
            await other.PostAsync($"{Base(tenantId)}/{zal.Id}/final", null),
            await other.PutAsJsonAsync($"{Base(tenantId)}/{next.Id}/final", new FinalDraftRequest()),
            await other.PostAsJsonAsync($"{Base(tenantId)}/{zal.Id}/corrections", new CorrectionCreateRequest()),
            await other.PostAsync($"{Base(tenantId)}/{next.Id}/issue?invoiceType=ZAL", null),
            await other.GetAsync($"{Base(tenantId)}/{zal.Id}"),
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal(2, await WithDbAsync(db => db.IssuedInvoices.CountAsync(i => i.TenantId == tenantId)));
    }

    [Fact]
    public async Task SellerProfile_AdvanceSeriesPatternAndNextNumber()
    {
        var tenantId = await CreateTenantAsync();
        var saved = await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
            "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "93000000002026111700000815", false,
            "FV/{nr}/{MM}/{RRRR}", AdvanceNumberingPattern: "ZAL/{RRRR}/{nr}", AdvanceNextNumber: 7, AdvanceNextNumberPeriod: "2026"));
        var profile = (await saved.Content.ReadFromJsonAsync<SellerProfileResponse>())!;
        Assert.Equal(("ZAL/{RRRR}/{nr}", 7, "ZAL/2026/7"), (profile.AdvanceNumberingPattern, profile.AdvanceNextNumber, profile.AdvanceNextNumberPreview));

        var draft = await Read(await _client.PostAsJsonAsync($"{Base(tenantId)}/advances", Advance(369m, 5, 6, Buyer)), HttpStatusCode.Created);
        Assert.Equal("ZAL/2026/7", (await IssueAsync(tenantId, draft.Id, "ZAL")).Number);

        var clash = await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
            "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "93000000002026111700000815", false,
            "FV/{nr}/{MM}/{RRRR}", AdvanceNumberingPattern: "FV/{nr}/{MM}/{RRRR}"));
        Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 12, 20, 12, 0, 0, TimeSpan.Zero);
    }
}
