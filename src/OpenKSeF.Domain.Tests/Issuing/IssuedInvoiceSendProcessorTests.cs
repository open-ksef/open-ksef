using System.Net;
using System.Security.Cryptography;
using System.Text;
using KSeF.Client.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync.Issuing;

namespace OpenKSeF.Domain.Tests.Issuing;

public class IssuedInvoiceSendProcessorTests
{
    private const string Token = "secret-ksef-token-value";
    private const string AccessToken = "access-token-value";
    private static readonly byte[] Xml = Encoding.UTF8.GetBytes("<Faktura/>");
    private static readonly string XmlHash = Convert.ToBase64String(SHA256.HashData(Xml));

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly FixedTime _time = new(new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeSubmitter _submitter = new();
    private readonly ListLogger _logs = new();
    private readonly AesGcmEncryptionService _encryption = new(RandomNumberGenerator.GetBytes(32));
    private readonly Guid _tenantId = Guid.NewGuid();

    private ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_dbName).Options);

    private async Task<Guid> SeedAsync(Action<IssuedInvoice>? arrange = null, string environment = "test", bool tokenCredential = true)
    {
        await using var db = NewDb();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            UserId = "u1",
            Nip = "5260250274",
            KSeFCredentials =
            [
                tokenCredential
                    ? new KSeFCredential { Id = Guid.NewGuid(), TenantId = _tenantId, Type = CredentialType.Token, EncryptedToken = _encryption.Encrypt(Token) }
                    : new KSeFCredential { Id = Guid.NewGuid(), TenantId = _tenantId, Type = CredentialType.Certificate, EncryptedCertificateData = "x", CertificateFingerprint = "f" }
            ]
        });
        var invoice = Fa3InvoiceWriterTests.Issued(i => i.AddLine("Usługa", null, 1, 100m, VatRate.Rate23), tenantId: _tenantId);
        invoice.AttachSentXml(Xml, Convert.ToHexString(SHA256.HashData(Xml)), environment, null);
        arrange?.Invoke(invoice);
        db.IssuedInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    private async Task RunAsync(string environment = "test")
    {
        await using var db = NewDb();
        var processor = new IssuedInvoiceSendProcessor(db, new FakeGateway(), _submitter, _encryption, _time,
            new LoggerAdapter<IssuedInvoiceSendProcessor>(_logs));
        await processor.ProcessAsync(environment, CancellationToken.None);
    }

    private async Task<IssuedInvoice> LoadAsync(Guid id)
    {
        await using var db = NewDb();
        return await db.IssuedInvoices.SingleAsync(i => i.Id == id);
    }

    private static KSeFSentInvoice Status(int code, string? ksefNumber = null, string elementRef = "el-1", string? hash = null, params string[] details) =>
        new(elementRef, hash ?? XmlHash, code, $"desc {code}", details, ksefNumber, new DateTimeOffset(2026, 11, 3, 10, 0, 5, TimeSpan.Zero));

    [Fact]
    public async Task Queued_SavesSessionRefAndSendStartedBeforeSend_ElementRefAfter()
    {
        var id = await SeedAsync();
        _submitter.OnSend = async () =>
        {
            var during = await LoadAsync(id);
            Assert.Equal("sess-1", during.KsefSessionRef);
            Assert.NotNull(during.SendStartedAt);
            Assert.Null(during.KsefElementRef);
            return "el-1";
        };

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Sending, invoice.Status);
        Assert.Equal("el-1", invoice.KsefElementRef);
        Assert.Equal(1, invoice.AttemptCount);
        Assert.Equal(1, _submitter.Sends);
        Assert.Equal(1, _submitter.Closes);
    }

    [Fact]
    public async Task SendingWithElementRef_Accepted_StoresKsefNumberAndUpo()
    {
        var id = await SeedAsync(i => { i.MarkSendStarted("sess-1", DateTime.UtcNow); i.RecordElementRef("el-1", DateTime.UtcNow); });
        _submitter.OnGet = _ => Status(200, "5260250274-20261103-ABCDEF-01");
        _submitter.OnUpo = () => Encoding.UTF8.GetBytes("<UPO/>");

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Accepted, invoice.Status);
        Assert.Equal("5260250274-20261103-ABCDEF-01", invoice.KsefNumber);
        Assert.NotNull(invoice.AcceptedAt);
        Assert.Equal("<UPO/>", Encoding.UTF8.GetString(invoice.UpoXml!));
        Assert.Null(invoice.NextAttemptAt);
        Assert.Equal(0, _submitter.Sends);
    }

    [Fact]
    public async Task Accepted_MissingUpo_RetriedLater()
    {
        var id = await SeedAsync(i => { i.MarkSendStarted("sess-1", DateTime.UtcNow); i.RecordElementRef("el-1", DateTime.UtcNow); });
        _submitter.OnGet = _ => Status(200, "KSEF-1");
        _submitter.OnUpo = () => null;

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Accepted, invoice.Status);
        Assert.Null(invoice.UpoXml);
        Assert.Equal(_time.UtcNow.AddSeconds(30), invoice.NextAttemptAt);

        _time.Advance(TimeSpan.FromSeconds(31));
        _submitter.OnUpo = () => Encoding.UTF8.GetBytes("<UPO/>");
        await RunAsync();

        Assert.NotNull((await LoadAsync(id)).UpoXml);
    }

    [Fact]
    public async Task RestartAfterSendWithoutElementRef_MatchesByHash_NoResend()
    {
        var id = await SeedAsync(i => i.MarkSendStarted("sess-old", DateTime.UtcNow));
        _submitter.OnList = sessionRef =>
        {
            Assert.Equal("sess-old", sessionRef);
            return [Status(150, elementRef: "other", hash: "zzz"), Status(200, "KSEF-1", elementRef: "el-found")];
        };
        _submitter.OnUpo = () => Encoding.UTF8.GetBytes("<UPO/>");

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(0, _submitter.Sends);
        Assert.Equal("el-found", invoice.KsefElementRef);
        Assert.Equal(IssuedInvoiceStatus.Accepted, invoice.Status);
    }

    [Fact]
    public async Task RestartAfterCrashBeforeSend_NotInSession_ResendsInNewSession()
    {
        var id = await SeedAsync(i => i.MarkSendStarted("sess-old", DateTime.UtcNow));
        _submitter.OnList = _ => [];

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(1, _submitter.Sends);
        Assert.Equal("sess-1", invoice.KsefSessionRef);
        Assert.Equal("el-1", invoice.KsefElementRef);
    }

    [Fact]
    public async Task Duplicate440_RejectedAndNeverResent()
    {
        var id = await SeedAsync(i => { i.MarkSendStarted("sess-1", DateTime.UtcNow); i.RecordElementRef("el-1", DateTime.UtcNow); });
        _submitter.OnGet = _ => Status(440, details: "Duplikat faktury");

        await RunAsync();
        _time.Advance(TimeSpan.FromMinutes(5));
        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Rejected, invoice.Status);
        Assert.Equal(440, invoice.KsefStatusCode);
        Assert.Equal("Duplikat faktury", invoice.KsefStatusDetails);
        Assert.Equal(0, _submitter.Sends);
        Assert.Equal(1, _submitter.Gets);
    }

    [Fact]
    public async Task Rejected_StoresCodeDescriptionDetails()
    {
        var id = await SeedAsync(i => { i.MarkSendStarted("sess-1", DateTime.UtcNow); i.RecordElementRef("el-1", DateTime.UtcNow); });
        _submitter.OnGet = _ => Status(450, details: ["Błąd weryfikacji", "P_15"]);

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Rejected, invoice.Status);
        Assert.Equal(450, invoice.KsefStatusCode);
        Assert.Equal("desc 450", invoice.KsefStatusDescription);
        Assert.Equal("Błąd weryfikacji; P_15", invoice.KsefStatusDetails);
        Assert.Null(invoice.NextAttemptAt);
    }

    [Fact]
    public async Task Processing150_LeftForNextCycle()
    {
        var id = await SeedAsync(i => { i.MarkSendStarted("sess-1", DateTime.UtcNow); i.RecordElementRef("el-1", DateTime.UtcNow); });
        _submitter.OnGet = _ => Status(150);

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Sending, invoice.Status);
        Assert.Null(invoice.LastError);
    }

    [Fact]
    public async Task RateLimited_WaitsRecommendedDelay()
    {
        var id = await SeedAsync();
        _submitter.OnSend = () => throw new KsefRateLimitException("429", 120, null, null!);

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Sending, invoice.Status);
        Assert.Equal(IssuedInvoiceSendProcessor.RateLimitedMessage, invoice.LastError);
        Assert.Equal(_time.UtcNow.AddSeconds(120), invoice.NextAttemptAt);
    }

    [Fact]
    public async Task ServerError_BackoffDoublesUpTo15Minutes_StaysSending()
    {
        var id = await SeedAsync(i => { i.MarkSendStarted("sess-1", DateTime.UtcNow); i.RecordElementRef("el-1", DateTime.UtcNow); });
        _submitter.OnGet = _ => throw new KsefApiException("boom", HttpStatusCode.BadGateway, null!, null!);

        var gaps = new List<TimeSpan>();
        for (var cycle = 0; cycle < 10; cycle++)
        {
            await RunAsync();
            var invoice = await LoadAsync(id);
            Assert.Equal(IssuedInvoiceStatus.Sending, invoice.Status);
            Assert.Equal(IssuedInvoiceSendProcessor.UnavailableMessage, invoice.LastError);
            gaps.Add(invoice.NextAttemptAt!.Value - _time.UtcNow);
            _time.Advance(gaps[^1]);
        }

        Assert.Equal(TimeSpan.FromSeconds(10), gaps[0]);
        Assert.Equal(TimeSpan.FromSeconds(20), gaps[1]);
        Assert.Equal(TimeSpan.FromMinutes(15), gaps[^1]);
        Assert.All(gaps, g => Assert.True(g <= TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public async Task NetworkError_RetriedNotRejected()
    {
        var id = await SeedAsync();
        _submitter.OnOpen = () => throw new HttpRequestException("no route");

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Queued, invoice.Status);
        Assert.Equal(IssuedInvoiceSendProcessor.UnavailableMessage, invoice.LastError);
        Assert.Equal(0, _submitter.Sends);
    }

    [Fact]
    public async Task Forbidden_RejectedWithPermissionMessage_NoRetry()
    {
        var id = await SeedAsync();
        _submitter.OnOpen = () => throw new KsefApiException("forbidden", HttpStatusCode.Forbidden, null!, null!);

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Rejected, invoice.Status);
        Assert.Equal(IssuedInvoiceSendProcessor.NoWritePermissionMessage, invoice.KsefStatusDescription);
        Assert.Null(invoice.NextAttemptAt);
    }

    [Fact]
    public async Task EnvironmentChanged_RejectedNotSent()
    {
        var id = await SeedAsync(environment: "test");

        await RunAsync("production");

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Rejected, invoice.Status);
        Assert.Equal(IssuedInvoiceSendProcessor.EnvironmentChangedMessage, invoice.KsefStatusDescription);
        Assert.Equal(0, _submitter.Opens);
    }

    [Fact]
    public async Task CertificateOnlyTenant_RejectedWithTokenMessage()
    {
        var id = await SeedAsync(tokenCredential: false);

        await RunAsync();

        Assert.Equal(IssuedInvoiceSendProcessor.MissingTokenMessage, (await LoadAsync(id)).KsefStatusDescription);
    }

    [Fact]
    public async Task TokenNeverLogged()
    {
        await SeedAsync();
        _submitter.OnSend = () => throw new KsefApiException("boom", HttpStatusCode.InternalServerError, null!, null!);

        await RunAsync();

        Assert.NotEmpty(_logs.Messages);
        Assert.DoesNotContain(_logs.Messages, m => m.Contains(Token) || m.Contains(AccessToken));
    }

    private sealed class FakeGateway : IKSeFGateway
    {
        public Task<KSeFSession> InitSessionAsync(string nip, string authToken, CancellationToken ct = default)
        {
            Assert.Equal(Token, authToken);
            return Task.FromResult(new KSeFSession(AccessToken, string.Empty, DateTime.UtcNow.AddHours(1)));
        }

        public Task<KSeFSession> InitSignedSessionAsync(string nip, Func<string, Task<string>> xmlSigner, string fingerprint, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<KSeFInvoiceQueryResult> QueryInvoicesAsync(KSeFSession session, KSeFQueryCriteria criteria, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<byte[]> DownloadInvoiceAsync(KSeFSession session, string ksefNumber, CancellationToken ct = default) => throw new NotSupportedException();
        public Task TerminateSessionAsync(KSeFSession session, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeSubmitter : IKSeFInvoiceSubmitter
    {
        public Action OnOpen = () => { };
        public Func<Task<string>> OnSend = () => Task.FromResult("el-1");
        public Func<string, KSeFSentInvoice?> OnGet = _ => null;
        public Func<string, IReadOnlyList<KSeFSentInvoice>> OnList = _ => [];
        public Func<byte[]?> OnUpo = () => null;
        public int Opens, Sends, Gets, Closes;

        public Task<KSeFOnlineSession> OpenSessionAsync(string accessToken, CancellationToken ct = default)
        {
            Opens++;
            OnOpen();
            return Task.FromResult(new KSeFOnlineSession($"sess-{Opens}", accessToken, [1], [2]));
        }

        public Task<string> SendAsync(KSeFOnlineSession session, byte[] invoiceXml, CancellationToken ct = default)
        {
            Sends++;
            Assert.Equal(Xml, invoiceXml);
            return OnSend();
        }

        public Task<KSeFSentInvoice?> GetInvoiceAsync(string accessToken, string sessionRef, string elementRef, CancellationToken ct = default)
        {
            Gets++;
            return Task.FromResult(OnGet(elementRef));
        }

        public Task<IReadOnlyList<KSeFSentInvoice>> ListInvoicesAsync(string accessToken, string sessionRef, CancellationToken ct = default) =>
            Task.FromResult(OnList(sessionRef));

        public Task<byte[]?> GetUpoAsync(string accessToken, string sessionRef, string elementRef, CancellationToken ct = default) =>
            Task.FromResult(OnUpo());

        public Task CloseSessionAsync(KSeFOnlineSession session, CancellationToken ct = default)
        {
            Closes++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public DateTime UtcNow => _now.UtcDateTime;
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class ListLogger
    {
        public List<string> Messages { get; } = [];
    }

    private sealed class LoggerAdapter<T>(ListLogger sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Messages.Add(formatter(state, exception) + exception);
    }
}
