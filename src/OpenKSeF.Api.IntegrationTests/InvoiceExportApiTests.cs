using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public sealed class InvoiceExportApiTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
    private const string IndexHeader = "kierunek;numer_ksef;numer_faktury;rodzaj;data_wystawienia;data_przyjecia_ksef;nip_sprzedawcy;nazwa_sprzedawcy;id_nabywcy;nazwa_nabywcy;waluta;netto;vat;brutto;termin_platnosci;oplacona;plik_xml;plik_upo";
    private const string October = "from=2026-10-01&to=2026-10-31";

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _bare;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private HttpClient _owner = null!;

    public InvoiceExportApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _bare = new OpenKSeFWebApplicationFactory(fixture, new FixedTime());
        _factory = _bare.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(_notifications);
            services.AddSingleton(_email);
        }));
    }

    public async Task InitializeAsync()
    {
        _owner = _factory.CreateClient();
        _owner.SetBearerToken(await _fixture.GetAccessTokenAsync());
    }

    public Task DisposeAsync()
    {
        _owner.Dispose();
        _factory.Dispose();
        return _bare.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task Owner_ExportsOctoberSales_AcceptedOnlyWithUpo_BytesEqualXmlEndpoint_OneAuditEvent()
    {
        var (tenantId, nip) = await CreateTenantAsync();
        var accepted = new List<(Guid Id, string Number)>();
        for (var day = 1; day <= 3; day++)
            accepted.Add(await SeedIssuedAsync(tenantId, nip, new DateOnly(2026, 10, day), IssuedInvoiceStatus.Accepted, upo: true));
        await SeedIssuedAsync(tenantId, nip, new DateOnly(2026, 10, 4), IssuedInvoiceStatus.Draft);
        await SeedIssuedAsync(tenantId, nip, new DateOnly(2026, 10, 5), IssuedInvoiceStatus.Rejected);
        await SeedIssuedAsync(tenantId, nip, new DateOnly(2026, 11, 2), IssuedInvoiceStatus.Accepted, upo: true);

        var (response, zip) = await ExportAsync(tenantId, $"{October}&purchases=false");

        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"OpenKSeF_{nip}_2026-10-01_2026-10-31_TEST.zip", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(
            new[] { "README.txt", "indeks.csv" }.Concat(accepted.SelectMany(a => new[] { $"sprzedaz/{a.Number}.xml", $"sprzedaz/{a.Number}_UPO.xml" })).Order(),
            zip.Entries.Select(e => e.FullName).Order());
        foreach (var (id, number) in accepted)
        {
            var endpoint = await _owner.GetByteArrayAsync($"/api/tenants/{tenantId}/issued-invoices/{id}/xml");
            Assert.Equal(endpoint, Bytes(zip, $"sprzedaz/{number}.xml"));
            Assert.Equal(await _owner.GetByteArrayAsync($"/api/tenants/{tenantId}/issued-invoices/{id}/upo"), Bytes(zip, $"sprzedaz/{number}_UPO.xml"));
        }

        var rows = Index(zip);
        Assert.Equal(3, rows.Count);
        Assert.Equal(accepted.Select(a => a.Number), rows.Select(r => r[1]));
        Assert.Equal(
            ["sprzedaz", accepted[0].Number, "FV/1", "VAT", "2026-10-01", "2026-11-05", nip, "OpenKSeF Test A sp. z o.o.", "1111111111", "Nabywca Testowy", "PLN",
                "100,00", "23,00", "123,00", "2026-10-15", "", $"sprzedaz/{accepted[0].Number}.xml", $"sprzedaz/{accepted[0].Number}_UPO.xml"],
            rows[0]);

        var readme = Encoding.UTF8.GetString(Bytes(zip, "README.txt"));
        Assert.Contains("testowe (TEST)", readme);
        Assert.Contains("art. 112aa", readme);
        Assert.Contains("art. 106na", readme);
        Assert.Contains("Aplikacji Podatnika", readme);

        var audit = await WithDbAsync(db => db.AuditEvents.Where(a => a.TenantId == tenantId && a.Action == AuditEvent.InvoicesExported).ToListAsync());
        var single = Assert.Single(audit);
        Assert.Equal("od=2026-10-01;do=2026-10-31;kierunki=sprzedaz;sprzedaz=3;zakup=0", single.Target);
        Assert.NotNull(single.ActorUserId);
        Assert.NotNull(single.ActorEmail);
    }

    [Fact]
    public async Task IssuedInOpenKSeFAndSynced_OneFileAndRow_SentXmlWins_ForeignIssued_HasXmlNoUpo()
    {
        var (tenantId, nip) = await CreateTenantAsync();
        var (_, both) = await SeedIssuedAsync(tenantId, nip, new DateOnly(2026, 10, 10), IssuedInvoiceStatus.Accepted, upo: true);
        await SeedHeaderAsync(tenantId, InvoiceDirection.Issued, both, new DateOnly(2026, 10, 10), "<Faktura>synced copy</Faktura>"u8.ToArray());
        var other = KsefNumber(nip, new DateOnly(2026, 10, 12));
        var otherXml = "<Faktura>inne narzędzie</Faktura>"u8.ToArray();
        await SeedHeaderAsync(tenantId, InvoiceDirection.Issued, other, new DateOnly(2026, 10, 12), otherXml);
        var sentXml = await WithDbAsync(db => db.IssuedInvoices.Where(i => i.KsefNumber == both).Select(i => i.SentXml!).SingleAsync());

        var (_, zip) = await ExportAsync(tenantId, October);

        Assert.Equal(sentXml, Bytes(zip, $"sprzedaz/{both}.xml"));
        Assert.Equal(otherXml, Bytes(zip, $"sprzedaz/{other}.xml"));
        Assert.Equal(1, zip.Entries.Count(e => e.FullName == $"sprzedaz/{both}.xml"));
        var rows = Index(zip);
        Assert.Equal([both, other], rows.Select(r => r[1]));
        Assert.Equal($"sprzedaz/{other}.xml", rows[1][16]);
        Assert.Equal("", rows[1][17]);
    }

    [Fact]
    public async Task PurchasesOnly_PaidState_MissingXmlRow_CsvFormatting_AndNothingChanges()
    {
        var (tenantId, nip) = await CreateTenantAsync();
        await SeedIssuedAsync(tenantId, nip, new DateOnly(2026, 10, 3), IssuedInvoiceStatus.Accepted, upo: true);
        var paid = KsefNumber("1111111111", new DateOnly(2026, 10, 6));
        var paidXml = "<Faktura>zakup</Faktura>"u8.ToArray();
        await SeedHeaderAsync(tenantId, InvoiceDirection.Received, paid, new DateOnly(2026, 10, 6), paidXml, isPaid: true,
            buyerName: "=HYPERLINK(\"x\")", gross: 1234.5m, invoiceType: "Kor");
        var noXml = KsefNumber("1111111111", new DateOnly(2026, 10, 7));
        await SeedHeaderAsync(tenantId, InvoiceDirection.Received, noXml, new DateOnly(2026, 10, 7), null, vendorName: "Zażółć; \"gęślą\" jaźń");
        var before = await PaidStateAsync(tenantId);
        _notifications.ClearReceivedCalls();
        _email.ClearReceivedCalls();

        var (_, zip) = await ExportAsync(tenantId, $"{October}&sales=false");

        Assert.Equal(["README.txt", $"zakup/{paid}.xml", "indeks.csv"], zip.Entries.Select(e => e.FullName));
        Assert.Equal(paidXml, Bytes(zip, $"zakup/{paid}.xml"));
        var raw = Encoding.UTF8.GetString(Bytes(zip, "indeks.csv"));
        Assert.Contains(";\"'=HYPERLINK(\"\"x\"\")\";", raw);
        Assert.Contains(";\"Zażółć; \"\"gęślą\"\" jaźń\";", raw);
        var rows = Index(zip);
        Assert.Equal(["zakup", paid, "FV/ZAKUP", "KOR", "2026-10-06", "2026-10-06", "1111111111", "Dostawca Testowy", nip, "'=HYPERLINK(\"x\")", "PLN",
            "1000,00", "234,50", "1234,50", "2026-10-20", "tak", $"zakup/{paid}.xml", ""], rows[0]);
        Assert.Equal("nie", rows[1][15]);
        Assert.Equal("", rows[1][16]);

        Assert.Equal(before, await PaidStateAsync(tenantId));
        Assert.Empty(_notifications.ReceivedCalls());
        Assert.Empty(_email.ReceivedCalls());
    }

    [Fact]
    public async Task EmptyPeriod_ReadmeAndHeaderOnlyIndex()
    {
        var (tenantId, nip) = await CreateTenantAsync();
        await SeedIssuedAsync(tenantId, nip, new DateOnly(2026, 10, 3), IssuedInvoiceStatus.Accepted);

        var (_, zip) = await ExportAsync(tenantId, "from=2020-01-01&to=2020-01-31");

        Assert.Equal(["README.txt", "indeks.csv"], zip.Entries.Select(e => e.FullName));
        Assert.Equal([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(IndexHeader + "\r\n")], Bytes(zip, "indeks.csv"));
    }

    [Fact]
    public async Task AllPeriods_ExportsAcceptedKor_WithRodzajKor()
    {
        var (tenantId, nip) = await CreateTenantAsync();
        var (originalId, original) = await SeedIssuedAsync(tenantId, nip, new DateOnly(2026, 10, 3), IssuedInvoiceStatus.Accepted);
        var created = await _owner.PostAsJsonAsync($"/api/tenants/{tenantId}/issued-invoices/{originalId}/corrections", new CorrectionCreateRequest("WrongBuyerId"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var korId = (await created.Content.ReadFromJsonAsync<IssuedInvoiceResponse>())!.Id;
        (await _owner.PutAsJsonAsync($"/api/tenants/{tenantId}/issued-invoices/{korId}/correction", new CorrectionDraftRequest())).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Accepted, (await _owner.PostAsync($"/api/tenants/{tenantId}/issued-invoices/{korId}/issue?invoiceType=KOR&confirmPastIssueDate=true", null)).StatusCode);
        var kor = KsefNumber(nip, new DateOnly(2026, 11, 5));
        await WithDbAsync(async db =>
        {
            var invoice = await db.IssuedInvoices.SingleAsync(i => i.Id == korId);
            invoice.MarkSendStarted("session-kor", Now);
            invoice.MarkAccepted(kor, Now, 200, "Sukces", Now);
            return await db.SaveChangesAsync();
        });

        var (response, zip) = await ExportAsync(tenantId, "purchases=false");

        Assert.Equal($"OpenKSeF_{nip}_wszystko_TEST.zip", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(await _owner.GetByteArrayAsync($"/api/tenants/{tenantId}/issued-invoices/{korId}/xml"), Bytes(zip, $"sprzedaz/{kor}.xml"));
        var rows = Index(zip);
        Assert.Equal([original, kor], rows.Select(r => r[1]));
        Assert.Equal("KOR", rows[1][3]);
        Assert.Contains("wszystkie faktury", Encoding.UTF8.GetString(Bytes(zip, "README.txt")));
    }

    [Theory]
    [InlineData("from=2026-10-01")]
    [InlineData("from=2026-10-31&to=2026-10-01")]
    [InlineData("sales=false&purchases=false")]
    public async Task InvalidQuery_Returns400_AndNoAudit(string query)
    {
        var (tenantId, _) = await CreateTenantAsync();

        var response = await _owner.GetAsync($"/api/tenants/{tenantId}/invoices/export?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull((await response.Content.ReadFromJsonAsync<JsonObject>())!["error"]);
        Assert.False(await WithDbAsync(db => db.AuditEvents.AnyAsync(a => a.TenantId == tenantId && a.Action == AuditEvent.InvoicesExported)));
    }

    [Fact]
    public async Task FiveThousandInvoices_FirstBytesUnder5s_LiveMemoryGrowthUnder100MB_Complete()
    {
        const int count = 5000;
        const long probeEvery = 8 * 1024 * 1024;
        var (tenantId, _) = await CreateTenantAsync();
        for (var chunk = 0; chunk < count / 500; chunk++)
        {
            await WithDbAsync(db =>
            {
                for (var i = 0; i < 500; i++)
                {
                    var header = Header(tenantId, InvoiceDirection.Received, $"1111111111-20261015-{chunk * 500 + i:D12}-00", new DateOnly(2026, 10, 15));
                    var xml = Encoding.ASCII.GetBytes($"<Faktura>{Convert.ToBase64String(RandomNumberGenerator.GetBytes(22_000))}</Faktura>");
                    db.InvoiceHeaders.Add(header);
                    db.InvoiceXmls.Add(new InvoiceXml { InvoiceHeaderId = header.Id, Content = xml, Sha256 = "x", StoredAt = Now });
                }

                return db.SaveChangesAsync();
            });
        }

        var file = Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}.zip");
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var baseline = GC.GetTotalMemory(true);
            long peak = 0, read = 0, nextProbe = probeEvery;
            var clock = Stopwatch.StartNew();
            TimeSpan? firstBytes = null;
            using (var response = await _owner.GetAsync($"/api/tenants/{tenantId}/invoices/export?{October}", HttpCompletionOption.ResponseHeadersRead))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                await using var body = await response.Content.ReadAsStreamAsync();
                await using var output = File.Create(file);
                var buffer = new byte[81920];
                int n;
                while ((n = await body.ReadAsync(buffer)) > 0)
                {
                    firstBytes ??= clock.Elapsed;
                    await output.WriteAsync(buffer.AsMemory(0, n));
                    read += n;
                    if (read >= nextProbe)
                    {
                        peak = Math.Max(peak, GC.GetTotalMemory(true) - baseline);
                        nextProbe += probeEvery;
                    }
                }
            }

            Assert.True(firstBytes < TimeSpan.FromSeconds(5), $"first bytes after {firstBytes}");
            Assert.True(read > 100L * 1024 * 1024, $"archive only {read} bytes, too small to prove streaming");
            Assert.True(peak < 100L * 1024 * 1024, $"live memory grew by {peak / 1024 / 1024} MB");
            using var zip = ZipFile.OpenRead(file);
            Assert.Equal(count, zip.Entries.Count(e => e.FullName.StartsWith("zakup/", StringComparison.Ordinal)));
            Assert.Equal(count, Index(zip).Count);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private async Task<(Guid TenantId, string Nip)> CreateTenantAsync()
    {
        var nip = TestNip.New();
        var response = await _owner.PostAsJsonAsync("/api/tenants", new { nip, displayName = "Export test" });
        response.EnsureSuccessStatusCode();
        var tenantId = (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        (await _owner.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", new SellerProfileRequest(
            "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "93000000002026111700000815", false,
            "FV/{nr}/{MM}/{RRRR}"))).EnsureSuccessStatusCode();
        return (tenantId, nip);
    }

    private async Task<(HttpResponseMessage Response, ZipArchive Zip)> ExportAsync(Guid tenantId, string query)
    {
        var response = await _owner.GetAsync($"/api/tenants/{tenantId}/invoices/export?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (response, new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read));
    }

    private static byte[] Bytes(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static List<string[]> Index(ZipArchive zip)
    {
        var bytes = Bytes(zip, "indeks.csv");
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n");
        Assert.Equal(IndexHeader, lines[0]);
        Assert.Equal("", lines[^1]);
        return lines[1..^1].Select(Cells).ToList();
    }

    private static string[] Cells(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted && c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
            else if (c == '"') quoted = !quoted;
            else if (c == ';' && !quoted) { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }

        cells.Add(cell.ToString());
        return [.. cells];
    }

    private static string KsefNumber(string nip, DateOnly date) => $"{nip}-{date:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()}-AB";

    private async Task<(Guid Id, string Number)> SeedIssuedAsync(Guid tenantId, string nip, DateOnly issueDate, IssuedInvoiceStatus status, bool upo = false)
    {
        var number = KsefNumber(nip, issueDate);
        var id = await WithDbAsync(async db =>
        {
            var invoice = IssuedInvoice.Draft(Guid.NewGuid(), tenantId, issueDate, Now);
            invoice.SetBuyer(BuyerKind.Company, "1111111111", "Nabywca Testowy", "ul. Krzywa 2", "00-002 Warszawa");
            invoice.ReplaceLines([("Usługa", "szt.", 1m, 100m, VatRate.Rate23)]);
            invoice.SetDates(issueDate, issueDate, issueDate.AddDays(14));
            if (status != IssuedInvoiceStatus.Draft)
            {
                var profile = await db.SellerProfiles.SingleAsync(p => p.TenantId == tenantId);
                var count = await db.IssuedInvoices.CountAsync(i => i.TenantId == tenantId && i.Number != null);
                invoice.Issue(profile, nip, $"FV/{count + 1}", Now);
                invoice.AttachSentXml(Encoding.UTF8.GetBytes($"<Faktura>{number}</Faktura>"), "test", null);
                invoice.MarkSendStarted("session-1", Now);
                if (status == IssuedInvoiceStatus.Accepted)
                {
                    invoice.MarkAccepted(number, Now, 200, "Sukces", Now);
                    if (upo)
                        invoice.AttachUpo(Encoding.UTF8.GetBytes($"<UPO>{number}</UPO>"), Now);
                }
                else
                {
                    invoice.MarkRejected(450, "Błąd", null, Now);
                }
            }

            db.IssuedInvoices.Add(invoice);
            await db.SaveChangesAsync();
            return invoice.Id;
        });
        return (id, number);
    }

    private static InvoiceHeader Header(Guid tenantId, InvoiceDirection direction, string number, DateOnly issueDate) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        KSeFInvoiceNumber = number,
        KSeFReferenceNumber = number,
        InvoiceNumber = direction == InvoiceDirection.Received ? "FV/ZAKUP" : "FV/INNE",
        VendorName = "Dostawca Testowy",
        VendorNip = "1111111111",
        Direction = direction,
        IssueDate = issueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        AcquisitionDate = issueDate.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc),
        FirstSeenAt = Now,
        LastUpdatedAt = Now,
        NotifiedAt = Now,
    };

    private Task SeedHeaderAsync(Guid tenantId, InvoiceDirection direction, string number, DateOnly issueDate, byte[]? xml, bool isPaid = false,
        string? buyerName = null, decimal gross = 123m, string invoiceType = "Vat", string vendorName = "Dostawca Testowy") =>
        WithDbAsync(async db =>
        {
            var header = Header(tenantId, direction, number, issueDate);
            var nip = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Nip).SingleAsync();
            header.VendorName = vendorName;
            header.BuyerNip = nip;
            header.BuyerName = buyerName;
            header.InvoiceType = invoiceType;
            header.AmountNet = gross == 123m ? 100m : 1000m;
            header.AmountVat = gross - header.AmountNet;
            header.AmountGross = gross;
            header.DueDate = issueDate.AddDays(14);
            header.IsPaid = isPaid;
            header.PaidAt = isPaid ? Now : null;
            header.PaidByUserId = isPaid ? "payer" : null;
            header.XmlFetchedAt = xml is null ? null : Now;
            db.InvoiceHeaders.Add(header);
            if (xml is not null)
                db.InvoiceXmls.Add(new InvoiceXml { InvoiceHeaderId = header.Id, Content = xml, Sha256 = "x", StoredAt = Now });
            return await db.SaveChangesAsync();
        });

    private Task<string> PaidStateAsync(Guid tenantId) => WithDbAsync(async db => string.Join('|', (await db.InvoiceHeaders
            .Where(h => h.TenantId == tenantId).OrderBy(h => h.KSeFInvoiceNumber)
            .Select(h => new { h.KSeFInvoiceNumber, h.IsPaid, h.PaidAt, h.PaidByUserId, h.NotifiedAt, h.XmlFetchedAt, h.XmlAttemptedAt, h.LastUpdatedAt })
            .ToListAsync()).Select(h => h.ToString())));

    private async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
