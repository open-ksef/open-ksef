using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using KSeF.Client.Core.Exceptions;
using KSeF.Client.Core.Interfaces.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Services;
using KSeF.Client.Core.Models;
using OpenKSeF.Sync;
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

    private async Task<Guid> SeedAsync(Action<IssuedInvoice>? arrange = null, string environment = "test", CredentialType credentialType = CredentialType.Token, bool withSecret = true, Guid? tenant = null, bool addTenant = true)
    {
        var tenantId = tenant ?? _tenantId;
        await using var db = NewDb();
        if (addTenant)
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                UserId = "u1",
                Nip = "5260250274",
                KSeFCredentials =
                [
                    credentialType == CredentialType.Token
                        ? new KSeFCredential { Id = Guid.NewGuid(), TenantId = tenantId, Type = CredentialType.Token, EncryptedToken = withSecret ? _encryption.Encrypt(Token) : null }
                        : new KSeFCredential { Id = Guid.NewGuid(), TenantId = tenantId, Type = CredentialType.Certificate, EncryptedCertificateData = withSecret ? _encryption.Encrypt("pfx") : null, CertificateFingerprint = "f" }
                ]
            });
        var invoice = Fa3InvoiceWriterTests.Issued(i => i.AddLine("Usługa", null, 1, 100m, VatRate.Rate23), tenantId: tenantId);
        invoice.AttachSentXml(Xml, Convert.ToHexString(SHA256.HashData(Xml)), environment, null);
        arrange?.Invoke(invoice);
        db.IssuedInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    private async Task RunAsync(string environment = "test")
    {
        await using var db = NewDb();
        var processor = new IssuedInvoiceSendProcessor(db, _submitter, _time,
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

    private static KSeFSentInvoice Duplicate(string? originalNumber = "KSEF-ORIG", string? originalSession = "sess-orig") =>
        Status(KSeFSentInvoice.Duplicate, details: "Duplikat faktury") with { OriginalKsefNumber = originalNumber, OriginalSessionRef = originalSession };

    private void Sent(IssuedInvoice invoice)
    {
        invoice.MarkSendStarted("sess-1", _time.UtcNow);
        invoice.RecordElementRef("el-1", _time.UtcNow);
    }

    private async Task<Guid> SeedResentAsync()
    {
        var id = await SeedAsync();
        _submitter.OnSend = () => throw new HttpRequestException("response lost");
        await RunAsync();
        _time.Advance(TimeSpan.FromMinutes(1));
        _submitter.OnSend = () => Task.FromResult("el-2");
        await RunAsync();
        var resent = await LoadAsync(id);
        Assert.Equal((2, "el-2"), (resent.AttemptCount, resent.KsefElementRef));
        _time.Advance(TimeSpan.FromMinutes(1));
        return id;
    }

    private async Task<Guid> SeedHeldThenResentAsync(int code)
    {
        var id = await SeedResentAsync();
        _submitter.OnGet = _ => Status(code);
        await RunAsync();
        _time.Advance(TimeSpan.FromMinutes(15));
        _submitter.OnSend = () => Task.FromResult("el-3");
        await RunAsync();
        var resent = await LoadAsync(id);
        Assert.Equal((3, "el-3"), (resent.AttemptCount, resent.KsefElementRef));
        return id;
    }

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
    public async Task Duplicate440_HashMatchInOriginalSession_AcceptedWithOriginalNumberAndUpo()
    {
        var id = await SeedAsync(Sent);
        _submitter.OnGet = _ => Duplicate();
        _submitter.OnList = sessionRef =>
        {
            Assert.Equal("sess-orig", sessionRef);
            return [Status(200, "KSEF-OTHER", elementRef: "el-other", hash: "zzz"), Status(200, "KSEF-ORIG", elementRef: "el-orig")];
        };
        _submitter.OnUpo = () => Encoding.UTF8.GetBytes("<UPO/>");

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Accepted, invoice.Status);
        Assert.Equal("KSEF-ORIG", invoice.KsefNumber);
        Assert.Equal("sess-orig", invoice.KsefSessionRef);
        Assert.Equal("el-orig", invoice.KsefElementRef);
        Assert.Equal(("sess-orig", "el-orig"), _submitter.UpoRequests.Single());
        Assert.NotNull(invoice.UpoXml);
        Assert.Equal(0, _submitter.Sends);
    }

    [Fact]
    public async Task Duplicate440_OtherDocumentHoldsNumber_RejectedAsNumberTaken_NeverResent()
    {
        var id = await SeedAsync(Sent);
        _submitter.OnGet = _ => Duplicate();
        _submitter.OnList = _ => [Status(200, "KSEF-ORIG", elementRef: "el-orig", hash: "other-content")];

        await RunAsync();
        _time.Advance(TimeSpan.FromMinutes(5));
        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Rejected, invoice.Status);
        Assert.Equal(440, invoice.KsefStatusCode);
        Assert.Equal("Ten numer faktury jest już użyty w KSeF. Sprawdź tę fakturę w KSeF, zanim wystawisz ją pod nowym numerem.", invoice.KsefStatusDescription);
        Assert.Equal("Duplikat faktury; Numer KSeF: KSEF-ORIG", invoice.KsefStatusDetails);
        Assert.Null(invoice.KsefNumber);
        Assert.Equal(0, _submitter.Sends);
        Assert.Equal(1, _submitter.Gets);
    }

    [Theory]
    [InlineData("sess-orig", false)]
    [InlineData(null, false)]
    [InlineData("sess-orig", true)]
    public async Task Duplicate440_FirstAttempt_NoHashMatch_RejectedInThatCycle(string? originalSession, bool sessionGone)
    {
        var id = await SeedAsync(Sent);
        _submitter.OnGet = _ => Duplicate(originalSession is null ? null : "KSEF-ORIG", originalSession);
        _submitter.OnList = _ => sessionGone ? throw new KsefApiException("no session", HttpStatusCode.BadRequest, null!, null!) : [];

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(1, invoice.AttemptCount);
        Assert.Equal(IssuedInvoiceStatus.Rejected, invoice.Status);
        Assert.Equal(IssuedInvoiceSendProcessor.NumberTakenMessage, invoice.KsefStatusDescription);
        Assert.Equal(1, _submitter.Gets);
        Assert.Equal(0, _submitter.Sends);
    }

    [Fact]
    public async Task Duplicate440_FirstAttempt_OriginalSessionForbidden_RejectedInThatCycle()
    {
        var id = await SeedAsync(Sent);
        _submitter.OnGet = _ => Duplicate();
        _submitter.OnList = _ => throw new KsefApiException("not your session", HttpStatusCode.Forbidden, null!, null!);

        await RunAsync();

        Assert.Equal(IssuedInvoiceStatus.Rejected, (await LoadAsync(id)).Status);
    }

    [Fact]
    public async Task AttemptCount_SavedBeforeEveryPost()
    {
        var id = await SeedAsync();
        var seenDuringPost = new List<int>();
        _submitter.OnSend = async () =>
        {
            seenDuringPost.Add((await LoadAsync(id)).AttemptCount);
            if (seenDuringPost.Count == 1)
                throw new HttpRequestException("response lost");
            return "el-2";
        };

        await RunAsync();
        _time.Advance(TimeSpan.FromMinutes(1));
        await RunAsync();

        Assert.Equal([1, 2], seenDuringPost);
    }

    [Fact]
    public async Task OpenSessionFails_NoPost_AttemptNotCounted()
    {
        var id = await SeedAsync();
        _submitter.OnOpen = () => throw new HttpRequestException("no route");

        await RunAsync();

        Assert.Equal(0, (await LoadAsync(id)).AttemptCount);
    }

    [Fact]
    public async Task SendFails_SessionClosedWithinCloseCap()
    {
        var id = await SeedAsync();
        var closeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _submitter.OnSend = () => throw new HttpRequestException("connection reset");
        _submitter.OnClose = () =>
        {
            closeStarted.SetResult();
            return new TaskCompletionSource().Task;
        };

        var run = RunAsync();
        await closeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _time.Advance(IssuedInvoiceSendProcessor.CloseCap - TimeSpan.FromSeconds(1));
        Assert.False(run.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(1));
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((1, 1, 1), (_submitter.Opens, _submitter.Sends, _submitter.Closes));
        var invoice = await LoadAsync(id);
        Assert.Equal((IssuedInvoiceStatus.Sending, "sess-1", 1, IssuedInvoiceSendProcessor.UnavailableMessage), (invoice.Status, invoice.KsefSessionRef, invoice.AttemptCount, invoice.LastError));
    }

    [Fact]
    public async Task BudgetRunsOut_SessionClosed_UntriedInvoiceKeepsStatusAttemptsAndSchedule()
    {
        var ids = new[] { await SeedAsync(), await SeedAsync(addTenant: false) };
        var before = new[] { await LoadAsync(ids[0]), await LoadAsync(ids[1]) };
        var hangStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _submitter.OnSend = () =>
        {
            hangStarted.SetResult();
            return new TaskCompletionSource<string>().Task;
        };

        var run = RunAsync();
        await hangStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _time.Advance(IssuedInvoiceSendProcessor.TenantBudget);
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((1, 1, 1), (_submitter.Opens, _submitter.Sends, _submitter.Closes));
        var after = new[] { await LoadAsync(ids[0]), await LoadAsync(ids[1]) };
        var tried = Assert.Single(after, i => i.AttemptCount == 1);
        Assert.Equal((IssuedInvoiceStatus.Sending, IssuedInvoiceSendProcessor.UnavailableMessage), (tried.Status, tried.LastError));
        var untried = Assert.Single(after, i => i.AttemptCount == 0);
        var untriedBefore = before.Single(i => i.Id == untried.Id);
        Assert.Equal(
            (untriedBefore.Status, untriedBefore.LastError, untriedBefore.NextAttemptAt, untriedBefore.UpdatedAt, untriedBefore.KsefSessionRef),
            (untried.Status, untried.LastError, untried.NextAttemptAt, untried.UpdatedAt, untried.KsefSessionRef));
        Assert.Equal(IssuedInvoiceStatus.Queued, untried.Status);
    }

    [Theory]
    [InlineData("sess-orig", false)]
    [InlineData(null, false)]
    [InlineData("sess-orig", true)]
    public async Task Duplicate440_AfterResend_OriginalNotListed_StaysSendingAtMaxBackoff_NeverRejected(string? originalSession, bool sessionGone)
    {
        var id = await SeedResentAsync();
        _submitter.OnGet = _ => Duplicate(originalSession is null ? null : "KSEF-ORIG", originalSession);
        _submitter.OnList = _ => sessionGone ? throw new KsefApiException("no session", HttpStatusCode.BadRequest, null!, null!) : [];

        for (var cycle = 0; cycle < 100; cycle++)
        {
            await RunAsync();

            var invoice = await LoadAsync(id);
            Assert.Equal(IssuedInvoiceStatus.Sending, invoice.Status);
            Assert.Equal(IssuedInvoiceSendProcessor.DuplicateUnresolvedMessage, invoice.LastError);
            Assert.Equal(_time.UtcNow.AddMinutes(15), invoice.NextAttemptAt);
            Assert.Null(invoice.KsefStatusCode);
            _time.Advance(TimeSpan.FromMinutes(15));
        }

        Assert.Equal(100, _submitter.Gets);
        Assert.Equal(2, _submitter.Sends);
    }

    [Fact]
    public async Task Duplicate440_AfterResend_OriginalListedLater_AcceptedWithoutResend()
    {
        var id = await SeedResentAsync();
        _submitter.OnGet = _ => Duplicate();
        _submitter.OnList = _ => [];
        await RunAsync();
        Assert.Equal(IssuedInvoiceStatus.Sending, (await LoadAsync(id)).Status);

        _time.Advance(TimeSpan.FromMinutes(15));
        _submitter.OnList = _ => [Status(200, "KSEF-ORIG", elementRef: "el-orig")];
        _submitter.OnUpo = () => Encoding.UTF8.GetBytes("<UPO/>");
        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Accepted, invoice.Status);
        Assert.Equal("KSEF-ORIG", invoice.KsefNumber);
        Assert.Null(invoice.LastError);
        Assert.Equal(2, _submitter.Sends);
    }

    [Fact]
    public async Task Duplicate440_AfterResend_OriginalListedWithOtherHash_RejectedAsNumberTaken()
    {
        var id = await SeedResentAsync();
        _submitter.OnGet = _ => Duplicate();
        _submitter.OnList = _ => [Status(200, "KSEF-ORIG", elementRef: "el-orig", hash: "other-content")];

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(2, invoice.AttemptCount);
        Assert.Equal(IssuedInvoiceStatus.Rejected, invoice.Status);
        Assert.Equal(440, invoice.KsefStatusCode);
        Assert.Equal(IssuedInvoiceSendProcessor.NumberTakenMessage, invoice.KsefStatusDescription);
        Assert.Equal(2, _submitter.Sends);
    }

    [Theory]
    [InlineData(405)]
    [InlineData(410)]
    [InlineData(415)]
    [InlineData(420)]
    [InlineData(435)]
    [InlineData(500)]
    [InlineData(550)]
    [InlineData(599)]
    public async Task HeldStatus_AfterResend_WaitsBackoff_ThenPostsStoredBytesInNewSession(int code)
    {
        var id = await SeedResentAsync();
        var stored = (await LoadAsync(id)).SentXml;
        _submitter.OnGet = _ => Status(code);

        await RunAsync();

        var waiting = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Sending, waiting.Status);
        Assert.Equal(IssuedInvoiceSendProcessor.DuplicateUnresolvedMessage, waiting.LastError);
        Assert.Equal(_time.UtcNow.AddMinutes(15), waiting.NextAttemptAt);
        Assert.Null(waiting.KsefStatusCode);
        Assert.Null(waiting.KsefElementRef);

        _time.Advance(TimeSpan.FromMinutes(14));
        await RunAsync();
        Assert.Equal(2, _submitter.Sends);

        _time.Advance(TimeSpan.FromMinutes(1));
        _submitter.OnSend = () => Task.FromResult("el-3");
        await RunAsync();

        var resent = await LoadAsync(id);
        Assert.Equal(3, _submitter.Sends);
        Assert.Equal(3, resent.AttemptCount);
        Assert.Equal("el-3", resent.KsefElementRef);
        Assert.Equal("sess-3", resent.KsefSessionRef);
        Assert.Equal(stored, _submitter.SentBytes[^1]);
        Assert.Equal(stored, resent.SentXml);
    }

    [Theory]
    [InlineData(405)]
    [InlineData(410)]
    [InlineData(415)]
    [InlineData(420)]
    [InlineData(435)]
    [InlineData(500)]
    [InlineData(550)]
    [InlineData(599)]
    public async Task HeldStatus_NewPostReturns440_HashMatch_AcceptedWithOriginalNumber(int code)
    {
        var id = await SeedHeldThenResentAsync(code);
        _submitter.OnGet = _ => Duplicate();
        _submitter.OnList = sessionRef => sessionRef == "sess-orig" ? [Status(200, "KSEF-ORIG", elementRef: "el-orig")] : [];
        _submitter.OnUpo = () => Encoding.UTF8.GetBytes("<UPO/>");

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Accepted, invoice.Status);
        Assert.Equal("KSEF-ORIG", invoice.KsefNumber);
        Assert.Equal(("sess-orig", "el-orig"), _submitter.UpoRequests.Single());
        Assert.Equal(3, _submitter.Sends);
    }

    [Theory]
    [InlineData(405)]
    [InlineData(410)]
    [InlineData(415)]
    [InlineData(420)]
    [InlineData(435)]
    [InlineData(500)]
    [InlineData(550)]
    [InlineData(599)]
    public async Task HeldStatus_NewPostReturns200_Accepted(int code)
    {
        var id = await SeedHeldThenResentAsync(code);
        _submitter.OnGet = _ => Status(200, "KSEF-NEW", elementRef: "el-3");
        _submitter.OnUpo = () => Encoding.UTF8.GetBytes("<UPO/>");

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Accepted, invoice.Status);
        Assert.Equal("KSEF-NEW", invoice.KsefNumber);
        Assert.Null(invoice.LastError);
        Assert.Equal(3, _submitter.Sends);
    }

    [Theory]
    [InlineData(405)]
    [InlineData(410)]
    [InlineData(415)]
    [InlineData(420)]
    [InlineData(435)]
    [InlineData(500)]
    [InlineData(550)]
    [InlineData(599)]
    public async Task HeldStatus_KeepsFailing_BackoffDoublesToFourHours_NeverRejected(int code)
    {
        var id = await SeedResentAsync();
        _submitter.OnGet = _ => Status(code);
        var delays = new List<int>();

        for (var round = 0; round < 8; round++)
        {
            await RunAsync();
            var waiting = await LoadAsync(id);
            Assert.Equal(IssuedInvoiceStatus.Sending, waiting.Status);
            var delay = waiting.NextAttemptAt!.Value - _time.UtcNow;
            delays.Add((int)delay.TotalMinutes);
            _time.Advance(delay);
            await RunAsync();
            Assert.Equal(round + 3, _submitter.Sends);
            Assert.Equal(IssuedInvoiceStatus.Sending, (await LoadAsync(id)).Status);
        }

        Assert.Equal([15, 30, 60, 120, 240, 240, 240, 240], delays);
    }

    [Theory]
    [InlineData(405)]
    [InlineData(440)]
    [InlineData(450)]
    [InlineData(550)]
    public async Task HashLookup_SkipsFailedElementInCurrentSession_PostsAgain(int code)
    {
        var id = await SeedAsync(i => i.MarkSendStarted("sess-old", _time.UtcNow));
        _submitter.OnList = _ => [Status(code, elementRef: "el-failed")];
        _submitter.OnSend = () => Task.FromResult("el-new");

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Sending, invoice.Status);
        Assert.Equal("el-new", invoice.KsefElementRef);
        Assert.Equal(1, _submitter.Sends);
    }

    [Fact]
    public async Task HashLookup_FailedAndLiveElementInSession_TakesLiveOne_NoPost()
    {
        var id = await SeedAsync(i => i.MarkSendStarted("sess-old", _time.UtcNow));
        _submitter.OnList = _ => [Status(550, elementRef: "el-failed"), Status(150, elementRef: "el-live")];

        await RunAsync();

        Assert.Equal("el-live", (await LoadAsync(id)).KsefElementRef);
        Assert.Equal(0, _submitter.Sends);
    }

    [Fact]
    public async Task HeldStatus_ResendCycle_FailedElementListedInSession_NotTakenBack()
    {
        var id = await SeedResentAsync();
        _submitter.OnGet = _ => Status(550, elementRef: "el-2");
        await RunAsync();
        _time.Advance(TimeSpan.FromMinutes(15));
        _submitter.OnList = sessionRef => sessionRef == "sess-2" ? [Status(550, elementRef: "el-2")] : [];
        _submitter.OnSend = () => Task.FromResult("el-3");

        await RunAsync();

        Assert.Equal("el-3", (await LoadAsync(id)).KsefElementRef);
        Assert.Equal(3, _submitter.Sends);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task HeldStatus_SessionCannotBeListed_PostsEveryOtherCycle_ThenAccepts(HttpStatusCode code)
    {
        var id = await SeedResentAsync();
        _submitter.OnGet = _ => Status(550);
        await RunAsync();
        _submitter.OnList = _ => throw new KsefApiException("Brak sesji", code, null!, null!);

        for (var i = 0; i < 50; i++)
        {
            _time.Advance(TimeSpan.FromHours(4));
            await RunAsync();
        }

        var held = await LoadAsync(id);
        Assert.Equal(27, _submitter.Sends);
        Assert.Equal(27, held.AttemptCount);
        Assert.Equal(IssuedInvoiceStatus.Sending, held.Status);
        Assert.Null(held.KsefElementRef);
        Assert.Equal(IssuedInvoiceSendProcessor.DuplicateUnresolvedMessage, held.LastError);
        Assert.Equal(25, _logs.Messages.Count(m => m.Contains("cannot be listed: Brak sesji")));

        _submitter.OnGet = _ => Status(200, "KSEF-1");
        _time.Advance(TimeSpan.FromHours(4));
        await RunAsync();
        await RunAsync();

        var accepted = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Accepted, accepted.Status);
        Assert.Equal("KSEF-1", accepted.KsefNumber);
        Assert.Equal(28, _submitter.Sends);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task CrashBeforeElementRef_SessionCannotBeListed_PostsAgain(HttpStatusCode code)
    {
        var id = await SeedAsync(i => i.MarkSendStarted("sess-old", _time.UtcNow));
        _submitter.OnList = _ => throw new KsefApiException("Brak sesji", code, null!, null!);

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(1, _submitter.Sends);
        Assert.Equal(2, invoice.AttemptCount);
        Assert.Equal(("sess-1", "el-1"), (invoice.KsefSessionRef, invoice.KsefElementRef));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SessionListForbidden_NoPost_WaitsWithKsefText(bool held)
    {
        Guid id;
        if (held)
        {
            id = await SeedResentAsync();
            _submitter.OnGet = _ => Status(550);
            await RunAsync();
        }
        else
        {
            id = await SeedAsync(i => i.MarkSendStarted("sess-old", _time.UtcNow));
        }

        var sends = _submitter.Sends;
        _submitter.OnList = _ => throw new KsefApiException("not your session", HttpStatusCode.Forbidden, null!, null!);
        _time.Advance(TimeSpan.FromHours(4));

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(sends, _submitter.Sends);
        Assert.Equal(IssuedInvoiceStatus.Sending, invoice.Status);
        Assert.Equal("not your session", invoice.LastError);
    }

    [Fact]
    public async Task HeldStatus_LogsCodeAndDescription_NoToken()
    {
        var id = await SeedResentAsync();
        _submitter.OnGet = _ => Status(410);

        await RunAsync();

        var line = Assert.Single(_logs.Messages, m => m.Contains("failed with status"));
        Assert.Contains(id.ToString(), line);
        Assert.Contains("status 410", line);
        Assert.Contains("desc 410", line);
        Assert.Contains("session sess-2", line);
        Assert.Contains("element el-2", line);
        Assert.DoesNotContain(Token, line);
        Assert.DoesNotContain(AccessToken, line);
    }

    [Theory]
    [InlineData(430)]
    [InlineData(450)]
    public async Task ContentStatus_AfterResend_Rejected(int code)
    {
        var id = await SeedResentAsync();
        _submitter.OnGet = _ => Status(code);

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Rejected, invoice.Status);
        Assert.Equal(code, invoice.KsefStatusCode);
    }

    [Theory]
    [InlineData(405)]
    [InlineData(410)]
    [InlineData(415)]
    [InlineData(430)]
    [InlineData(435)]
    [InlineData(450)]
    [InlineData(500)]
    [InlineData(550)]
    public async Task FailureStatus_FirstAttempt_Rejected(int code)
    {
        var id = await SeedAsync(Sent);
        _submitter.OnGet = _ => Status(code);

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Rejected, invoice.Status);
        Assert.Equal(code, invoice.KsefStatusCode);
    }

    [Fact]
    public async Task Duplicate440_Rejected_StoresOriginalKsefNumber_ExposedAsDuplicateNumber()
    {
        const string original = "5260250274-20261103-ABCDEF123456-AB";
        var id = await SeedAsync(Sent);
        _submitter.OnGet = _ => Duplicate(original);

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal($"Duplikat faktury; Numer KSeF: {original}", invoice.KsefStatusDetails);
        Assert.Equal(original, invoice.DuplicateKsefNumber);
    }

    [Fact]
    public async Task Duplicate440_NumberAlreadyInDetails_NotRepeated()
    {
        const string original = "5260250274-20261103-ABCDEF123456-AB";
        var id = await SeedAsync(Sent);
        _submitter.OnGet = _ => Status(KSeFSentInvoice.Duplicate, details: $"Duplikat faktury o numerze KSeF: {original}") with { OriginalKsefNumber = original };

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal($"Duplikat faktury o numerze KSeF: {original}", invoice.KsefStatusDetails);
        Assert.Equal(original, invoice.DuplicateKsefNumber);
    }

    [Fact]
    public async Task Http401OnLogin_RetriedNotRejected()
    {
        var id = await SeedAsync();
        _submitter.OnLogin = () => throw new KsefApiException("unauthorized", HttpStatusCode.Unauthorized, null!, null!);

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Queued, invoice.Status);
        Assert.Equal(IssuedInvoiceSendProcessor.UnavailableMessage, invoice.LastError);
    }

    [Fact]
    public async Task Submitter_Http401OnLogin_StaysKsefApiException()
    {
        var submitter = new KSeFInvoiceSubmitter(DispatchProxy.Create<IKSeFClient, UnauthorizedClient>(), null!, _encryption);
        var credential = new KSeFCredential { Type = CredentialType.Token, EncryptedToken = _encryption.Encrypt("token") };

        var error = await Assert.ThrowsAsync<KsefApiException>(() => submitter.LoginAsync("5260250274", credential));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task Duplicate440_OriginalInEarlierSession_FoundAfterSessionRefWasOverwritten()
    {
        var id = await SeedAsync(i => i.MarkSendStarted("sess-early", _time.UtcNow));
        _submitter.OnList = _ => [];
        _submitter.OnSend = () => throw new HttpRequestException("response lost");
        await RunAsync();
        Assert.Equal("sess-1", (await LoadAsync(id)).KsefSessionRef);

        _time.Advance(TimeSpan.FromMinutes(1));
        _submitter.OnSend = () => Task.FromResult("el-dup");
        await RunAsync();
        Assert.Equal("sess-2", (await LoadAsync(id)).KsefSessionRef);

        _time.Advance(TimeSpan.FromMinutes(1));
        _submitter.OnGet = _ => Duplicate(originalSession: "sess-early");
        _submitter.OnList = sessionRef => sessionRef == "sess-early" ? [Status(200, "KSEF-ORIG", elementRef: "el-orig")] : [];
        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Accepted, invoice.Status);
        Assert.Equal("KSEF-ORIG", invoice.KsefNumber);
        Assert.Equal("sess-early", invoice.KsefSessionRef);
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

    [Theory]
    [InlineData(0, 3)]
    [InlineData(240, 60)]
    [InlineData(7200, 300)]
    public async Task Processing150_NextPollSlowsWithSendAge(int ageSeconds, int expectedDelaySeconds)
    {
        var id = await SeedAsync(i => { i.MarkSendStarted("sess-1", _time.UtcNow.AddSeconds(-ageSeconds)); i.RecordElementRef("el-1", _time.UtcNow); });
        _submitter.OnGet = _ => Status(150);

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Sending, invoice.Status);
        Assert.Null(invoice.LastError);
        Assert.Equal(_time.UtcNow.AddSeconds(expectedDelaySeconds), invoice.NextAttemptAt);
    }

    [Fact]
    public async Task Processing150_NotDueYet_NoLogin()
    {
        await SeedAsync(i => { i.MarkSendStarted("sess-1", _time.UtcNow); i.RecordElementRef("el-1", _time.UtcNow); });
        _submitter.OnGet = _ => Status(150);
        await RunAsync();

        await RunAsync();

        Assert.Equal(1, _submitter.Logins);
        Assert.Equal(1, _submitter.Gets);
    }

    [Fact]
    public async Task NothingDue_NoLoginNoKSeFCall()
    {
        var id = await SeedAsync();
        _submitter.OnSend = () => throw new KsefRateLimitException("429", 120, null, null!);
        await RunAsync();
        _submitter.Logins = _submitter.Opens = _submitter.Sends = 0;

        await RunAsync();

        Assert.Equal(0, _submitter.Logins);
        Assert.Equal(0, _submitter.Opens);
        Assert.Equal(0, _submitter.Sends);
        Assert.NotNull((await LoadAsync(id)).KsefSessionRef);
    }

    [Fact]
    public async Task NetworkTimeoutOnLogin_RetriedNotRejected()
    {
        var id = await SeedAsync();
        _submitter.OnLogin = () => throw new TimeoutException("Ostatni status: 100");

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Queued, invoice.Status);
        Assert.Equal(IssuedInvoiceSendProcessor.UnavailableMessage, invoice.LastError);
        Assert.Equal(_time.UtcNow.AddSeconds(10), invoice.NextAttemptAt);
    }

    [Fact]
    public async Task LoginOverTimeCap_RetriedNotRejected()
    {
        var id = await SeedAsync();
        _submitter.OnLogin = () => throw new OperationCanceledException();

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Queued, invoice.Status);
        Assert.Equal(IssuedInvoiceSendProcessor.UnavailableMessage, invoice.LastError);
        Assert.NotNull(invoice.NextAttemptAt);
    }

    [Fact]
    public async Task TokenRejected_UnsentInvoice_RejectedWithTokenMessage_NoRetry()
    {
        var id = await SeedAsync();
        _submitter.OnLogin = () => throw new KSeFAuthRejectedException(450, "Błędny token: Nieprawidłowy token");

        await RunAsync();
        _time.Advance(TimeSpan.FromHours(1));
        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Rejected, invoice.Status);
        Assert.Equal(450, invoice.KsefStatusCode);
        Assert.Equal(IssuedInvoiceSendProcessor.InvalidTokenMessage, invoice.KsefStatusDescription);
        Assert.Equal("Błędny token: Nieprawidłowy token", invoice.KsefStatusDetails);
        Assert.Null(invoice.NextAttemptAt);
        Assert.Equal(1, _submitter.Logins);
        Assert.Equal(0, _submitter.Opens);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TokenRejected_AlreadySentInvoice_StaysSendingWithTokenMessage_RecoversWithNewToken(bool withElementRef)
    {
        var id = await SeedAsync(i =>
        {
            i.MarkSendStarted("sess-1", _time.UtcNow);
            if (withElementRef)
                i.RecordElementRef("el-1", _time.UtcNow);
        });
        _submitter.OnLogin = () => throw new KSeFAuthRejectedException(450, "Nieprawidłowy token");

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Sending, invoice.Status);
        Assert.Equal($"{IssuedInvoiceSendProcessor.InvalidTokenMessage} Nieprawidłowy token", invoice.LastError);
        Assert.Null(invoice.KsefStatusCode);
        Assert.Equal(_time.UtcNow.AddMinutes(15), invoice.NextAttemptAt);

        _time.Advance(TimeSpan.FromMinutes(16));
        _submitter.OnLogin = () => { };
        _submitter.OnGet = _ => Status(200, "KSEF-1");
        _submitter.OnList = _ => [Status(200, "KSEF-1")];
        await RunAsync();

        Assert.Equal(IssuedInvoiceStatus.Accepted, (await LoadAsync(id)).Status);
        Assert.Equal(0, _submitter.Sends);
    }

    [Fact]
    public async Task TokenRejected_OneTenant_OtherTenantStillSent()
    {
        await SeedAsync();
        await SeedAsync(tenant: Guid.NewGuid());
        var logins = 0;
        _submitter.OnLogin = () =>
        {
            if (++logins == 1)
                throw new KSeFAuthRejectedException(450, "Nieprawidłowy token");
        };

        await RunAsync();

        Assert.Equal(2, logins);
        Assert.Equal(1, _submitter.Sends);
    }

    [Theory]
    [InlineData(415, true)]
    [InlineData(425, true)]
    [InlineData(450, true)]
    [InlineData(480, true)]
    [InlineData(100, false)]
    [InlineData(200, false)]
    [InlineData(500, false)]
    [InlineData(550, false)]
    public void AuthStatus_4xxIsFinal_OthersAreNot(int code, bool rejected)
    {
        var status = new OperationStatusInfo { Code = code, Description = "Opis", Details = ["Nieprawidłowy token"] };

        if (rejected)
            Assert.Equal("Opis: Nieprawidłowy token", Assert.Throws<KSeFAuthRejectedException>(() => KSeFInvoiceSubmitter.ThrowIfAuthRejected(status)).Message);
        else
            KSeFInvoiceSubmitter.ThrowIfAuthRejected(status);
    }

    [Fact]
    public async Task MissingToken_SentInvoice_StaysSending_NeverRejected()
    {
        var id = await SeedAsync(Sent, withSecret: false);

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Sending, invoice.Status);
        Assert.Equal(IssuedInvoiceSendProcessor.MissingTokenMessage, invoice.LastError);
        Assert.Equal("el-1", invoice.KsefElementRef);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ClientErrorOnLogin_SentInvoice_StaysSending(HttpStatusCode statusCode)
    {
        var id = await SeedAsync(Sent);
        _submitter.OnLogin = () => throw new KsefApiException("refused", statusCode, null!, null!);

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Sending, invoice.Status);
        Assert.Equal(_time.UtcNow.AddMinutes(15), invoice.NextAttemptAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClientErrorOnRetry_EarlierAttemptMayHaveReachedKSeF_StaysSending(bool failOnOpen)
    {
        var id = await SeedAsync(i => i.MarkSendStarted("sess-old", _time.UtcNow));
        _submitter.OnList = _ => [];
        if (failOnOpen)
            _submitter.OnOpen = () => throw new KsefApiException("bad", HttpStatusCode.BadRequest, null!, null!);
        else
            _submitter.OnSend = () => throw new KsefApiException("bad", HttpStatusCode.BadRequest, null!, null!);

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Sending, invoice.Status);
        Assert.Equal("bad", invoice.LastError);
        Assert.Equal(_time.UtcNow.AddMinutes(15), invoice.NextAttemptAt);
    }

    [Fact]
    public async Task ClientErrorOnFirstAttempt_RejectedBecauseNothingReachedKSeF()
    {
        var id = await SeedAsync();
        _submitter.OnSend = () => throw new KsefApiException("bad", HttpStatusCode.BadRequest, null!, null!);

        await RunAsync();

        Assert.Equal(IssuedInvoiceStatus.Rejected, (await LoadAsync(id)).Status);
    }

    [Fact]
    public async Task EnvironmentChanged_SentInvoice_StaysSendingNotRejected()
    {
        var id = await SeedAsync(Sent, environment: "test");

        await RunAsync("production");

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Sending, invoice.Status);
        Assert.Equal(IssuedInvoiceSendProcessor.EnvironmentChangedMessage, invoice.LastError);
        Assert.Equal(_time.UtcNow.AddDays(1), invoice.NextAttemptAt);
        Assert.Equal(0, _submitter.Logins);
    }

    [Fact]
    public async Task ForbiddenWithOtherReason_RejectedWithKSeFReason_NotPermissionAdvice()
    {
        var id = await SeedAsync();
        _submitter.OnOpen = () => throw new KsefApiException("ip-not-allowed: Adres IP niedozwolony", HttpStatusCode.Forbidden, null!, null!);

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Rejected, invoice.Status);
        Assert.Equal(403, invoice.KsefStatusCode);
        Assert.Equal("ip-not-allowed: Adres IP niedozwolony", invoice.KsefStatusDescription);
    }

    [Fact]
    public async Task OneTenantFails_OtherTenantStillSent()
    {
        await SeedAsync();
        await SeedAsync(tenant: Guid.NewGuid());
        _submitter.OnLogin = () =>
        {
            _submitter.OnLogin = () => { };
            throw new InvalidOperationException("poison");
        };

        await RunAsync();

        Assert.Equal(2, _submitter.Logins);
        Assert.Equal(1, _submitter.Sends);
    }

    [Fact]
    public void AddSyncServices_ResolvesProcessor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddSingleton<IEncryptionService>(_encryption);
        services.AddSyncServices(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IssuedInvoiceSendProcessor>());
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
        _submitter.OnOpen = () => throw new KsefApiException("missing-permissions: Brak uprawnień", HttpStatusCode.Forbidden, null!, null!);

        await RunAsync();
        _time.Advance(TimeSpan.FromHours(1));
        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Rejected, invoice.Status);
        Assert.Equal(403, invoice.KsefStatusCode);
        Assert.Equal(IssuedInvoiceSendProcessor.NoWritePermissionMessage, invoice.KsefStatusDescription);
        Assert.Null(invoice.NextAttemptAt);
        Assert.Equal(1, _submitter.Opens);
        Assert.Equal(0, _submitter.Sends);
    }

    [Fact]
    public async Task ForbiddenOnSend_RejectedWithPermissionMessage_NoRetry()
    {
        var id = await SeedAsync();
        _submitter.OnSend = () => throw new KsefApiException("missing-permissions: Brak uprawnień", HttpStatusCode.Forbidden, null!, null!);

        await RunAsync();
        _time.Advance(TimeSpan.FromHours(1));
        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Rejected, invoice.Status);
        Assert.Equal(IssuedInvoiceSendProcessor.NoWritePermissionMessage, invoice.KsefStatusDescription);
        Assert.Equal(1, _submitter.Sends);
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
    public async Task UnknownCurrentEnvironment_NothingSent_InvoiceUntouched()
    {
        var id = await SeedAsync();

        await RunAsync("tset");

        Assert.Equal(IssuedInvoiceStatus.Queued, (await LoadAsync(id)).Status);
        Assert.Equal((0, 0), (_submitter.Logins, _submitter.Sends));
        Assert.Contains(_logs.Messages, m => m.Contains("Unknown KSeF environment 'tset'"));
    }

    [Fact]
    public async Task UnknownInvoiceEnvironment_NothingSentForThatTenant_OtherTenantSends()
    {
        var bad = await SeedAsync(environment: "tset");
        var good = await SeedAsync(tenant: Guid.NewGuid());

        await RunAsync();

        Assert.Equal(IssuedInvoiceStatus.Queued, (await LoadAsync(bad)).Status);
        Assert.NotEqual(IssuedInvoiceStatus.Queued, (await LoadAsync(good)).Status);
        Assert.Equal(1, _submitter.Sends);
        Assert.Contains(_logs.Messages, m => m.Contains("Unknown KSeF environment 'tset'"));
    }

    [Fact]
    public async Task HungSendAndClose_OtherTenantSendsWithinBudgetPlusCloseCap_HungInvoiceResumesWithoutSecondPost()
    {
        var first = await SeedAsync();
        var second = await SeedAsync(tenant: Guid.NewGuid());
        var third = await SeedAsync(tenant: Guid.NewGuid());
        var hangStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _submitter.OnClose = () =>
        {
            if (_submitter.Closes > 1)
                return Task.CompletedTask;
            closeStarted.SetResult();
            return new TaskCompletionSource().Task;
        };
        _submitter.OnSend = () =>
        {
            if (_submitter.Sends > 1)
                return Task.FromResult("el-ok");
            hangStarted.SetResult();
            return new TaskCompletionSource<string>().Task;
        };

        var run = RunAsync();
        await hangStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _time.Advance(IssuedInvoiceSendProcessor.TenantBudget - TimeSpan.FromSeconds(1));
        Assert.False(run.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(1));
        await closeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _time.Advance(IssuedInvoiceSendProcessor.CloseCap - TimeSpan.FromSeconds(1));
        Assert.False(run.IsCompleted);
        Assert.Equal(1, _submitter.Sends);
        _time.Advance(TimeSpan.FromSeconds(1));
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        var invoices = new[] { await LoadAsync(first), await LoadAsync(second), await LoadAsync(third) };
        Assert.All(invoices.Where(i => i.KsefElementRef == "el-ok"), i => Assert.Equal(IssuedInvoiceStatus.Sending, i.Status));
        Assert.Equal(2, invoices.Count(i => i.KsefElementRef == "el-ok"));
        var hung = Assert.Single(invoices, i => i.KsefElementRef is null);
        Assert.Equal((IssuedInvoiceStatus.Sending, "sess-1", 1), (hung.Status, hung.KsefSessionRef, hung.AttemptCount));
        Assert.Equal(IssuedInvoiceSendProcessor.UnavailableMessage, hung.LastError);
        Assert.Equal(hung.UpdatedAt.AddSeconds(10), hung.NextAttemptAt);
        Assert.Equal(3, _submitter.Sends);
        Assert.Equal((3, 3), (_submitter.Opens, _submitter.Closes));

        _time.Advance(TimeSpan.FromMinutes(1));
        _submitter.OnList = _ => [Status(KSeFSentInvoice.Accepted, "KSEF-1", elementRef: "el-hung")];
        await RunAsync();

        var resumed = await LoadAsync(hung.Id);
        Assert.Equal((IssuedInvoiceStatus.Accepted, "el-hung", "KSEF-1"), (resumed.Status, resumed.KsefElementRef, resumed.KsefNumber));
        Assert.Equal(3, _submitter.Sends);
    }

    [Fact]
    public async Task LegacyInvoiceEnvironment_MatchesCurrentAndSends()
    {
        var id = await SeedAsync(environment: "https://api.ksef.mf.gov.pl/v2");

        await RunAsync("production");

        Assert.NotEqual(IssuedInvoiceStatus.Rejected, (await LoadAsync(id)).Status);
        Assert.Equal(1, _submitter.Sends);
    }

    [Fact]
    public async Task CertificateTenant_LogsInWithCertificate_AndSends()
    {
        var id = await SeedAsync(credentialType: CredentialType.Certificate);

        await RunAsync();

        Assert.Equal(CredentialType.Certificate, _submitter.Credential?.Type);
        Assert.Equal(1, _submitter.Sends);
        Assert.Equal("el-1", (await LoadAsync(id)).KsefElementRef);
    }

    [Fact]
    public async Task CertificateWithoutData_RejectedWithMissingCredentialMessage_NoLogin()
    {
        var id = await SeedAsync(credentialType: CredentialType.Certificate, withSecret: false);

        await RunAsync();

        Assert.Equal(IssuedInvoiceSendProcessor.MissingTokenMessage, (await LoadAsync(id)).KsefStatusDescription);
        Assert.Equal(0, _submitter.Logins);
    }

    [Fact]
    public async Task UnreadableStoredCertificate_UnsentRejected_SentStaysSending_NoSend()
    {
        var unsent = await SeedAsync(credentialType: CredentialType.Certificate);
        var sent = await SeedAsync(Sent, tenant: _tenantId, addTenant: false);
        _submitter.OnLogin = () => throw new StoredCertificateUnreadableException(new CryptographicException("The certificate data cannot be read with the provided password"));

        await RunAsync();

        Assert.Equal(IssuedInvoiceStatus.Rejected, (await LoadAsync(unsent)).Status);
        Assert.Equal(IssuedInvoiceSendProcessor.InvalidCertificateMessage, (await LoadAsync(unsent)).KsefStatusDescription);
        Assert.Equal(IssuedInvoiceStatus.Sending, (await LoadAsync(sent)).Status);
        Assert.Equal(IssuedInvoiceSendProcessor.InvalidCertificateMessage, (await LoadAsync(sent)).LastError);
        Assert.Equal(0, _submitter.Sends);
    }

    [Theory]
    [InlineData(CredentialType.Token)]
    [InlineData(CredentialType.Certificate)]
    public async Task StoredCredentialDecryptFails_UnsentStaysQueued_NotRejected(CredentialType credentialType)
    {
        var id = await SeedAsync(credentialType: credentialType);
        _submitter.OnLogin = () => throw new AuthenticationTagMismatchException();

        await RunAsync();

        var invoice = await LoadAsync(id);
        Assert.Equal(IssuedInvoiceStatus.Queued, invoice.Status);
        Assert.Null(invoice.KsefStatusDescription);
        Assert.Equal(0, _submitter.Sends);
    }

    [Theory]
    [InlineData(CredentialType.Certificate, 460, "Uwierzytelnianie zakończone niepowodzeniem z powodu błędu certyfikatu: Certyfikat odwołany", IssuedInvoiceSendProcessor.InvalidCertificateMessage)]
    [InlineData(CredentialType.Certificate, 415, "Uwierzytelnianie zakończone niepowodzeniem: Brak przypisanych uprawnień", IssuedInvoiceSendProcessor.NoWritePermissionMessage)]
    [InlineData(CredentialType.Certificate, 480, "Uwierzytelnienie zablokowane: Podejrzenie incydentu bezpieczeństwa.", IssuedInvoiceSendProcessor.LoginRefusedMessage)]
    [InlineData(CredentialType.Token, 450, "Uwierzytelnianie zakończone niepowodzeniem z powodu błędnego tokenu: Token unieważniony", IssuedInvoiceSendProcessor.InvalidTokenMessage)]
    [InlineData(CredentialType.Token, 450, "Uwierzytelnianie zakończone niepowodzeniem z powodu błędnego tokenu: Token nie może być użyty w kontekście 1111111111", IssuedInvoiceSendProcessor.InvalidTokenMessage)]
    [InlineData(CredentialType.Token, 450, "Uwierzytelnianie zakończone niepowodzeniem z powodu błędnego tokenu: Token o numerze referencyjnym 20261113-EC-0000000000-0000000000-00 nie został znaleziony.", IssuedInvoiceSendProcessor.InvalidTokenMessage)]
    [InlineData(CredentialType.Token, 450, "Uwierzytelnianie zakończone niepowodzeniem z powodu błędnego tokenu: Nieprawidłowy czas tokena", IssuedInvoiceSendProcessor.LoginRefusedMessage)]
    [InlineData(CredentialType.Token, 450, "Uwierzytelnianie zakończone niepowodzeniem z powodu błędnego tokenu: Nieprawidłowe wyzwanie autoryzacyjne", IssuedInvoiceSendProcessor.LoginRefusedMessage)]
    [InlineData(CredentialType.Token, 450, "Uwierzytelnianie zakończone niepowodzeniem z powodu błędnego tokenu: Nieprawidłowe szyfrowanie tokena", IssuedInvoiceSendProcessor.LoginRefusedMessage)]
    [InlineData(CredentialType.Token, 415, "Uwierzytelnianie zakończone niepowodzeniem: Brak przypisanych uprawnień", IssuedInvoiceSendProcessor.NoWritePermissionMessage)]
    [InlineData(CredentialType.Token, 425, "Uwierzytelnienie unieważnione", IssuedInvoiceSendProcessor.LoginRefusedMessage)]
    [InlineData(CredentialType.Token, 480, "Uwierzytelnienie zablokowane: Podejrzenie incydentu bezpieczeństwa.", IssuedInvoiceSendProcessor.LoginRefusedMessage)]
    public async Task AuthRejected_MessageFitsCause_UnsentRejectedWithDetails_SentStaysSending(CredentialType credentialType, int code, string reason, string expected)
    {
        var unsent = await SeedAsync(credentialType: credentialType);
        var sent = await SeedAsync(Sent, tenant: _tenantId, addTenant: false);
        _submitter.OnLogin = () => throw new KSeFAuthRejectedException(code, reason);

        await RunAsync();

        var rejected = await LoadAsync(unsent);
        Assert.Equal(IssuedInvoiceStatus.Rejected, rejected.Status);
        Assert.Equal(code, rejected.KsefStatusCode);
        Assert.Equal(expected, rejected.KsefStatusDescription);
        Assert.Equal(reason, rejected.KsefStatusDetails);
        var sending = await LoadAsync(sent);
        Assert.Equal(IssuedInvoiceStatus.Sending, sending.Status);
        Assert.Equal($"{expected} {reason}", sending.LastError);
        Assert.NotNull(sending.NextAttemptAt);
        Assert.Equal(0, _submitter.Sends);
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

    public class UnauthorizedClient : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new KsefApiException("unauthorized", HttpStatusCode.Unauthorized, null!, null!);
    }

    private sealed class FakeSubmitter : IKSeFInvoiceSubmitter
    {
        public Action OnLogin = () => { };
        public Action OnOpen = () => { };
        public Func<Task<string>> OnSend = () => Task.FromResult("el-1");
        public Func<string, KSeFSentInvoice?> OnGet = _ => null;
        public Func<string, IReadOnlyList<KSeFSentInvoice>> OnList = _ => [];
        public Func<byte[]?> OnUpo = () => null;
        public Func<Task> OnClose = () => Task.CompletedTask;
        public int Logins, Opens, Sends, Gets, Closes;
        public List<(string SessionRef, string ElementRef)> UpoRequests = [];
        public List<byte[]> SentBytes = [];
        public KSeFCredential? Credential;

        public Task<string> LoginAsync(string nip, KSeFCredential credential, CancellationToken ct = default)
        {
            Logins++;
            Credential = credential;
            OnLogin();
            return Task.FromResult(AccessToken);
        }

        public Task<KSeFOnlineSession> OpenSessionAsync(string accessToken, CancellationToken ct = default)
        {
            Opens++;
            OnOpen();
            return Task.FromResult(new KSeFOnlineSession($"sess-{Opens}", accessToken, [1], [2]));
        }

        public Task<string> SendAsync(KSeFOnlineSession session, byte[] invoiceXml, CancellationToken ct = default)
        {
            Sends++;
            SentBytes.Add(invoiceXml);
            Assert.Equal(Xml, invoiceXml);
            return OnSend().WaitAsync(ct);
        }

        public Task<KSeFSentInvoice?> GetInvoiceAsync(string accessToken, string sessionRef, string elementRef, CancellationToken ct = default)
        {
            Gets++;
            return Task.FromResult(OnGet(elementRef));
        }

        public Task<IReadOnlyList<KSeFSentInvoice>> ListInvoicesAsync(string accessToken, string sessionRef, CancellationToken ct = default) =>
            Task.FromResult(OnList(sessionRef));

        public Task<byte[]?> GetUpoAsync(string accessToken, string sessionRef, string elementRef, CancellationToken ct = default)
        {
            UpoRequests.Add((sessionRef, elementRef));
            return Task.FromResult(OnUpo());
        }

        public Task CloseSessionAsync(KSeFOnlineSession session, CancellationToken ct = default)
        {
            Closes++;
            return OnClose().WaitAsync(ct);
        }
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
