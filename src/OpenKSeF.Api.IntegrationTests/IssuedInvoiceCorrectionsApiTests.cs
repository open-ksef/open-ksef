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
public class IssuedInvoiceCorrectionsApiTests : IDisposable
{
    private const string BuyerNip = "3333333333";
    private const string OtherUser = "otheruser";
    private const string OriginalKsef = "5213456789-20261103-ABCDEF123456-AB";

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public IssuedInvoiceCorrectionsApiTests(TestcontainersFixture fixture)
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
        var response = await client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), displayName = "Corrections test" });
        response.EnsureSuccessStatusCode();
        var tenantId = (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        (await client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
            "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "93000000002026111700000815", false,
            "FV/{nr}/{MM}/{RRRR}"))).EnsureSuccessStatusCode();
        return tenantId;
    }

    private async Task<Guid> AcceptedOriginalAsync(Guid tenantId, params IssuedInvoiceLineRequest[] lines)
    {
        var created = await _client.PostAsJsonAsync(Base(tenantId), new IssuedInvoiceDraftRequest(
            new IssuedInvoiceBuyerModel("Company", BuyerNip, "Nabywca sp. z o.o.", "ul. Krótka 2", "00-002 Warszawa"),
            new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 17), "Transfer", false, null,
            lines.Length > 0 ? lines : [new IssuedInvoiceLineRequest("Usługa", "szt.", 1, 100m, "23")]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!.Id;
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"{Base(tenantId)}/{id}/issue?confirmPastIssueDate=true", null)).StatusCode);
        await AcceptAsync(id, OriginalKsef);
        return id;
    }

    private Task AcceptAsync(Guid id, string ksefNumber = "5213456789-20261220-ABCDEF123456-CD") => WithDbAsync(async db =>
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

    private async Task<HttpResponseMessage> CreateCorrectionAsync(Guid tenantId, Guid originalId, string? mode = null, HttpClient? client = null) =>
        await (client ?? _client).PostAsJsonAsync($"{Base(tenantId)}/{originalId}/corrections", new CorrectionCreateRequest(mode));

    private async Task<IssuedInvoiceResponse> NewCorrectionAsync(Guid tenantId, Guid originalId, string? mode = null)
    {
        var response = await CreateCorrectionAsync(tenantId, originalId, mode);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
    }

    private async Task<HttpResponseMessage> PutCorrectionAsync(Guid tenantId, Guid id, CorrectionDraftRequest request) =>
        await _client.PutAsJsonAsync($"{Base(tenantId)}/{id}/correction", request);

    private async Task<HttpResponseMessage> IssueCorrectionAsync(Guid tenantId, Guid id) =>
        await _client.PostAsync($"{Base(tenantId)}/{id}/issue?invoiceType=KOR&confirmPastIssueDate=true", null);

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]?.GetValue<string>();

    private static async Task<JsonArray> ErrorsOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["errors"]!.AsArray();

    [Fact]
    public async Task QuantityToZero_CreateEditIssueAccept_ViewsNumberingAndReissue()
    {
        var tenantId = await CreateTenantAsync();
        var originalId = await AcceptedOriginalAsync(tenantId);

        var created = await CreateCorrectionAsync(tenantId, originalId);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draftJson = JsonNode.Parse(await created.Content.ReadAsStringAsync())!;
        var correctionId = draftJson["id"]!.GetValue<Guid>();
        IssuedInvoicesApiTests.AssertMatchesFixture("issued-invoice-correction-draft.json", draftJson);

        var put = await PutCorrectionAsync(tenantId, correctionId, new CorrectionDraftRequest(
            CorrectionReason: "Zwrot towaru",
            Lines: [new CorrectionLineRequest(1, "Usługa", "szt.", 0, 100m, "23")]));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var saved = (await put.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
        Assert.Equal((-100m, -23m, -123m), (saved.TotalNet, saved.TotalVat, saved.TotalGross));
        Assert.Equal([true, false], saved.CorrectionRows!.Select(r => r.IsBefore));
        Assert.Equal(correctionId, (await _client.GetFromJsonAsync<IssuedInvoiceResponse>($"{Base(tenantId)}/{originalId}"))!.OpenCorrectionId);

        var issued = await IssueCorrectionAsync(tenantId, correctionId);
        Assert.Equal(HttpStatusCode.Accepted, issued.StatusCode);
        Assert.Equal("KOR/1/12/2026", (await issued.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!.Number);
        var xml = Encoding.UTF8.GetString((await WithDbAsync(db => db.IssuedInvoices.SingleAsync(i => i.Id == correctionId))).SentXml!);
        Assert.Contains("<RodzajFaktury>KOR</RodzajFaktury>", xml);
        Assert.Contains($"<NrKSeFFaKorygowanej>{OriginalKsef}</NrKSeFFaKorygowanej>", xml);

        await AcceptAsync(correctionId);
        IssuedInvoicesApiTests.AssertMatchesFixture("issued-invoice-correction-accepted.json", JsonNode.Parse(await _client.GetStringAsync($"{Base(tenantId)}/{correctionId}"))!);
        IssuedInvoicesApiTests.AssertMatchesFixture("issued-invoice-original-with-corrections.json", JsonNode.Parse(await _client.GetStringAsync($"{Base(tenantId)}/{originalId}"))!);

        var list = await _client.GetFromJsonAsync<JsonObject>($"{Base(tenantId)}?pageSize=10");
        Assert.Contains(list!["items"]!.AsArray(), i => i!["invoiceType"]!.GetValue<string>() == "KOR" && i["totalGross"]!.GetValue<decimal>() == -123m);
        var feed = await _client.GetFromJsonAsync<JsonObject>($"{Base(tenantId)}/feed?pageSize=10");
        var korRow = feed!["items"]!.AsArray().Single(i => i!["invoiceType"]!.GetValue<string>() == "KOR")!;
        Assert.Null(korRow["dueDate"]);
        Assert.NotNull(feed["items"]!.AsArray().First(i => i!["invoiceType"]!.GetValue<string>() == "VAT")!["dueDate"]);

        var reissue = await _client.PostAsync($"{Base(tenantId)}/{originalId}/reissue", null);
        Assert.Equal(HttpStatusCode.Created, reissue.StatusCode);
        var fresh = (await reissue.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
        Assert.Equal(("VAT", BuyerNip, 100m), (fresh.InvoiceType, fresh.Buyer.Nip, fresh.TotalNet));

        var next = await AcceptedOriginalAsync(tenantId);
        Assert.Equal("FV/2/11/2026", (await _client.GetFromJsonAsync<IssuedInvoiceResponse>($"{Base(tenantId)}/{next}"))!.Number);
    }

    [Fact]
    public async Task WrongBuyerId_ToZeroThenReissueWithEmptyNip()
    {
        var tenantId = await CreateTenantAsync();
        var originalId = await AcceptedOriginalAsync(tenantId);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync($"{Base(tenantId)}/{originalId}/reissue", null)).StatusCode);

        var correction = await NewCorrectionAsync(tenantId, originalId, "WrongBuyerId");
        Assert.Equal(("Błędny NIP nabywcy", -123m), (correction.CorrectionReason, correction.TotalGross));
        Assert.Equal(HttpStatusCode.Accepted, (await IssueCorrectionAsync(tenantId, correction.Id)).StatusCode);
        await AcceptAsync(correction.Id);

        var reissue = await _client.PostAsync($"{Base(tenantId)}/{originalId}/reissue", null);
        Assert.Equal(HttpStatusCode.Created, reissue.StatusCode);
        var fresh = (await reissue.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
        Assert.Null(fresh.Buyer.Nip);
        Assert.Equal(("Nabywca sp. z o.o.", 123m), (fresh.Buyer.Name, fresh.TotalGross));
    }

    [Fact]
    public async Task SecondCorrection_StartsFromStateAfterFirst_AndNamesOriginal()
    {
        var tenantId = await CreateTenantAsync();
        var originalId = await AcceptedOriginalAsync(tenantId, new IssuedInvoiceLineRequest("Usługa", "szt.", 1, 1000m, "23"));
        var first = await NewCorrectionAsync(tenantId, originalId);
        (await PutCorrectionAsync(tenantId, first.Id, new CorrectionDraftRequest(CorrectionReason: "Rabat", Lines: [new(1, "Usługa", "szt.", 1, 900m, "23")]))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Accepted, (await IssueCorrectionAsync(tenantId, first.Id)).StatusCode);
        await AcceptAsync(first.Id);

        var second = await NewCorrectionAsync(tenantId, originalId);
        Assert.Equal(900m, second.Lines.Single().UnitNetPrice);
        var put = await PutCorrectionAsync(tenantId, second.Id, new CorrectionDraftRequest(CorrectionReason: "Rabat", Lines: [new(1, "Usługa", "szt.", 1, 700m, "23")]));
        var saved = (await put.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
        Assert.Equal((-200m, -46m, -246m), (saved.TotalNet, saved.TotalVat, saved.TotalGross));
        Assert.Equal(900m, saved.CorrectionRows![0].NetAmount);
        Assert.Equal(originalId, saved.CorrectedInvoice!.Id);

        Assert.Equal(HttpStatusCode.Accepted, (await IssueCorrectionAsync(tenantId, second.Id)).StatusCode);
        var xml = Encoding.UTF8.GetString((await WithDbAsync(db => db.IssuedInvoices.SingleAsync(i => i.Id == second.Id))).SentXml!);
        Assert.Contains("<NrFaKorygowanej>FV/1/11/2026</NrFaKorygowanej>", xml);
        Assert.Contains("<P_15>-246.00</P_15>", xml);
        Assert.Contains("KOR/2/12/2026", xml);
    }

    [Fact]
    public async Task OneOpenCorrection_ParallelCreates_One201One409()
    {
        var tenantId = await CreateTenantAsync();
        var originalId = await AcceptedOriginalAsync(tenantId);
        using var second = _factory.CreateClient();
        second.SetBearerToken(await _fixture.GetAccessTokenAsync());

        var responses = await Task.WhenAll(CreateCorrectionAsync(tenantId, originalId), CreateCorrectionAsync(tenantId, originalId, client: second));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        var conflict = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("INV-KOR-OPEN", await CodeOf(conflict));
        Assert.Equal(1, await WithDbAsync(db => db.IssuedInvoices.CountAsync(i => i.CorrectedInvoiceId == originalId)));
    }

    [Fact]
    public async Task Source_NotAcceptedOrACorrection_Returns409()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await _client.PostAsJsonAsync(Base(tenantId), new IssuedInvoiceDraftRequest(null, null, null, null, null, false, null, null));
        var draftId = (await draft.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!.Id;
        Assert.Equal("INV-KOR-SOURCE", await CodeOf(await CreateCorrectionAsync(tenantId, draftId)));

        var originalId = await AcceptedOriginalAsync(tenantId);
        var correction = await NewCorrectionAsync(tenantId, originalId);
        Assert.Equal("INV-KOR-SOURCE", await CodeOf(await CreateCorrectionAsync(tenantId, correction.Id)));
    }

    [Fact]
    public async Task OlderClient_VatUpdateIssueAndCopyOnCorrection_Return409AndChangeNothing()
    {
        var tenantId = await CreateTenantAsync();
        var originalId = await AcceptedOriginalAsync(tenantId);
        var correction = await NewCorrectionAsync(tenantId, originalId);
        var before = await WithDbAsync(db => db.IssuedInvoices.AsNoTracking().Select(i => new { i.Id, Version = EF.Property<uint>(i, "Version") }).SingleAsync(i => i.Id == correction.Id));
        const string mobileBody = """
            {"buyer":{"kind":"Company","nip":"3333333333","name":"Nabywca sp. z o.o.","addressLine1":"ul. Krótka 2","addressLine2":"00-002 Warszawa","countryCode":null,"idType":null,"foreignId":null},
             "issueDate":"2026-12-20","saleDate":"2026-12-20","dueDate":"2027-01-03","paymentMethod":"Transfer","splitPayment":false,"exemptionBasis":null,
             "lines":[{"name":"Usługa","unit":"szt.","quantity":1,"unitNetPrice":100,"vatRate":"23"}]}
            """;

        var update = await _client.PutAsync($"{Base(tenantId)}/{correction.Id}?version={correction.Version}", new StringContent(mobileBody, Encoding.UTF8, "application/json"));
        var issue = await _client.PostAsync($"{Base(tenantId)}/{correction.Id}/issue?version={correction.Version}", null);
        var copy = await _client.PostAsync($"{Base(tenantId)}/{correction.Id}/copy", null);
        var vatPutOnOriginal = await PutCorrectionAsync(tenantId, originalId, new CorrectionDraftRequest(CorrectionReason: "x"));

        foreach (var response in new[] { update, issue, copy, vatPutOnOriginal })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("INV-KIND-MISMATCH", await CodeOf(response));
        }

        var after = await WithDbAsync(db => db.IssuedInvoices.AsNoTracking().Select(i => new { i.Id, Version = EF.Property<uint>(i, "Version"), i.Status }).SingleAsync(i => i.Id == correction.Id));
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(IssuedInvoiceStatus.Draft, after.Status);
        Assert.Equal(2, await WithDbAsync(db => db.IssuedInvoices.CountAsync(i => i.TenantId == tenantId)));
    }

    [Fact]
    public async Task Validation_TaxIdChange422NothingSaved_NoChangeAndNoReasonOnIssue()
    {
        var tenantId = await CreateTenantAsync();
        var originalId = await AcceptedOriginalAsync(tenantId);
        var correction = await NewCorrectionAsync(tenantId, originalId);

        var nipChange = await PutCorrectionAsync(tenantId, correction.Id, new CorrectionDraftRequest(
            CorrectionReason: "Błąd w danych nabywcy",
            Buyer: new IssuedInvoiceBuyerModel("Company", "1111111111", "Inna", "ul. Krótka 2", null)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, nipChange.StatusCode);
        Assert.Contains(await ErrorsOf(nipChange), e => e!["code"]!.GetValue<string>() == "INV-VAL-083" && e["field"]!.GetValue<string>() == "buyer.nip");
        var stored = await WithDbAsync(db => db.IssuedInvoices.AsNoTracking().SingleAsync(i => i.Id == correction.Id));
        Assert.Equal((BuyerNip, "Nabywca sp. z o.o.", (string?)null), (stored.BuyerNip, stored.BuyerName, stored.CorrectionReason));

        var issue = await IssueCorrectionAsync(tenantId, correction.Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, issue.StatusCode);
        var codes = (await ErrorsOf(issue)).Select(e => e!["code"]!.GetValue<string>()).ToList();
        Assert.Contains("INV-VAL-082", codes);
        Assert.Contains("INV-VAL-084", codes);

        var early = await PutCorrectionAsync(tenantId, correction.Id, new CorrectionDraftRequest(IssueDate: new DateOnly(2026, 11, 2)));
        Assert.Contains(await ErrorsOf(early), e => e!["code"]!.GetValue<string>() == "INV-VAL-085" && e["field"]!.GetValue<string>() == "issueDate");
    }

    [Fact]
    public async Task DuplicateBaseLineNo_Returns422AndSavesNothing()
    {
        var tenantId = await CreateTenantAsync();
        var correction = await NewCorrectionAsync(tenantId, await AcceptedOriginalAsync(tenantId));

        var put = await PutCorrectionAsync(tenantId, correction.Id, new CorrectionDraftRequest(
            CorrectionReason: "Rabat", Lines: [new(1, "Usługa", "szt.", 1, 90m, "23"), new(1, "Usługa", "szt.", 1, 80m, "23")]));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, put.StatusCode);
        Assert.Contains(await ErrorsOf(put), e => e!["code"]!.GetValue<string>() == "INV-VAL-086" && e["field"]!.GetValue<string>() == "lines[1].baseLineNo");
        Assert.Equal(100m, (await WithDbAsync(db => db.IssuedInvoiceLines.AsNoTracking().SingleAsync(l => l.IssuedInvoiceId == correction.Id))).UnitNetPrice);
    }

    [Fact]
    public async Task RejectedCorrection_NumberTakenIsClosed_OtherRejectionStaysOpen()
    {
        var tenantId = await CreateTenantAsync();
        async Task<Guid> RejectedCorrectionAsync(Guid originalId, int code)
        {
            var correction = await NewCorrectionAsync(tenantId, originalId);
            (await PutCorrectionAsync(tenantId, correction.Id, new CorrectionDraftRequest(CorrectionReason: "Rabat", Lines: [new(1, "Usługa", "szt.", 1, 90m, "23")]))).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Accepted, (await IssueCorrectionAsync(tenantId, correction.Id)).StatusCode);
            await WithDbAsync(async db =>
            {
                var invoice = await db.IssuedInvoices.SingleAsync(i => i.Id == correction.Id);
                invoice.MarkSendStarted("session-1", DateTime.UtcNow);
                invoice.MarkRejected(code, "Odrzucona", "details", DateTime.UtcNow);
                return await db.SaveChangesAsync();
            });
            return correction.Id;
        }

        var semanticOriginal = await AcceptedOriginalAsync(tenantId);
        await RejectedCorrectionAsync(semanticOriginal, 450);
        Assert.Equal("INV-KOR-OPEN", await CodeOf(await CreateCorrectionAsync(tenantId, semanticOriginal)));

        var takenOriginal = await AcceptedOriginalAsync(tenantId);
        var taken = await RejectedCorrectionAsync(takenOriginal, KSeFSentInvoice.Duplicate);
        Assert.Equal(HttpStatusCode.Created, (await CreateCorrectionAsync(tenantId, takenOriginal)).StatusCode);
        var resend = await IssueCorrectionAsync(tenantId, taken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resend.StatusCode);
        Assert.Contains(await ErrorsOf(resend), e => e!["code"]!.GetValue<string>() == "INV-VAL-023");
    }

    [Fact]
    public async Task KorNumberTakenByFvSeries_409PointsToCorrectionNextNumber()
    {
        var tenantId = await CreateTenantAsync();
        (await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
            "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "93000000002026111700000815", false,
            "A{nr}/{RRRR}", NextNumber: 11, NextNumberPeriod: "2026", CorrectionNumberingPattern: "A1{nr}/{RRRR}"))).EnsureSuccessStatusCode();
        var originalId = await AcceptedOriginalAsync(tenantId);
        Assert.Equal("A11/2026", (await _client.GetFromJsonAsync<IssuedInvoiceResponse>($"{Base(tenantId)}/{originalId}"))!.Number);
        var correction = await NewCorrectionAsync(tenantId, originalId);
        (await PutCorrectionAsync(tenantId, correction.Id, new CorrectionDraftRequest(CorrectionReason: "Rabat", Lines: [new(1, "Usługa", "szt.", 1, 90m, "23")]))).EnsureSuccessStatusCode();

        var issue = await IssueCorrectionAsync(tenantId, correction.Id);

        Assert.Equal(HttpStatusCode.Conflict, issue.StatusCode);
        Assert.Contains("następny numer korekt", (await issue.Content.ReadFromJsonAsync<JsonObject>())!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task PortalIssue_WithVersionAndSellerVersionFromSaveResponse_Returns202()
    {
        var tenantId = await CreateTenantAsync();
        var correction = await NewCorrectionAsync(tenantId, await AcceptedOriginalAsync(tenantId));
        var put = await PutCorrectionAsync(tenantId, correction.Id, new CorrectionDraftRequest(
            CorrectionReason: "Zwrot towaru",
            Lines: [new CorrectionLineRequest(1, "Usługa", "szt.", 0, 100m, "23")]));
        var saved = (await put.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;

        var issued = await _client.PostAsync(
            $"{Base(tenantId)}/{saved.Id}/issue?version={saved.Version}&sellerVersion={saved.SellerVersion}&confirmPastIssueDate=true&invoiceType=KOR", null);

        Assert.Equal(HttpStatusCode.Accepted, issued.StatusCode);
    }

    [Fact]
    public async Task CorrectionType_NullKeeps_ZeroClears()
    {
        var tenantId = await CreateTenantAsync();
        var correction = await NewCorrectionAsync(tenantId, await AcceptedOriginalAsync(tenantId));

        async Task<int?> Put(int? type) =>
            (await (await PutCorrectionAsync(tenantId, correction.Id, new CorrectionDraftRequest(CorrectionType: type))).Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!.CorrectionType;

        Assert.Equal(2, await Put(2));
        Assert.Equal(2, await Put(null));
        Assert.Null(await Put(0));
    }

    [Fact]
    public async Task CorrectionNumbering_OwnPatternAndNextNumber_DistinctFromFv()
    {
        var tenantId = await CreateTenantAsync();
        var same = await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
            "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "93000000002026111700000815", false,
            "FV/{nr}/{MM}/{RRRR}", CorrectionNumberingPattern: "FV/{nr}/{MM}/{RRRR}"));
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
        Assert.Contains(await ErrorsOf(same), e => e!["code"]!.GetValue<string>() == "SP-VAL-010");

        var next = await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
            "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "93000000002026111700000815", false,
            "FV/{nr}/{MM}/{RRRR}", CorrectionNextNumber: 5, CorrectionNextNumberPeriod: "2026-12"));
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        var profile = (await next.Content.ReadFromJsonAsync<SellerProfileResponse>())!;
        Assert.Equal(("KOR/{nr}/{MM}/{RRRR}", 5, "KOR/5/12/2026"), (profile.CorrectionNumberingPattern, profile.CorrectionNextNumber, profile.CorrectionNextNumberPreview));

        var correction = await NewCorrectionAsync(tenantId, await AcceptedOriginalAsync(tenantId));
        (await PutCorrectionAsync(tenantId, correction.Id, new CorrectionDraftRequest(CorrectionReason: "Rabat", Lines: [new(1, "Usługa", "szt.", 1, 90m, "23")]))).EnsureSuccessStatusCode();
        Assert.Equal("KOR/5/12/2026", (await (await IssueCorrectionAsync(tenantId, correction.Id)).Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!.Number);

        var below = await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
            "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "93000000002026111700000815", false,
            "FV/{nr}/{MM}/{RRRR}", CorrectionNextNumber: 5, CorrectionNextNumberPeriod: "2026-12"));
        Assert.Equal(HttpStatusCode.Conflict, below.StatusCode);
        Assert.Contains(await ErrorsOf(below), e => e!["code"]!.GetValue<string>() == "SP-VAL-006" && e["field"]!.GetValue<string>() == "correctionNextNumber");
    }

    [Fact]
    public async Task TenantIsolation_OtherTenantGets404OnEveryCorrectionEndpoint()
    {
        var tenantId = await CreateTenantAsync();
        var originalId = await AcceptedOriginalAsync(tenantId);
        var correction = await NewCorrectionAsync(tenantId, originalId);
        using var other = _factory.CreateClient();
        await CreateTenantAsync(other, OtherUser);

        var responses = new[]
        {
            await CreateCorrectionAsync(tenantId, originalId, client: other),
            await other.PutAsJsonAsync($"{Base(tenantId)}/{correction.Id}/correction", new CorrectionDraftRequest(CorrectionReason: "x")),
            await other.PostAsync($"{Base(tenantId)}/{originalId}/reissue", null),
            await other.PostAsync($"{Base(tenantId)}/{correction.Id}/issue?invoiceType=KOR", null),
            await other.PostAsync($"{Base(tenantId)}/{correction.Id}/copy?invoiceType=KOR", null),
            await other.GetAsync($"{Base(tenantId)}/{correction.Id}"),
            await other.DeleteAsync($"{Base(tenantId)}/{correction.Id}"),
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal(IssuedInvoiceStatus.Draft, (await WithDbAsync(db => db.IssuedInvoices.AsNoTracking().SingleAsync(i => i.Id == correction.Id))).Status);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 12, 20, 12, 0, 0, TimeSpan.Zero);
    }
}
