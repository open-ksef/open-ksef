using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using KSeF.Client.Core.Interfaces.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Models;
using OpenKSeF.Sync.Issuing;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class IssuedInvoicesApiTests : IDisposable
{
    private const string BuyerNip = "5261040828";
    private static readonly DateOnly Today = new(2026, 12, 20);

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public IssuedInvoicesApiTests(TestcontainersFixture fixture)
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

    private async Task<Guid> CreateTenantAsync(string? nip = null, bool withProfile = true, int? nextNumber = null, string? nextNumberPeriod = null)
    {
        _client.SetBearerToken(await _fixture.GetAccessTokenAsync());
        nip ??= $"521{Random.Shared.Next(1_000_000, 9_999_999)}";
        var response = await _client.PostAsJsonAsync("/api/tenants", new { nip, displayName = "Issued invoices test" });
        response.EnsureSuccessStatusCode();
        var tenantId = (await response.Content.ReadFromJsonAsync<TenantIdDto>())!.Id;

        if (withProfile)
        {
            var profile = await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
                "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "61109010140000071219812874", false,
                "FV/{nr}/{MM}/{RRRR}", nextNumber, nextNumberPeriod));
            profile.EnsureSuccessStatusCode();
        }

        return tenantId;
    }

    private static IssuedInvoiceDraftRequest Draft(
        string? buyerNip = BuyerNip,
        string issueDate = "2026-11-03",
        string buyerName = "Nabywca sp. z o.o.",
        decimal unitNetPrice = 100.00m,
        string vatRate = "23") =>
        new(
            new IssuedInvoiceBuyerModel("Company", buyerNip, buyerName, "ul. Krótka 2", "00-002 Warszawa"),
            DateOnly.Parse(issueDate),
            DateOnly.Parse(issueDate),
            DateOnly.Parse(issueDate).AddDays(14),
            "Transfer",
            false,
            null,
            [new IssuedInvoiceLineRequest("Usługa", "szt.", 1, unitNetPrice, vatRate)]);

    private async Task<IssuedInvoiceResponse> CreateDraftAsync(Guid tenantId, IssuedInvoiceDraftRequest? request = null)
    {
        var response = await _client.PostAsJsonAsync(Base(tenantId), request ?? Draft());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
    }

    private async Task<IssuedInvoiceResponse> IssueAsync(Guid tenantId, Guid id, HttpStatusCode expected = HttpStatusCode.Accepted)
    {
        var response = await _client.PostAsync($"{Base(tenantId)}/{id}/issue", null);
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
    }

    private async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private Task<IssuedInvoice> LoadAsync(Guid id) =>
        WithDbAsync(db => db.IssuedInvoices.AsNoTracking().Include(i => i.Lines).SingleAsync(i => i.Id == id));

    private Task MutateAsync(Guid id, Action<IssuedInvoice> change) => WithDbAsync(async db =>
    {
        var invoice = await db.IssuedInvoices.Include(i => i.Lines).SingleAsync(i => i.Id == id);
        change(invoice);
        return await db.SaveChangesAsync();
    });

    private static void MarkSending(IssuedInvoice invoice)
    {
        var now = DateTime.UtcNow;
        invoice.MarkSendStarted("session-1", now);
        invoice.RecordElementRef("element-1", now);
    }

    private static void MarkAccepted(IssuedInvoice invoice)
    {
        var now = DateTime.UtcNow;
        MarkSending(invoice);
        invoice.MarkAccepted("5213456789-20261103-ABCDEF123456-AB", now, 200, "Sukces", now);
    }

    private static void MarkRejected(IssuedInvoice invoice)
    {
        var now = DateTime.UtcNow;
        MarkSending(invoice);
        invoice.MarkRejected(450, "Błąd weryfikacji semantyki", "P_2: niepoprawna wartość", now);
    }

    private async Task SetPatternAsync(Guid tenantId, string pattern)
    {
        var profile = await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
            "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "61109010140000071219812874", false, pattern, null, null));
        profile.EnsureSuccessStatusCode();
    }

    private async Task<Guid> SeedRejectedAsync(Guid tenantId, Action<IssuedInvoice>? reject = null)
    {
        var draft = await CreateDraftAsync(tenantId);
        await IssueAsync(tenantId, draft.Id);
        await MutateAsync(draft.Id, reject ?? MarkRejected);
        return draft.Id;
    }

    private async Task AssertPeriodBlockedAsync(Guid tenantId, Guid id, string issueDate, string number)
    {
        var counters = await WithDbAsync(db => db.InvoiceNumberCounters.Where(c => c.TenantId == tenantId).OrderBy(c => c.Period).Select(c => c.Next).ToListAsync());
        var response = await _client.PutAsJsonAsync($"{Base(tenantId)}/{id}", Draft(issueDate: issueDate, buyerName: "Nie zapisano"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<ValidationEnvelope>())!.Errors.Single();
        Assert.Equal(("INV-VAL-022", "issueDate"), (error.Code, error.Field));
        Assert.Contains($"Numer {number} należy do okresu", error.Message);

        var stored = await LoadAsync(id);
        Assert.Equal(number, stored.Number);
        Assert.Equal("Nabywca sp. z o.o.", stored.BuyerName);
        Assert.Equal(new DateOnly(2026, 11, 3), stored.IssueDate);
        Assert.Equal(counters, await WithDbAsync(db => db.InvoiceNumberCounters.Where(c => c.TenantId == tenantId).OrderBy(c => c.Period).Select(c => c.Next).ToListAsync()));
    }

    private async Task<(Guid TenantId, Guid InvoiceId)> SeedForeignInvoiceAsync()
    {
        var tenantId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        await WithDbAsync(db =>
        {
            db.Tenants.Add(new Tenant { Id = tenantId, UserId = $"someone-else-{tenantId}", Nip = "5261040828", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            var invoice = IssuedInvoice.Draft(invoiceId, tenantId, new DateOnly(2026, 11, 3), DateTime.UtcNow);
            invoice.SetBuyer(BuyerKind.Company, BuyerNip, "Cudzy nabywca", "ul. Obca 1", null);
            db.IssuedInvoices.Add(invoice);
            return db.SaveChangesAsync();
        });
        return (tenantId, invoiceId);
    }

    private static async Task<ValidationEnvelope> ReadEnvelopeAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ValidationEnvelope>())!;
    }

    [Fact]
    public async Task EveryRoute_ForeignTenantOrForeignInvoice_Returns404_AndChangesNothing()
    {
        var ownTenantId = await CreateTenantAsync();
        var (foreignTenantId, foreignInvoiceId) = await SeedForeignInvoiceAsync();

        foreach (var (tenantId, invoiceId) in new[] { (foreignTenantId, foreignInvoiceId), (ownTenantId, foreignInvoiceId), (Guid.NewGuid(), Guid.NewGuid()) })
        {
            var item = $"{Base(tenantId)}/{invoiceId}";
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(item)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync(item, Draft())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync(item)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync($"{item}/issue", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync($"{item}/copy", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{item}/xml")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{item}/upo")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Base(foreignTenantId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync(Base(foreignTenantId), Draft())).StatusCode);

        var untouched = await LoadAsync(foreignInvoiceId);
        Assert.Equal(IssuedInvoiceStatus.Draft, untouched.Status);
        Assert.Equal("Cudzy nabywca", untouched.BuyerName);
        Assert.Equal(1, await WithDbAsync(db => db.IssuedInvoices.CountAsync(i => i.TenantId == foreignTenantId)));
    }

    [Fact]
    public async Task List_DoesNotShowOtherTenantsInvoices()
    {
        var tenantId = await CreateTenantAsync();
        await SeedForeignInvoiceAsync();
        await CreateDraftAsync(tenantId);

        var page = await _client.GetFromJsonAsync<PagedResult<IssuedInvoiceListItemResponse>>(Base(tenantId));

        Assert.Equal(1, page!.TotalCount);
        Assert.Equal("Nabywca sp. z o.o.", page.Items.Single().BuyerName);
    }

    [Fact]
    public async Task Create_WithSellerFieldsInBody_IgnoresThem_AndIssueStoresProfileSnapshot()
    {
        var tenantId = await CreateTenantAsync();
        var nip = await WithDbAsync(db => db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Nip).SingleAsync());

        var response = await _client.PostAsJsonAsync(Base(tenantId), new
        {
            seller = new { nip = "1111111111", name = "Podstawiony sprzedawca", bankAccount = "00000000000000000000000000" },
            sellerNip = "1111111111",
            sellerName = "Podstawiony sprzedawca",
            buyer = new { kind = "Company", nip = BuyerNip, name = "Nabywca", addressLine1 = "ul. Krótka 2" },
            issueDate = "2026-11-03",
            lines = new[] { new { name = "Usługa", unit = "szt.", quantity = 1, unitNetPrice = 100.00m, vatRate = "23" } }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draft = (await response.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
        Assert.Equal(nip, draft.Seller.Nip);
        Assert.Equal("OpenKSeF Test A sp. z o.o.", draft.Seller.Name);

        await IssueAsync(tenantId, draft.Id);

        await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
            "Nowa nazwa", "ul. Nowa 9", null, "61109010140000071219812874", true, "FV/{nr}/{MM}/{RRRR}"));

        var stored = await LoadAsync(draft.Id);
        Assert.Equal(nip, stored.SellerNip);
        Assert.Equal("OpenKSeF Test A sp. z o.o.", stored.SellerName);
        Assert.Equal("ul. Prosta 1", stored.SellerAddressLine1);
        Assert.Equal("61109010140000071219812874", stored.SellerBankAccount);
        Assert.False(stored.SellerCashAccounting);

        var reloaded = await _client.GetFromJsonAsync<IssuedInvoiceResponse>($"{Base(tenantId)}/{draft.Id}");
        Assert.Equal("OpenKSeF Test A sp. z o.o.", reloaded!.Seller.Name);
    }

    [Fact]
    public void RequestType_HasNoSellerFields()
    {
        var properties = typeof(IssuedInvoiceDraftRequest).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(properties, p => p.Contains("Seller", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Create_WithoutDates_GetsServerDefaults_AndSaleAndDueFollowIssueDate()
    {
        var tenantId = await CreateTenantAsync();
        var empty = await CreateDraftAsync(tenantId, new IssuedInvoiceDraftRequest(null, null, null, null, null, false, null, null));
        Assert.Equal((Today, Today, Today.AddDays(14)), (empty.IssueDate, empty.SaleDate, empty.DueDate));
        Assert.Equal("Transfer", empty.PaymentMethod);
        Assert.Equal("Company", empty.Buyer.Kind);
        Assert.Empty(empty.Lines);

        var withIssueDate = await CreateDraftAsync(tenantId, new IssuedInvoiceDraftRequest(null, new DateOnly(2026, 1, 15), null, null, null, false, null, null));
        Assert.Equal((new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 29)), (withIssueDate.SaleDate, withIssueDate.DueDate));
    }

    [Fact]
    public async Task Create_ComputesTotalsPerRate_AndWarnsWithoutBlocking()
    {
        var tenantId = await CreateTenantAsync();

        var draft = await CreateDraftAsync(tenantId, Draft(buyerNip: "1234567890", unitNetPrice: 20000m));

        Assert.Equal((20000m, 4600m, 24600m), (draft.TotalNet, draft.TotalVat, draft.TotalGross));
        Assert.Contains(draft.Warnings, w => w.Code == "INV-W-NIP");
        Assert.Contains(draft.Warnings, w => w.Code == "INV-W-MPP");
    }

    [Theory]
    [InlineData("""{"buyer":{"kind":"Alien"}}""", "INV-VAL-070", "buyer.kind")]
    [InlineData("""{"paymentMethod":"1"}""", "INV-VAL-071", "paymentMethod")]
    [InlineData("""{"exemptionBasis":{"kind":"Nope","text":"x"}}""", "INV-VAL-072", "exemptionBasis.kind")]
    [InlineData("""{"buyer":{"kind":"Company","nip":"12345678901"}}""", "INV-VAL-013", "buyer.nip")]
    [InlineData("""{"lines":[{"name":"a","quantity":1,"unitNetPrice":1,"vatRate":"19"}]}""", "INV-VAL-060", "lines[0].vatRate")]
    [InlineData("""{"lines":[{"name":"a","quantity":0.1234567,"unitNetPrice":1,"vatRate":"23"}]}""", "INV-VAL-053", "lines[0].quantity")]
    [InlineData("""{"lines":[{"name":"a","quantity":1,"unitNetPrice":1.001,"vatRate":"23"}]}""", "INV-VAL-054", "lines[0].unitNetPrice")]
    [InlineData("""{"lines":[{"name":"a","quantity":9999999999,"unitNetPrice":9999999999,"vatRate":"23"}]}""", "INV-VAL-054", "lines[0].unitNetPrice")]
    public async Task Create_MalformedDraft_Returns400Envelope(string json, string code, string field)
    {
        var tenantId = await CreateTenantAsync();

        var response = await _client.PostAsync(Base(tenantId), new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        var envelope = await ReadEnvelopeAsync(response, HttpStatusCode.BadRequest);
        Assert.Contains(envelope.Errors, e => e.Code == code && e.Field == field);
    }

    [Fact]
    public async Task Create_TooLongBuyerName_Returns400Envelope()
    {
        var tenantId = await CreateTenantAsync();

        var envelope = await ReadEnvelopeAsync(
            await _client.PostAsJsonAsync(Base(tenantId), Draft(buyerName: new string('x', 513))),
            HttpStatusCode.BadRequest);

        Assert.Contains(envelope.Errors, e => e.Code == "INV-VAL-073" && e.Field == "buyer.name");
    }

    [Fact]
    public async Task Issue_ValidDraft_Returns202Sending_StoresXmlAndHash_SecondCallChangesNothing()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);

        var issued = await IssueAsync(tenantId, draft.Id);

        Assert.Equal("Sending", issued.Status);
        Assert.Equal("FV/1/11/2026", issued.Number);
        Assert.NotNull(issued.IssuedAt);

        var stored = await LoadAsync(draft.Id);
        Assert.Equal(IssuedInvoiceStatus.Queued, stored.Status);
        Assert.NotEmpty(stored.SentXml!);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(stored.SentXml!)), stored.SentXmlSha256);
        Assert.Equal("test", stored.KsefEnvironment);
        Assert.Contains("<P_2>FV/1/11/2026</P_2>", System.Text.Encoding.UTF8.GetString(stored.SentXml!));

        var second = await IssueAsync(tenantId, draft.Id, HttpStatusCode.OK);

        Assert.Equal("Sending", second.Status);
        Assert.Equal(issued.Number, second.Number);
        var after = await LoadAsync(draft.Id);
        Assert.Equal(stored.UpdatedAt, after.UpdatedAt);
        Assert.Equal(stored.SentXml, after.SentXml);
        Assert.Equal(2, await WithDbAsync(db => db.InvoiceNumberCounters.Where(c => c.TenantId == tenantId).Select(c => c.Next).SingleAsync()));
    }

    [Fact]
    public async Task Issue_AcceptedInvoice_Returns200CurrentState()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);
        await IssueAsync(tenantId, draft.Id);
        await MutateAsync(draft.Id, MarkAccepted);

        var current = await IssueAsync(tenantId, draft.Id, HttpStatusCode.OK);

        Assert.Equal("Accepted", current.Status);
    }

    [Fact]
    public async Task Issue_HardCheckFailures_Return422Envelope_AndChangeNothing()
    {
        var tenantId = await CreateTenantAsync(withProfile: false);
        var draft = await CreateDraftAsync(tenantId, new IssuedInvoiceDraftRequest(
            new IssuedInvoiceBuyerModel("Company", "1234567890", null, null, null), null, null, null, "Transfer", false, null, null));

        var envelope = await ReadEnvelopeAsync(await _client.PostAsync($"{Base(tenantId)}/{draft.Id}/issue", null), HttpStatusCode.UnprocessableEntity);

        foreach (var code in new[] { "INV-VAL-002", "INV-VAL-010", "INV-VAL-011", "INV-VAL-012", "INV-VAL-013", "INV-VAL-015", "INV-VAL-016" })
            Assert.Contains(envelope.Errors, e => e.Code == code);
        Assert.Contains(envelope.Errors, e => e.Message == "Dla nabywcy firmy wymagany jest poprawny NIP");

        var stored = await LoadAsync(draft.Id);
        Assert.Equal(IssuedInvoiceStatus.Draft, stored.Status);
        Assert.Null(stored.Number);
        Assert.False(await WithDbAsync(db => db.InvoiceNumberCounters.AnyAsync(c => c.TenantId == tenantId)));
    }

    [Fact]
    public async Task Issue_XsdFailureFromRealWriter_Returns422_AndRollsBackNumber()
    {
        var tenantId = await CreateTenantAsync(nip: $"012{Random.Shared.Next(1_000_000, 9_999_999)}");
        var draft = await CreateDraftAsync(tenantId);

        var envelope = await ReadEnvelopeAsync(await _client.PostAsync($"{Base(tenantId)}/{draft.Id}/issue", null), HttpStatusCode.UnprocessableEntity);

        Assert.Contains(envelope.Errors, e => e.Code == "INV-XSD" && e.Field == null);
        var stored = await LoadAsync(draft.Id);
        Assert.Equal(IssuedInvoiceStatus.Draft, stored.Status);
        Assert.Null(stored.Number);
        Assert.Null(stored.SentXml);
        Assert.False(await WithDbAsync(db => db.InvoiceNumberCounters.AnyAsync(c => c.TenantId == tenantId)));
    }

    [Theory]
    [InlineData("sending")]
    [InlineData("accepted")]
    public async Task UpdateAndDelete_OutsideDraftAndRejected_Return409_AndChangeNothing(string state)
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);
        await IssueAsync(tenantId, draft.Id);
        if (state == "sending")
            await MutateAsync(draft.Id, MarkSending);
        else
            await MutateAsync(draft.Id, MarkAccepted);

        var update = await _client.PutAsJsonAsync($"{Base(tenantId)}/{draft.Id}", Draft(buyerName: "Zmieniony"));
        var delete = await _client.DeleteAsync($"{Base(tenantId)}/{draft.Id}");

        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal("Nabywca sp. z o.o.", (await LoadAsync(draft.Id)).BuyerName);
    }

    [Fact]
    public async Task UpdateAndDelete_QueuedInvoice_Return409()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);
        await IssueAsync(tenantId, draft.Id);

        Assert.Equal(HttpStatusCode.Conflict, (await _client.PutAsJsonAsync($"{Base(tenantId)}/{draft.Id}", Draft())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync($"{Base(tenantId)}/{draft.Id}")).StatusCode);
    }

    [Fact]
    public async Task Draft_CanBeUpdatedAndDeleted()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);

        var update = await _client.PutAsJsonAsync($"{Base(tenantId)}/{draft.Id}", Draft(buyerName: "Zmieniony", unitNetPrice: 50m, vatRate: "8"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
        Assert.Equal("Zmieniony", updated.Buyer.Name);
        Assert.Equal((50m, 4m), (updated.TotalNet, updated.TotalVat));
        Assert.Single(updated.Lines);
        Assert.Single((await LoadAsync(draft.Id)).Lines);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{Base(tenantId)}/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{Base(tenantId)}/{draft.Id}")).StatusCode);
    }

    [Fact]
    public async Task Rejected_CanBeUpdated_NotDeleted_AndReissueKeepsNumber()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);
        var issued = await IssueAsync(tenantId, draft.Id);
        await MutateAsync(draft.Id, MarkRejected);

        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync($"{Base(tenantId)}/{draft.Id}")).StatusCode);
        var update = await _client.PutAsJsonAsync($"{Base(tenantId)}/{draft.Id}", Draft(buyerName: "Poprawiony"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Rejected", (await update.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!.Status);

        var reissued = await IssueAsync(tenantId, draft.Id);

        Assert.Equal("Sending", reissued.Status);
        Assert.Equal(issued.Number, reissued.Number);
        Assert.Null(reissued.Ksef.StatusCode);
        var stored = await LoadAsync(draft.Id);
        Assert.Null(stored.KsefElementRef);
        Assert.Null(stored.SendStartedAt);
        Assert.Contains("Poprawiony", System.Text.Encoding.UTF8.GetString(stored.SentXml!));
        Assert.Equal(2, await WithDbAsync(db => db.InvoiceNumberCounters.Where(c => c.TenantId == tenantId).Select(c => c.Next).SingleAsync()));
    }

    [Fact]
    public async Task Rejected_IssueDateWithinNumberPeriod_SavedAndNumberStays()
    {
        var tenantId = await CreateTenantAsync();
        var id = await SeedRejectedAsync(tenantId);

        var update = await _client.PutAsJsonAsync($"{Base(tenantId)}/{id}", Draft(issueDate: "2026-11-02"));

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var saved = (await update.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
        Assert.Equal(new DateOnly(2026, 11, 2), saved.IssueDate);
        Assert.Equal("FV/1/11/2026", saved.Number);
    }

    [Theory]
    [InlineData("2026-12-05")]
    [InlineData("2026-10-31")]
    public async Task Rejected_IssueDateOutsideNumberPeriod_Returns422_ChangesNothing_ConsumesNoNumber(string issueDate)
    {
        var tenantId = await CreateTenantAsync();
        var id = await SeedRejectedAsync(tenantId);

        await AssertPeriodBlockedAsync(tenantId, id, issueDate, "FV/1/11/2026");
    }

    [Fact]
    public async Task Rejected_YearlyPattern_IssueDateFreeWithinYear_NextYearReturns422()
    {
        var tenantId = await CreateTenantAsync();
        await SetPatternAsync(tenantId, "{nr}/{RRRR}");
        var id = await SeedRejectedAsync(tenantId);
        Assert.Equal("1/2026", (await LoadAsync(id)).Number);

        await AssertPeriodBlockedAsync(tenantId, id, "2027-01-02", "1/2026");

        var update = await _client.PutAsJsonAsync($"{Base(tenantId)}/{id}", Draft(issueDate: "2026-02-10"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
    }

    [Fact]
    public async Task Rejected_PatternChangedAfterRejection_NumberAsIssuedDecides()
    {
        var tenantId = await CreateTenantAsync();
        var id = await SeedRejectedAsync(tenantId);
        await SetPatternAsync(tenantId, "{nr}/{RRRR}");

        await AssertPeriodBlockedAsync(tenantId, id, "2026-12-05", "FV/1/11/2026");
    }

    [Fact]
    public async Task Rejected_NumberPeriodSurvivesReissueAfterPatternChange()
    {
        var tenantId = await CreateTenantAsync();
        var id = await SeedRejectedAsync(tenantId);
        await SetPatternAsync(tenantId, "{nr}/{RRRR}");
        await IssueAsync(tenantId, id);
        await MutateAsync(id, MarkRejected);

        await AssertPeriodBlockedAsync(tenantId, id, "2026-12-05", "FV/1/11/2026");
    }

    [Fact]
    public async Task Draft_IssueDateChangesFreely_NumberComesFromPeriodAtIssue()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);

        var update = await _client.PutAsJsonAsync($"{Base(tenantId)}/{draft.Id}", Draft(issueDate: "2025-03-15"));

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Null((await update.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!.Number);
        Assert.Equal("FV/1/03/2025", (await IssueAsync(tenantId, draft.Id)).Number);
    }

    [Fact]
    public async Task Rejected440_Issue_Returns422_NothingSent_ViewNamesKsefNumber_CopyAllowed()
    {
        const string original = "5213456789-20261103-ABCDEF123456-AB";
        var tenantId = await CreateTenantAsync();
        var id = await SeedRejectedAsync(tenantId, invoice =>
        {
            MarkSending(invoice);
            invoice.MarkRejected(440, IssuedInvoiceSendProcessor.NumberTakenMessage, $"Duplikat faktury; Numer KSeF: {original}", DateTime.UtcNow);
        });
        var before = await LoadAsync(id);

        var response = await _client.PostAsync($"{Base(tenantId)}/{id}/issue", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<ValidationEnvelope>())!.Errors.Single();
        Assert.Equal(("INV-VAL-023", "number"), (error.Code, error.Field));
        Assert.Contains(original, error.Message);

        var stored = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Rejected, stored.Status);
        Assert.Null(stored.NextAttemptAt);
        Assert.Equal(before.SentXml, stored.SentXml);
        Assert.Equal(before.IssuedAt, stored.IssuedAt);
        Assert.Equal(before.AttemptCount, stored.AttemptCount);

        var view = (await _client.GetFromJsonAsync<IssuedInvoiceResponse>($"{Base(tenantId)}/{id}"))!;
        Assert.Equal(original, view.Ksef.DuplicateKsefNumber);
        Assert.Null(view.Ksef.KsefNumber);

        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsync($"{Base(tenantId)}/{id}/copy", null)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentIssue_OfDifferentDrafts_GetsDifferentGaplessNumbers()
    {
        var tenantId = await CreateTenantAsync();
        var drafts = new List<IssuedInvoiceResponse>();
        for (var i = 0; i < 8; i++)
            drafts.Add(await CreateDraftAsync(tenantId));

        var responses = await Task.WhenAll(drafts.Select(d => _client.PostAsync($"{Base(tenantId)}/{d.Id}/issue", null)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        var numbers = await WithDbAsync(db => db.IssuedInvoices.Where(i => i.TenantId == tenantId).Select(i => i.Number!).ToListAsync());
        Assert.Equal(Enumerable.Range(1, 8).Select(n => $"FV/{n}/11/2026").Order(), numbers.Order());
    }

    [Fact]
    public async Task ConcurrentIssue_OfSameDraft_IssuesOnce_AndConsumesOneNumber()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _client.PostAsync($"{Base(tenantId)}/{draft.Id}/issue", null)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Accepted);
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal("FV/1/11/2026", (await LoadAsync(draft.Id)).Number);
        Assert.Equal(2, await WithDbAsync(db => db.InvoiceNumberCounters.Where(c => c.TenantId == tenantId).Select(c => c.Next).SingleAsync()));
    }

    [Fact]
    public async Task Issue_UsesNextNumberFromProfile_InPeriodOfIssueDate()
    {
        var tenantId = await CreateTenantAsync(nextNumber: 57, nextNumberPeriod: "2026-11");

        var november = await IssueAsync(tenantId, (await CreateDraftAsync(tenantId)).Id);
        var october = await IssueAsync(tenantId, (await CreateDraftAsync(tenantId, Draft(issueDate: "2026-10-31"))).Id);

        Assert.Equal("FV/57/11/2026", november.Number);
        Assert.Equal("FV/1/10/2026", october.Number);
    }

    [Theory]
    [InlineData("FV/{nr}", "FV/1")]
    [InlineData("FV/{nr}/{MM}", "FV/1/11")]
    public async Task StoredPatternWithoutYear_StaysReadable_CollidesNextYear_UntilPatternFixed(string pattern, string firstNumber)
    {
        const string legalName = "OpenKSeF Test A sp. z o.o.";
        const string nrb = "61109010140000071219812874";
        var tenantId = await CreateTenantAsync();
        await WithDbAsync(async db =>
        {
            var stored = await db.SellerProfiles.SingleAsync(p => p.TenantId == tenantId);
            stored.Update(legalName, "ul. Prosta 1", "00-001 Warszawa", nrb, false, pattern);
            return await db.SaveChangesAsync();
        });

        var read = await _client.GetFromJsonAsync<SellerProfileResponse>($"/api/tenants/{tenantId}/seller-profile");
        Assert.Equal(pattern, read!.NumberingPattern);

        var first = await IssueAsync(tenantId, (await CreateDraftAsync(tenantId, Draft(issueDate: "2025-11-03"))).Id);
        Assert.Equal(firstNumber, first.Number);

        var nextYear = await CreateDraftAsync(tenantId, Draft(issueDate: "2026-11-03"));
        var collision = await _client.PostAsync($"{Base(tenantId)}/{nextYear.Id}/issue", null);
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        Assert.Contains($"Numer {firstNumber} jest już użyty", (await collision.Content.ReadFromJsonAsync<JsonObject>())!["message"]!.GetValue<string>());
        Assert.Equal(IssuedInvoiceStatus.Draft, (await LoadAsync(nextYear.Id)).Status);

        var refused = await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
            legalName, "ul. Prosta 1", "00-001 Warszawa", nrb, false, pattern));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        (await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
            legalName, "ul. Prosta 1", "00-001 Warszawa", nrb, false, "FV/{nr}/{MM}/{RRRR}"))).EnsureSuccessStatusCode();
        Assert.Equal("FV/1/11/2026", (await IssueAsync(tenantId, nextYear.Id)).Number);
    }

    [Fact]
    public async Task Issue_FutureIssueDate_Returns422_ConsumesNoNumber_TodayIsAllowed()
    {
        var tenantId = await CreateTenantAsync();
        var future = Today.AddDays(1).ToString("yyyy-MM-dd");
        var draft = await CreateDraftAsync(tenantId, Draft(issueDate: future));

        var response = await _client.PostAsync($"{Base(tenantId)}/{draft.Id}/issue", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = Assert.Single((await response.Content.ReadFromJsonAsync<ValidationEnvelope>())!.Errors);
        Assert.Equal(("INV-VAL-020", "issueDate", "Data wystawienia nie może być późniejsza niż dzisiejsza"), (error.Code, error.Field, error.Message));
        var stored = await LoadAsync(draft.Id);
        Assert.Equal(IssuedInvoiceStatus.Draft, stored.Status);
        Assert.Null(stored.Number);
        Assert.False(await WithDbAsync(db => db.InvoiceNumberCounters.AnyAsync(c => c.TenantId == tenantId)));

        var update = await _client.PutAsJsonAsync($"{Base(tenantId)}/{draft.Id}", Draft(issueDate: Today.ToString("yyyy-MM-dd")));
        update.EnsureSuccessStatusCode();
        Assert.Equal("FV/1/12/2026", (await IssueAsync(tenantId, draft.Id)).Number);
    }

    [Fact]
    public async Task Issue_NumberAlreadyUsed_Returns409Friendly_AndStaysDraft()
    {
        var tenantId = await CreateTenantAsync();
        await IssueAsync(tenantId, (await CreateDraftAsync(tenantId)).Id);
        await WithDbAsync(async db =>
        {
            var counter = await db.InvoiceNumberCounters.SingleAsync(c => c.TenantId == tenantId);
            counter.Next = 1;
            return await db.SaveChangesAsync();
        });
        var second = await CreateDraftAsync(tenantId);

        var response = await _client.PostAsync($"{Base(tenantId)}/{second.Id}/issue", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("jest już użyty", (await response.Content.ReadFromJsonAsync<JsonObject>())!["message"]!.GetValue<string>());
        Assert.Equal(IssuedInvoiceStatus.Draft, (await LoadAsync(second.Id)).Status);
    }

    [Fact]
    public async Task List_PagesAndFiltersOnServer()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId, Draft(issueDate: "2026-12-01", buyerName: "Żółta Firma"));
        var queued = await CreateDraftAsync(tenantId, Draft(issueDate: "2026-11-03", buyerName: "Kolejka"));
        var sending = await CreateDraftAsync(tenantId, Draft(issueDate: "2026-11-04", buyerName: "Wysyłka", buyerNip: "7740001454"));
        var accepted = await CreateDraftAsync(tenantId, Draft(issueDate: "2026-10-05", buyerName: "Przyjęta"));
        var rejected = await CreateDraftAsync(tenantId, Draft(issueDate: "2026-10-06", buyerName: "Odrzucona"));
        foreach (var id in new[] { queued.Id, sending.Id, accepted.Id, rejected.Id })
            await IssueAsync(tenantId, id);
        await MutateAsync(sending.Id, MarkSending);
        await MutateAsync(accepted.Id, MarkAccepted);
        await MutateAsync(rejected.Id, MarkRejected);

        async Task<PagedResult<IssuedInvoiceListItemResponse>> GetAsync(string query) =>
            (await _client.GetFromJsonAsync<PagedResult<IssuedInvoiceListItemResponse>>($"{Base(tenantId)}?{query}"))!;

        var firstPage = await GetAsync("page=1&pageSize=2");
        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal(3, firstPage.TotalPages);
        Assert.Equal(new[] { draft.Id, sending.Id }, firstPage.Items.Select(i => i.Id));
        Assert.Equal(new[] { accepted.Id }, (await GetAsync("page=3&pageSize=2")).Items.Select(i => i.Id));

        Assert.Equal(new[] { draft.Id }, (await GetAsync("status=Draft")).Items.Select(i => i.Id));
        Assert.Equal(new[] { sending.Id, queued.Id }, (await GetAsync("status=Sending")).Items.Select(i => i.Id));
        Assert.Equal(new[] { accepted.Id }, (await GetAsync("status=Accepted")).Items.Select(i => i.Id));
        Assert.Equal(new[] { rejected.Id }, (await GetAsync("status=Rejected")).Items.Select(i => i.Id));

        Assert.Equal(new[] { sending.Id, queued.Id }, (await GetAsync("month=2026-11")).Items.Select(i => i.Id));
        Assert.Equal(new[] { draft.Id }, (await GetAsync("month=2026-12")).Items.Select(i => i.Id));

        Assert.Equal(new[] { draft.Id }, (await GetAsync($"q={Uri.EscapeDataString("żółta")}")).Items.Select(i => i.Id));
        Assert.Equal(new[] { sending.Id }, (await GetAsync("q=7740001454")).Items.Select(i => i.Id));
        var acceptedNumber = (await LoadAsync(accepted.Id)).Number!;
        Assert.Equal(new[] { accepted.Id }, (await GetAsync($"q={Uri.EscapeDataString(acceptedNumber.ToLowerInvariant())}")).Items.Select(i => i.Id));

        var item = (await GetAsync("status=Accepted")).Items.Single();
        Assert.Equal("Accepted", item.Status);
        Assert.Equal(new DateOnly(2026, 10, 19), item.DueDate);
        Assert.Equal((100m, 123m), (item.TotalNet, item.TotalGross));
        Assert.Equal("PLN", item.Currency);
        Assert.NotNull(item.KsefNumber);
        Assert.Equal("Sending", (await GetAsync("status=Sending")).Items.First().Status);
    }

    [Theory]
    [InlineData("status=Queued", "status")]
    [InlineData("month=2026-13", "month")]
    [InlineData("month=11-2026", "month")]
    public async Task List_InvalidFilter_Returns400Envelope(string query, string field)
    {
        var tenantId = await CreateTenantAsync();

        var envelope = await ReadEnvelopeAsync(await _client.GetAsync($"{Base(tenantId)}?{query}"), HttpStatusCode.BadRequest);

        Assert.Contains(envelope.Errors, e => e.Field == field);
    }

    [Fact]
    public async Task Copy_CreatesDraftWithSameBuyerAndLines_TodayDates_NoNumber_SourceUnchanged()
    {
        var tenantId = await CreateTenantAsync();
        var source = await CreateDraftAsync(tenantId, new IssuedInvoiceDraftRequest(
            new IssuedInvoiceBuyerModel("Company", BuyerNip, "Nabywca", "ul. Krótka 2", "00-002 Warszawa"),
            new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 4), new DateOnly(2026, 2, 5),
            "Cash", true, new ExemptionBasisModel("Act", "art. 113 ust. 1 ustawy o VAT"),
            [new("Usługa A", "godz.", 2.5m, 100m, "23"), new("Usługa B", "szt.", 1, 50m, "zw")]));
        await IssueAsync(tenantId, source.Id);
        await MutateAsync(source.Id, MarkAccepted);
        var before = await LoadAsync(source.Id);

        var response = await _client.PostAsync($"{Base(tenantId)}/{source.Id}/copy", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var copy = (await response.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!;
        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("Draft", copy.Status);
        Assert.Null(copy.Number);
        Assert.Null(copy.Ksef.KsefNumber);
        Assert.Equal((Today, Today, Today.AddDays(14)), (copy.IssueDate, copy.SaleDate, copy.DueDate));
        Assert.Equal(source.Buyer, copy.Buyer);
        Assert.Equal(("Cash", true), (copy.PaymentMethod, copy.SplitPayment));
        Assert.Equal(source.ExemptionBasis, copy.ExemptionBasis);
        Assert.Equal(
            source.Lines.Select(l => (l.LineNo, l.Name, l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate)),
            copy.Lines.Select(l => (l.LineNo, l.Name, l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate)));

        var after = await LoadAsync(source.Id);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Lines.Count, after.Lines.Count);
    }

    [Fact]
    public async Task List_EscalatedSending_NeedsAttention_CopyStill409_StatusUnchanged()
    {
        var tenantId = await CreateTenantAsync();
        var plain = await CreateDraftAsync(tenantId);
        var stuck = await CreateDraftAsync(tenantId);
        await IssueAsync(tenantId, stuck.Id);
        await MutateAsync(stuck.Id, invoice =>
        {
            MarkSending(invoice);
            invoice.MarkEscalated(DateTime.UtcNow);
        });

        var items = (await _client.GetFromJsonAsync<PagedResult<IssuedInvoiceListItemResponse>>(Base(tenantId)))!.Items;

        Assert.True(items.Single(i => i.Id == stuck.Id).NeedsAttention);
        Assert.Equal("Sending", items.Single(i => i.Id == stuck.Id).Status);
        Assert.False(items.Single(i => i.Id == plain.Id).NeedsAttention);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync($"{Base(tenantId)}/{stuck.Id}/copy", null)).StatusCode);
    }

    [Fact]
    public async Task Copy_WhileQueuedOrSending_Returns409_AndCreatesNothing_RejectedCopies()
    {
        var tenantId = await CreateTenantAsync();
        var source = await CreateDraftAsync(tenantId);
        await IssueAsync(tenantId, source.Id);

        var queued = await _client.PostAsync($"{Base(tenantId)}/{source.Id}/copy", null);
        Assert.Equal(HttpStatusCode.Conflict, queued.StatusCode);
        Assert.Contains("w trakcie wysyłki", (await queued.Content.ReadFromJsonAsync<JsonObject>())!["message"]!.GetValue<string>());

        await MutateAsync(source.Id, MarkSending);
        var sending = await _client.PostAsync($"{Base(tenantId)}/{source.Id}/copy", null);
        Assert.Equal(HttpStatusCode.Conflict, sending.StatusCode);
        Assert.Equal(1, await WithDbAsync(db => db.IssuedInvoices.CountAsync(i => i.TenantId == tenantId)));

        await MutateAsync(source.Id, invoice => invoice.MarkRejected(450, "Błąd", null, DateTime.UtcNow));
        var rejected = await _client.PostAsync($"{Base(tenantId)}/{source.Id}/copy", null);
        Assert.Equal(HttpStatusCode.Created, rejected.StatusCode);
    }

    [Fact]
    public async Task Json_MatchesContractFixtures()
    {
        var tenantId = await CreateTenantAsync();
        var created = await _client.PostAsJsonAsync(Base(tenantId), new IssuedInvoiceDraftRequest(
            new IssuedInvoiceBuyerModel("Company", BuyerNip, "Nabywca sp. z o.o.", "ul. Krótka 2", "00-002 Warszawa"),
            new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 17),
            "Transfer", false, null,
            [new("Usługa", "szt.", 1, 20000m, "23")]));
        var draftJson = JsonNode.Parse(await created.Content.ReadAsStringAsync())!;
        var id = draftJson["id"]!.GetValue<Guid>();
        AssertMatchesFixture("issued-invoice-draft.json", draftJson);

        var list = JsonNode.Parse(await _client.GetStringAsync($"{Base(tenantId)}?pageSize=10"))!;
        AssertMatchesFixture("issued-invoice-list.json", list);

        var invalid = await _client.PostAsJsonAsync(Base(tenantId), new IssuedInvoiceDraftRequest(
            null, null, null, null, null, false, null, null));
        var emptyId = JsonNode.Parse(await invalid.Content.ReadAsStringAsync())!["id"]!.GetValue<Guid>();
        var envelope = await _client.PostAsync($"{Base(tenantId)}/{emptyId}/issue", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, envelope.StatusCode);
        AssertMatchesFixture("validation-envelope.json", JsonNode.Parse(await envelope.Content.ReadAsStringAsync())!);

        await IssueAsync(tenantId, id);
        await MutateAsync(id, MarkAccepted);
        await MutateAsync(id, invoice => invoice.AttachUpo([1, 2, 3], DateTime.UtcNow));
        AssertMatchesFixture("issued-invoice-accepted.json", JsonNode.Parse(await _client.GetStringAsync($"{Base(tenantId)}/{id}"))!);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("queued")]
    [InlineData("sending")]
    [InlineData("rejected")]
    public async Task XmlAndUpo_NotAccepted_Return409(string state)
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);
        if (state != "draft")
            await IssueAsync(tenantId, draft.Id);
        if (state == "sending")
            await MutateAsync(draft.Id, MarkSending);
        if (state == "rejected")
            await MutateAsync(draft.Id, MarkRejected);

        var xml = await _client.GetAsync($"{Base(tenantId)}/{draft.Id}/xml");
        var upo = await _client.GetAsync($"{Base(tenantId)}/{draft.Id}/upo");

        Assert.Equal(HttpStatusCode.Conflict, xml.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, upo.StatusCode);
        Assert.NotNull((await xml.Content.ReadFromJsonAsync<JsonObject>())!["message"]);
    }

    [Fact]
    public async Task Accepted_XmlReturnsStoredBytes_UpoOnlyOnceStored()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);
        await IssueAsync(tenantId, draft.Id);
        await MutateAsync(draft.Id, MarkAccepted);
        var stored = await LoadAsync(draft.Id);

        var xml = await _client.GetAsync($"{Base(tenantId)}/{draft.Id}/xml");
        Assert.Equal(HttpStatusCode.OK, xml.StatusCode);
        Assert.Equal("application/xml", xml.Content.Headers.ContentType!.MediaType);
        Assert.Equal(stored.SentXml, await xml.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.Conflict, (await _client.GetAsync($"{Base(tenantId)}/{draft.Id}/upo")).StatusCode);
        Assert.False((await _client.GetFromJsonAsync<IssuedInvoiceResponse>($"{Base(tenantId)}/{draft.Id}"))!.Ksef.HasUpo);

        byte[] upoBytes = [60, 85, 80, 79, 47, 62];
        await MutateAsync(draft.Id, invoice => invoice.AttachUpo(upoBytes, DateTime.UtcNow));

        var upo = await _client.GetAsync($"{Base(tenantId)}/{draft.Id}/upo");
        Assert.Equal(HttpStatusCode.OK, upo.StatusCode);
        Assert.Equal("application/xml", upo.Content.Headers.ContentType!.MediaType);
        Assert.Equal(upoBytes, await upo.Content.ReadAsByteArrayAsync());
        Assert.True((await _client.GetFromJsonAsync<IssuedInvoiceResponse>($"{Base(tenantId)}/{draft.Id}"))!.Ksef.HasUpo);
    }

    [Fact]
    public async Task VerificationUrl_OnlyWhenAccepted_EqualsVerificationLinkServiceOutput()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);
        Assert.Null(draft.Ksef.VerificationUrl);

        var issued = await IssueAsync(tenantId, draft.Id);
        Assert.Null(issued.Ksef.VerificationUrl);

        await MutateAsync(draft.Id, MarkSending);
        Assert.Null((await _client.GetFromJsonAsync<IssuedInvoiceResponse>($"{Base(tenantId)}/{draft.Id}"))!.Ksef.VerificationUrl);

        await MutateAsync(draft.Id, invoice => invoice.MarkAccepted("5213456789-20261103-ABCDEF123456-AB", DateTime.UtcNow, 200, "Sukces", DateTime.UtcNow));
        var accepted = await _client.GetFromJsonAsync<IssuedInvoiceResponse>($"{Base(tenantId)}/{draft.Id}");

        var stored = await LoadAsync(draft.Id);
        using var scope = _factory.Services.CreateScope();
        var expected = scope.ServiceProvider.GetRequiredService<IVerificationLinkService>().BuildInvoiceVerificationUrl(
            stored.SellerNip!, new DateTime(2026, 11, 3), Convert.ToBase64String(SHA256.HashData(stored.SentXml!)));
        Assert.Equal(expected, accepted!.Ksef.VerificationUrl);
        Assert.StartsWith($"https://qr-test.ksef.mf.gov.pl/invoice/{stored.SellerNip}/03-11-2026/", accepted.Ksef.VerificationUrl);
    }

    [Fact]
    public async Task VerificationUrl_RejectedInvoice_IsNotReturned()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);
        await IssueAsync(tenantId, draft.Id);
        await MutateAsync(draft.Id, MarkRejected);

        var rejected = await _client.GetFromJsonAsync<IssuedInvoiceResponse>($"{Base(tenantId)}/{draft.Id}");

        Assert.Null(rejected!.Ksef.VerificationUrl);
    }

    [Theory]
    [InlineData("test")]
    [InlineData("demo")]
    [InlineData("production")]
    public async Task Response_CarriesEnvironmentTheInvoiceWasSentTo(string environment)
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);
        Assert.Equal("test", draft.Ksef.Environment);
        await IssueAsync(tenantId, draft.Id);
        await MutateAsync(draft.Id, invoice => invoice.AttachSentXml(invoice.SentXml!, invoice.SentXmlSha256!, environment, invoice.VerificationUrl));
        await MutateAsync(draft.Id, MarkAccepted);

        var accepted = await _client.GetFromJsonAsync<IssuedInvoiceResponse>($"{Base(tenantId)}/{draft.Id}");

        Assert.Equal(environment, accepted!.Ksef.Environment);
    }

    [Fact]
    public async Task Rejection_ReturnsKsefCodeMessageAndDetails()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);
        await IssueAsync(tenantId, draft.Id);
        await MutateAsync(draft.Id, MarkRejected);

        var rejected = await _client.GetFromJsonAsync<IssuedInvoiceResponse>($"{Base(tenantId)}/{draft.Id}");

        Assert.Equal("Rejected", rejected!.Status);
        Assert.Equal(450, rejected.Ksef.StatusCode);
        Assert.Equal("Błąd weryfikacji semantyki", rejected.Ksef.StatusMessage);
        Assert.Equal("P_2: niepoprawna wartość", rejected.Ksef.StatusDetails);
        Assert.Null(rejected.Ksef.KsefNumber);
        Assert.Null(rejected.Ksef.DuplicateKsefNumber);
    }

    [Fact]
    public async Task ReadOnlyToken_ReturnsRequirementMessage()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);
        await IssueAsync(tenantId, draft.Id);
        await MutateAsync(draft.Id, invoice => invoice.MarkRejected(403, IssuedInvoiceSendProcessor.NoWritePermissionMessage, "Forbidden", DateTime.UtcNow));

        var rejected = await _client.GetFromJsonAsync<IssuedInvoiceResponse>($"{Base(tenantId)}/{draft.Id}");

        Assert.Equal("Rejected", rejected!.Status);
        Assert.Equal(403, rejected.Ksef.StatusCode);
        Assert.Equal(
            "Token KSeF nie ma uprawnienia do wystawiania faktur. Wygeneruj token z uprawnieniem do zapisu i podmień go w ustawieniach firmy.",
            rejected.Ksef.StatusMessage);
    }

    [Fact]
    public async Task Sending_ShowsRetryableProblemInLastError()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);
        await IssueAsync(tenantId, draft.Id);
        await MutateAsync(draft.Id, invoice =>
        {
            MarkSending(invoice);
            invoice.ScheduleRetry(IssuedInvoiceSendProcessor.UnavailableMessage, DateTime.UtcNow.AddSeconds(10), DateTime.UtcNow);
        });

        var sending = await _client.GetFromJsonAsync<IssuedInvoiceResponse>($"{Base(tenantId)}/{draft.Id}");

        Assert.Equal("Sending", sending!.Status);
        Assert.Equal("KSeF nie odpowiada, ponowimy wysyłkę", sending.Ksef.LastError);
    }

    private static void AssertMatchesFixture(string fixture, JsonNode actual)
    {
        Normalize(actual);
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "IssuedInvoices", fixture)))!;
        Assert.True(JsonNode.DeepEquals(expected, actual), $"{fixture} differs.\nExpected:\n{expected}\nActual:\n{actual}");
    }

    private static void Normalize(JsonNode? node)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var child in array)
                    Normalize(child);
                break;
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (key is "id" or "createdAt" or "updatedAt" or "issuedAt" or "acceptedAt" && obj[key] is not null)
                        obj[key] = $"<{key}>";
                    else if (key == "seller")
                        obj[key]!["nip"] = "<nip>";
                    else if (key == "verificationUrl" && obj[key] is not null)
                        obj[key] = "<verificationUrl>";
                    else
                        Normalize(obj[key]);
                }
                break;
        }
    }

    [Fact]
    public async Task DeleteTenant_OnlyDrafts_Returns204_AndRemovesTheirData()
    {
        var tenantId = await CreateTenantAsync();
        await CreateDraftAsync(tenantId);

        await AssertTenantDeletedAsync(tenantId);
    }

    [Theory]
    [InlineData("test")]
    [InlineData("demo")]
    public async Task DeleteTenant_AcceptedAndRejectedOnTestOrDemo_Returns204(string environment)
    {
        var tenantId = await CreateTenantAsync();
        await SeedSentAsync(tenantId, environment, MarkAccepted);
        await SeedSentAsync(tenantId, environment, MarkRejected);
        await CreateDraftAsync(tenantId);

        await AssertTenantDeletedAsync(tenantId);
    }

    [Fact]
    public async Task DeleteTenant_RejectedOnProduction_Returns204()
    {
        var tenantId = await CreateTenantAsync();
        await SeedSentAsync(tenantId, "production", MarkRejected);

        await AssertTenantDeletedAsync(tenantId);
    }

    [Fact]
    public async Task DeleteTenant_AcceptedOnProduction_Returns409_AndChangesNothing()
    {
        var tenantId = await CreateTenantAsync();
        await SeedSentAsync(tenantId, "production", MarkAccepted);
        await SeedSentAsync(tenantId, "test", MarkRejected);
        await CreateDraftAsync(tenantId);

        var message = await AssertTenantDeleteConflictAsync(tenantId, 3);

        Assert.Contains("pozostają w KSeF", message);
        Assert.Contains("operatorem", message);
    }

    [Fact]
    public async Task DeleteTenant_AcceptedWithoutEnvironment_Returns409()
    {
        var tenantId = await CreateTenantAsync();
        var id = await SeedSentAsync(tenantId, "test", MarkAccepted);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"""UPDATE "IssuedInvoices" SET "KsefEnvironment" = NULL WHERE "Id" = {id}"""));

        await AssertTenantDeleteConflictAsync(tenantId, 1);
    }

    [Theory]
    [InlineData("test", false)]
    [InlineData("test", true)]
    [InlineData("demo", false)]
    [InlineData("production", true)]
    public async Task DeleteTenant_QueuedOrSending_Returns409_AndChangesNothing(string environment, bool sending)
    {
        var tenantId = await CreateTenantAsync();
        var id = await SeedSentAsync(tenantId, environment, sending ? MarkSending : _ => { });

        Assert.Contains("wysyłki", await AssertTenantDeleteConflictAsync(tenantId, 1));
        Assert.Equal(sending ? IssuedInvoiceStatus.Sending : IssuedInvoiceStatus.Queued, (await LoadAsync(id)).Status);
    }

    [Fact]
    public async Task DeleteTenant_OtherUsersTenant_Returns404_AndChangesNothing()
    {
        await CreateTenantAsync(withProfile: false);
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await WithDbAsync(db =>
        {
            db.Tenants.Add(new Tenant { Id = tenantId, UserId = "other-user", Nip = $"522{Random.Shared.Next(1_000_000, 9_999_999)}", CreatedAt = now, UpdatedAt = now });
            db.IssuedInvoices.Add(IssuedInvoice.Draft(Guid.NewGuid(), tenantId, Today, now));
            return db.SaveChangesAsync();
        });

        var response = await _client.DeleteAsync($"/api/tenants/{tenantId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(await WithDbAsync(db => db.Tenants.AnyAsync(t => t.Id == tenantId)));
        Assert.Equal(1, await WithDbAsync(db => db.IssuedInvoices.CountAsync(i => i.TenantId == tenantId)));
    }

    [Fact]
    public async Task DeleteTenant_LastTenantWithInvoices_ReleasesEnvironmentLock()
    {
        await WithDbAsync(async db =>
        {
            await db.IssuedInvoices.ExecuteDeleteAsync();
            return await db.InvoiceHeaders.ExecuteDeleteAsync();
        });
        var first = await CreateTenantAsync();
        var last = await CreateTenantAsync();
        await SeedSentAsync(first, "test", MarkAccepted);
        await SeedSentAsync(last, "test", MarkRejected);
        Assert.True(await EnvironmentLockedByInvoicesAsync());

        await AssertTenantDeletedAsync(first);
        Assert.True(await EnvironmentLockedByInvoicesAsync());

        await AssertTenantDeletedAsync(last);
        Assert.False(await EnvironmentLockedByInvoicesAsync());
    }

    [Theory]
    [InlineData(false, "test", "Queued", HttpStatusCode.Conflict)]
    [InlineData(true, "production", "Accepted", HttpStatusCode.Conflict)]
    [InlineData(true, "production", "Rejected", HttpStatusCode.NoContent)]
    public async Task DeleteTenant_InvoiceFlippingStatus_WaitsAndUsesCommittedStatus(bool sent, string environment, string newStatus, HttpStatusCode expected)
    {
        var tenantId = await CreateTenantAsync();
        var id = sent ? await SeedSentAsync(tenantId, environment, MarkSending) : (await CreateDraftAsync(tenantId)).Id;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"""UPDATE "IssuedInvoices" SET "Status" = {newStatus} WHERE "Id" = {id}""");

        var delete = _client.DeleteAsync($"/api/tenants/{tenantId}");
        await WaitForLockWaitAsync("FOR UPDATE");
        Assert.False(delete.IsCompleted);
        await transaction.CommitAsync();

        Assert.Equal(expected, (await delete).StatusCode);
        Assert.Equal(expected == HttpStatusCode.Conflict ? 1 : 0, await WithDbAsync(d => d.IssuedInvoices.CountAsync(i => i.Id == id)));
    }

    [Fact]
    public async Task Issue_WhileTenantIsBeingDeleted_WaitsAndReturns404()
    {
        var tenantId = await CreateTenantAsync();
        var draft = await CreateDraftAsync(tenantId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"""SELECT 1 FROM "Tenants" WHERE "Id" = {tenantId} FOR UPDATE""");

        var issue = _client.PostAsync($"{Base(tenantId)}/{draft.Id}/issue", null);
        await WaitForLockWaitAsync("FOR KEY SHARE");
        await db.Database.ExecuteSqlAsync($"""DELETE FROM "IssuedInvoices" WHERE "TenantId" = {tenantId}""");
        await db.Database.ExecuteSqlAsync($"""DELETE FROM "Tenants" WHERE "Id" = {tenantId}""");
        await transaction.CommitAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await issue).StatusCode);
        Assert.Equal(0, await WithDbAsync(d => d.InvoiceNumberCounters.CountAsync(c => c.TenantId == tenantId)));
    }

    [Fact]
    public async Task DeleteTenant_RacingIssue_NeverLosesQueuedInvoice()
    {
        for (var round = 0; round < 12; round++)
        {
            var tenantId = await CreateTenantAsync();
            var draft = await CreateDraftAsync(tenantId);

            var issue = _client.PostAsync($"{Base(tenantId)}/{draft.Id}/issue", null);
            await Task.Delay(round % 4 * 5);
            var delete = _client.DeleteAsync($"/api/tenants/{tenantId}");
            await Task.WhenAll(issue, delete);

            if ((await delete).StatusCode == HttpStatusCode.NoContent)
            {
                Assert.Equal(HttpStatusCode.NotFound, (await issue).StatusCode);
                Assert.False(await WithDbAsync(db => db.IssuedInvoices.AnyAsync(i => i.Id == draft.Id)));
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, (await delete).StatusCode);
                Assert.Equal(HttpStatusCode.Accepted, (await issue).StatusCode);
                Assert.Equal(IssuedInvoiceStatus.Queued, (await LoadAsync(draft.Id)).Status);
            }
        }
    }

    private async Task<Guid> SeedSentAsync(Guid tenantId, string environment, Action<IssuedInvoice> change)
    {
        var draft = await CreateDraftAsync(tenantId);
        await IssueAsync(tenantId, draft.Id);
        await MutateAsync(draft.Id, invoice =>
        {
            invoice.AttachSentXml(invoice.SentXml!, invoice.SentXmlSha256!, environment, null);
            change(invoice);
        });
        return draft.Id;
    }

    private async Task AssertTenantDeletedAsync(Guid tenantId)
    {
        var ids = await WithDbAsync(db => db.IssuedInvoices.Where(i => i.TenantId == tenantId).Select(i => i.Id).ToListAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/tenants/{tenantId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/tenants/{tenantId}")).StatusCode);
        Assert.Equal(0, await WithDbAsync(db => db.IssuedInvoices.CountAsync(i => ids.Contains(i.Id))));
        Assert.Equal(0, await WithDbAsync(db => db.IssuedInvoiceLines.CountAsync(l => ids.Contains(l.IssuedInvoiceId))));
        Assert.Equal(0, await WithDbAsync(db => db.SellerProfiles.CountAsync(p => p.TenantId == tenantId)));
        Assert.Equal(0, await WithDbAsync(db => db.InvoiceNumberCounters.CountAsync(c => c.TenantId == tenantId)));
    }

    private async Task<string> AssertTenantDeleteConflictAsync(Guid tenantId, int invoiceCount)
    {
        var response = await _client.DeleteAsync($"/api/tenants/{tenantId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/tenants/{tenantId}")).StatusCode);
        Assert.Equal(invoiceCount, await WithDbAsync(db => db.IssuedInvoices.CountAsync(i => i.TenantId == tenantId)));
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["message"]!.GetValue<string>();
    }

    private async Task<bool> EnvironmentLockedByInvoicesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<ISystemSettingsService>().GetKSeFEnvironmentLockStatusAsync()).HasInvoices;
    }

    private async Task WaitForLockWaitAsync(string queryFragment)
    {
        var pattern = $"%{queryFragment}%";
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var waiting = await WithDbAsync(db => db.Database
                .SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query LIKE {pattern}""")
                .SingleAsync());
            if (waiting > 0)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"No query waiting on a lock matched {queryFragment}");
    }

    private sealed record TenantIdDto(Guid Id);

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 12, 20, 12, 0, 0, TimeSpan.Zero);
    }
}
