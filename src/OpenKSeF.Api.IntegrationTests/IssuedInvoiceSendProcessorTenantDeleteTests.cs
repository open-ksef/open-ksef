using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync.Issuing;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

/// <summary>Postgres, not InMemory: the send processor relies on the 0-row UPDATE of a deleted invoice raising DbUpdateConcurrencyException.</summary>
public sealed class IssuedInvoiceSendProcessorTenantDeleteTests : IAsyncLifetime
{
    private const string ConcurrentSkipLog = "changed concurrently, skipped this cycle";
    private static readonly DateTime Now = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] EncryptionKey = RandomNumberGenerator.GetBytes(32);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly AesGcmEncryptionService _encryption = new(EncryptionKey);
    private readonly FixedTime _time = new();
    private readonly FakeSubmitter _submitter = new();
    private readonly ListLogger _logs = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private async Task<Guid> SeedTenantAsync(string nip)
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewContext();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            UserId = "user-1",
            Nip = nip,
            CreatedAt = Now,
            UpdatedAt = Now,
            KSeFCredentials =
            [
                new KSeFCredential
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Type = CredentialType.Token,
                    EncryptedToken = _encryption.Encrypt("ksef-token"),
                    CreatedAt = Now,
                    UpdatedAt = Now
                }
            ]
        });
        await db.SaveChangesAsync();
        return tenantId;
    }

    private async Task<Guid> SeedInvoiceAsync(Guid tenantId, string number, DateTime dueAt, Action<IssuedInvoice>? arrange = null)
    {
        var invoice = IssuedInvoice.Draft(Guid.NewGuid(), tenantId, DateOnly.FromDateTime(Now), dueAt);
        invoice.SetBuyer(BuyerKind.Company, "5261040828", "Nabywca", "ul. Długa 2", "00-002 Warszawa");
        invoice.AddLine("Usługa", null, 1, 100m, VatRate.Rate23);
        var profile = new SellerProfile(tenantId);
        profile.Update("Sprzedawca", "ul. Prosta 1", "00-001 Warszawa", "61109010140000071219812874", false, InvoiceNumbering.DefaultPattern);
        invoice.Issue(profile, "5260250274", number, dueAt);
        var xml = Encoding.UTF8.GetBytes(number);
        invoice.AttachSentXml(xml, Convert.ToHexString(SHA256.HashData(xml)), "test", null);
        arrange?.Invoke(invoice);

        await using var db = NewContext();
        db.IssuedInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    private static void AwaitingUpo(IssuedInvoice invoice, string elementRef, DateTime dueAt)
    {
        invoice.MarkSendStarted("sess-old", dueAt);
        invoice.RecordElementRef(elementRef, dueAt);
        invoice.MarkAccepted($"KSEF-{elementRef}", dueAt, 200, "Sukces", dueAt);
    }

    private async Task RunAsync()
    {
        await using var db = NewContext();
        var processor = new IssuedInvoiceSendProcessor(db, _submitter, _time, _logs);
        await processor.ProcessAsync("test", CancellationToken.None);
    }

    private async Task DeleteTenantAsync(Guid tenantId)
    {
        await using var db = NewContext();
        var service = new IssuedInvoiceService(db, null!, null!, null!, null!, _time);
        Assert.True(await service.DeleteTenantAsync(tenantId));
    }

    private async Task AssertDeletedAsync(Guid tenantId, params Guid[] invoiceIds)
    {
        await using var db = NewContext();
        Assert.False(await db.Tenants.AnyAsync(t => t.Id == tenantId));
        Assert.False(await db.IssuedInvoices.AnyAsync(i => i.TenantId == tenantId || invoiceIds.Contains(i.Id)));
    }

    private void AssertSkippedConcurrently(params Guid[] invoiceIds)
    {
        var skipped = _logs.Entries.Where(e => e.Message.Contains(ConcurrentSkipLog)).ToList();
        Assert.Equal(invoiceIds.Length, skipped.Count);
        Assert.All(invoiceIds, id => Assert.Contains(skipped, e => e.Level == LogLevel.Warning && e.Message.Contains(id.ToString())));
        Assert.DoesNotContain(_logs.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task DueInvoices_TenantDeletedBeforeSendStartedSaved_NoPost_CycleContinues()
    {
        var doomedTenant = await SeedTenantAsync("1234563218");
        var first = await SeedInvoiceAsync(doomedTenant, "FV/1", Now.AddMinutes(-5));
        var second = await SeedInvoiceAsync(doomedTenant, "FV/2", Now.AddMinutes(-4));
        var otherTenant = await SeedTenantAsync("5261040828");
        var other = await SeedInvoiceAsync(otherTenant, "FV/3", Now.AddMinutes(-1));

        _submitter.BeforeNextOpen = async () =>
        {
            // DeleteTenantAsync refuses Queued, so the loaded rows must first have left that status in another process.
            await using (var db = NewContext())
            {
                foreach (var invoice in await db.IssuedInvoices.Where(i => i.TenantId == doomedTenant).ToListAsync())
                    invoice.MarkRejected(450, "Rejected elsewhere", null, Now);
                await db.SaveChangesAsync();
            }

            await DeleteTenantAsync(doomedTenant);
        };

        await RunAsync();

        Assert.Equal(["FV/3"], _submitter.Posted);
        AssertSkippedConcurrently(first, second);
        await AssertDeletedAsync(doomedTenant, first, second);

        await using var check = NewContext();
        var sent = await check.IssuedInvoices.SingleAsync(i => i.Id == other);
        Assert.Equal((IssuedInvoiceStatus.Sending, "el-1"), (sent.Status, sent.KsefElementRef));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AcceptedAwaitingUpo_TenantDeleted_SaveDoesNotFailCycle_RowNotRecreated(bool upoAvailable)
    {
        var doomedTenant = await SeedTenantAsync("1234563218");
        var doomedDueAt = Now.AddMinutes(-5);
        var doomed = await SeedInvoiceAsync(doomedTenant, "FV/1", doomedDueAt, i => AwaitingUpo(i, "el-doomed", doomedDueAt));
        var otherTenant = await SeedTenantAsync("5261040828");
        var otherDueAt = Now.AddMinutes(-1);
        var other = await SeedInvoiceAsync(otherTenant, "FV/2", otherDueAt, i => AwaitingUpo(i, "el-other", otherDueAt));

        _submitter.OnUpo = async elementRef =>
        {
            if (elementRef != "el-doomed")
                return Encoding.UTF8.GetBytes("<UPO/>");

            await DeleteTenantAsync(doomedTenant);
            return upoAvailable ? Encoding.UTF8.GetBytes("<UPO/>") : null;
        };

        await RunAsync();

        AssertSkippedConcurrently(doomed);
        await AssertDeletedAsync(doomedTenant, doomed);

        await using var check = NewContext();
        Assert.NotNull((await check.IssuedInvoices.SingleAsync(i => i.Id == other)).UpoXml);
    }

    private sealed class FakeSubmitter : IKSeFInvoiceSubmitter
    {
        public Func<Task>? BeforeNextOpen;
        public Func<string, Task<byte[]?>> OnUpo = _ => Task.FromResult<byte[]?>(null);
        public List<string> Posted { get; } = [];

        public Task<string> LoginAsync(string nip, KSeFCredential credential, CancellationToken ct = default) => Task.FromResult("access-token");

        public async Task<KSeFOnlineSession> OpenSessionAsync(string accessToken, CancellationToken ct = default)
        {
            var hook = BeforeNextOpen;
            BeforeNextOpen = null;
            if (hook is not null)
                await hook();
            return new KSeFOnlineSession("sess-1", accessToken, [1], [2]);
        }

        public Task<string> SendAsync(KSeFOnlineSession session, byte[] invoiceXml, CancellationToken ct = default)
        {
            Posted.Add(Encoding.UTF8.GetString(invoiceXml));
            return Task.FromResult($"el-{Posted.Count}");
        }

        public Task<KSeFSentInvoice?> GetInvoiceAsync(string accessToken, string sessionRef, string elementRef, CancellationToken ct = default) =>
            Task.FromResult<KSeFSentInvoice?>(null);

        public Task<IReadOnlyList<KSeFSentInvoice>> ListInvoicesAsync(string accessToken, string sessionRef, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<KSeFSentInvoice>>([]);

        public Task<byte[]?> GetUpoAsync(string accessToken, string sessionRef, string elementRef, CancellationToken ct = default) => OnUpo(elementRef);

        public Task CloseSessionAsync(KSeFOnlineSession session, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ListLogger : ILogger<IssuedInvoiceSendProcessor>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
