using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using KSeF.Client.Api.Services;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.DI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync.Issuing;
using Xunit.Abstractions;

namespace OpenKSeF.Domain.Tests.Issuing;

/// <summary>Real KSeF TEST runs (NIP 2 to NIP 1). Opt-in: KSEF_TEST_SEND=1 and open-ksef/.env.test with KSEF_TEST_* values.</summary>
[Trait("Category", "KSeFTest")]
public class KSeFTestSendTests(ITestOutputHelper output)
{
    [KSeFTestFact]
    public async Task SendsOneInvoice_GetsKsefNumberAndUpo()
    {
        await using var ksef = await TestKSeF.StartAsync();
        var number = $"OKSEF-T199/{DateTime.UtcNow:yyMMddHHmmss}";

        var current = await ksef.ProcessUntilDoneAsync(ksef.NewInvoice(number, "Usługa testowa T-199"), ksef.Token, output);

        AssertAcceptedWithUpo(current);
        var login = await ksef.Submitter.LoginAsync(ksef.SellerNip, ksef.Token);
        var listed = await ksef.Submitter.ListInvoicesAsync(login, current.KsefSessionRef!);
        var hash = Convert.ToBase64String(SHA256.HashData(current.SentXml!));
        Assert.Contains(listed, s => s.ElementRef == current.KsefElementRef && s.InvoiceHash == hash);
    }

    [KSeFTestFact]
    public async Task Duplicate440_SameBytesRecoverOriginal_OtherBytesRejected()
    {
        await using var ksef = await TestKSeF.StartAsync();
        var number = $"OKSEF-T199D/{DateTime.UtcNow:yyMMddHHmmss}";

        var first = ksef.NewInvoice(number, "Usługa testowa T-199 duplikat");
        var original = await ksef.ProcessUntilDoneAsync(first, ksef.Token, output);
        AssertAcceptedWithUpo(original);

        var recovered = await ksef.ProcessUntilDoneAsync(ksef.NewInvoice(number, "Usługa testowa T-199 duplikat", original.SentXml), ksef.Token, output);
        AssertAcceptedWithUpo(recovered);
        Assert.Equal(original.KsefNumber, recovered.KsefNumber);
        Assert.Equal(original.KsefSessionRef, recovered.KsefSessionRef);
        Assert.Equal(original.KsefElementRef, recovered.KsefElementRef);

        var other = await ksef.ProcessUntilDoneAsync(ksef.NewInvoice(number, "Inna treść pod tym samym numerem"), ksef.Token, output);
        Assert.Equal(IssuedInvoiceStatus.Rejected, other.Status);
        Assert.Equal(KSeFSentInvoice.Duplicate, other.KsefStatusCode);
        Assert.Equal(IssuedInvoiceSendProcessor.NumberTakenMessage, other.KsefStatusDescription);
        Assert.Contains(original.KsefNumber!, other.KsefStatusDetails);

        var login = await ksef.Submitter.LoginAsync(ksef.SellerNip, ksef.Token);
        var raw = await ksef.Submitter.GetInvoiceAsync(login, other.KsefSessionRef!, other.KsefElementRef!);
        output.WriteLine($"440 extensions: originalKsefNumber={raw!.OriginalKsefNumber} originalSessionRef={raw.OriginalSessionRef}");
        Assert.Equal(original.KsefNumber, raw.OriginalKsefNumber);
        Assert.Equal(original.KsefSessionRef, raw.OriginalSessionRef);
    }

    [KSeFTestFact]
    public async Task CorruptedToken_UnsentRejected_SentStaysSending_QuicklyAndWithoutSend()
    {
        await using var ksef = await TestKSeF.StartAsync();
        var corrupted = ksef.Token[..^4] + new string(ksef.Token[^4..].Reverse().Select(c => c == '0' ? '1' : '0').ToArray());
        var stamp = DateTime.UtcNow.ToString("yyMMddHHmmss");

        var timer = Stopwatch.StartNew();
        var unsent = await ksef.ProcessOnceAsync(ksef.NewInvoice($"OKSEF-T228A/{stamp}", "T-228"), corrupted);
        output.WriteLine($"unsent: {timer.Elapsed.TotalSeconds:F1}s status={unsent.Status} code={unsent.KsefStatusCode} desc={unsent.KsefStatusDescription} details={unsent.KsefStatusDetails}");
        Assert.Equal(IssuedInvoiceStatus.Rejected, unsent.Status);
        Assert.Equal(IssuedInvoiceSendProcessor.InvalidTokenMessage, unsent.KsefStatusDescription);
        Assert.Null(unsent.SendStartedAt);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(30));

        var started = ksef.NewInvoice($"OKSEF-T228B/{stamp}", "T-228");
        started.MarkSendStarted("20261102-SO-0000000000-0000000000-00", DateTime.UtcNow);
        started.RecordElementRef("20261102-EE-0000000000-0000000000-00", DateTime.UtcNow);
        var sent = await ksef.ProcessOnceAsync(started, corrupted);
        output.WriteLine($"sent: status={sent.Status} lastError={sent.LastError}");
        Assert.Equal(IssuedInvoiceStatus.Sending, sent.Status);
        Assert.Equal(IssuedInvoiceSendProcessor.InvalidTokenMessage, sent.LastError);
    }

    private static void AssertAcceptedWithUpo(IssuedInvoice current)
    {
        Assert.Equal(IssuedInvoiceStatus.Accepted, current.Status);
        Assert.NotNull(current.KsefNumber);
        Assert.Equal(35, current.KsefNumber!.Length);
        Assert.NotNull(current.UpoXml);
        Assert.Contains(current.KsefNumber, Encoding.UTF8.GetString(current.UpoXml!));
    }

    private sealed class TestKSeF(ServiceProvider provider, IReadOnlyDictionary<string, string> env) : IAsyncDisposable
    {
        private readonly AesGcmEncryptionService _encryption = new(RandomNumberGenerator.GetBytes(32));

        public string SellerNip => env["KSEF_TEST_NIP_2"];
        public string Token => env["KSEF_TEST_TOKEN_2"];
        public KSeFInvoiceSubmitter Submitter { get; } = new(provider.GetRequiredService<IKSeFClient>(), provider.GetRequiredService<ICryptographyService>());

        public static async Task<TestKSeF> StartAsync()
        {
            var env = File.ReadAllLines(EnvFile())
                .Where(l => l.Contains('=') && !l.TrimStart().StartsWith('#'))
                .Select(l => l.Split('=', 2))
                .ToDictionary(p => p[0].Trim(), p => p[1].Trim().Trim('"', '\''));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddKSeFClient(o => o.BaseUrl = KsefEnvironmentsUris.TEST);
            services.AddCryptographyClient();
            var provider = services.BuildServiceProvider();
            foreach (var hosted in provider.GetServices<IHostedService>())
                await hosted.StartAsync(CancellationToken.None);
            return new TestKSeF(provider, env);
        }

        public IssuedInvoice NewInvoice(string number, string lineName, byte[]? xml = null)
        {
            var invoice = Fa3InvoiceWriterTests.Issued(i => i.AddLine(lineName, null, 1, 100m, VatRate.Rate23),
                sellerNip: SellerNip, buyerNip: env["KSEF_TEST_NIP"], number: number, tenantId: Guid.NewGuid(), now: DateTime.UtcNow);
            if (xml is not null)
            {
                invoice.AttachSentXml(xml, Convert.ToHexString(SHA256.HashData(xml)), "test", null);
                return invoice;
            }

            var written = new Fa3InvoiceWriter().Write(invoice, DateTime.UtcNow);
            Assert.Empty(written.SchemaErrors);
            invoice.AttachSentXml(written.Xml, Convert.ToHexString(SHA256.HashData(written.Xml)), "test", null);
            return invoice;
        }

        public async Task<IssuedInvoice> ProcessOnceAsync(IssuedInvoice invoice, string token)
        {
            var options = await SeedAsync(invoice, token);
            await CycleAsync(options);
            await using var db = new ApplicationDbContext(options);
            return await db.IssuedInvoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        }

        public async Task<IssuedInvoice> ProcessUntilDoneAsync(IssuedInvoice invoice, string token, ITestOutputHelper output)
        {
            var options = await SeedAsync(invoice, token);
            IssuedInvoice current = invoice;
            var deadline = DateTime.UtcNow.AddMinutes(4);
            while (DateTime.UtcNow < deadline)
            {
                await CycleAsync(options);
                await using var db = new ApplicationDbContext(options);
                current = await db.IssuedInvoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
                if (current.Status == IssuedInvoiceStatus.Rejected || current.UpoXml is not null)
                    break;
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            output.WriteLine($"number={current.Number} status={current.Status} ksef={current.KsefNumber} code={current.KsefStatusCode} " +
                             $"desc={current.KsefStatusDescription} details={current.KsefStatusDetails} lastError={current.LastError} " +
                             $"session={current.KsefSessionRef} element={current.KsefElementRef} upoBytes={current.UpoXml?.Length}");
            return current;
        }

        public ValueTask DisposeAsync() => provider.DisposeAsync();

        private async Task<DbContextOptions<ApplicationDbContext>> SeedAsync(IssuedInvoice invoice, string token)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            await using var db = new ApplicationDbContext(options);
            db.Tenants.Add(new Tenant
            {
                Id = invoice.TenantId,
                UserId = "t199",
                Nip = SellerNip,
                KSeFCredentials = [new KSeFCredential { Id = Guid.NewGuid(), TenantId = invoice.TenantId, Type = CredentialType.Token, EncryptedToken = _encryption.Encrypt(token) }]
            });
            db.IssuedInvoices.Add(invoice);
            await db.SaveChangesAsync();
            return options;
        }

        private async Task CycleAsync(DbContextOptions<ApplicationDbContext> options)
        {
            await using var db = new ApplicationDbContext(options);
            await new IssuedInvoiceSendProcessor(db, Submitter, _encryption, TimeProvider.System,
                NullLogger<IssuedInvoiceSendProcessor>.Instance).ProcessAsync("test", CancellationToken.None);
        }
    }

    private static string EnvFile([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", ".env.test"));

    private sealed class KSeFTestFactAttribute : FactAttribute
    {
        public KSeFTestFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("KSEF_TEST_SEND") != "1")
                Skip = "Real KSeF TEST send; set KSEF_TEST_SEND=1 to run.";
        }
    }
}
