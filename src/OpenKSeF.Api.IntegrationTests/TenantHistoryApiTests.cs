using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public sealed class TenantHistoryApiTests : IAsyncLifetime
{
    private static readonly DateTime Base = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _owner;
    private Guid _tenantId;
    private Guid _otherTenantId;

    public TenantHistoryApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture);
        _owner = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        _owner.SetBearerToken(await _fixture.GetAccessTokenAsync());
        _tenantId = await CreateTenantAsync();
        _otherTenantId = await CreateTenantAsync();
    }

    public Task DisposeAsync()
    {
        _owner.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task OneHundredTwentyEvents_PagesOf50_50_20_NewestFirst_TiesStable()
    {
        var seeded = Enumerable.Range(0, 120)
            .Select(i => Event(_tenantId, AuditEvent.CredentialDeleted, Base.AddMinutes(i / 3)))
            .ToList();
        await SeedAsync(seeded);

        var pages = new List<TenantHistoryPageResponse>();
        Guid? before = null;
        do
        {
            pages.Add((await _owner.GetFromJsonAsync<TenantHistoryPageResponse>($"/api/tenants/{_tenantId}/history{(before is null ? "" : $"?before={before}")}"))!);
            before = pages[^1].Items[^1].Id;
        } while (pages[^1].HasMore);

        Assert.Equal([50, 50, 20], pages.Select(p => p.Items.Count));
        Assert.Equal([true, true, false], pages.Select(p => p.HasMore));
        var all = pages.SelectMany(p => p.Items).ToList();
        Assert.Equal(seeded.Select(e => e.Id).Order(), all.Select(e => e.Id).Order());
        Assert.Equal(all.OrderByDescending(e => e.OccurredAt).Select(e => e.OccurredAt), all.Select(e => e.OccurredAt));
    }

    [Fact]
    public async Task TwoFactorResetsAndOtherCompanyRows_AreNotListed()
    {
        var own = Event(_tenantId, AuditEvent.CredentialDeleted, Base);
        await SeedAsync([
            own,
            Event(null, AuditEvent.TwoFactorReset, Base.AddMinutes(1)),
            Event(null, AuditEvent.TwoFactorResetPartial, Base.AddMinutes(2)),
            Event(_otherTenantId, AuditEvent.CredentialDeleted, Base.AddMinutes(3)),
        ]);

        var page = await HistoryAsync();

        Assert.Equal(own.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task Details_PerCode_ClaimAndUnknownWithoutDetails_DeletedActorFlagged()
    {
        var now = DateTime.UtcNow;
        var invoice = new InvoiceHeader
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, KSeFInvoiceNumber = $"HISTORY-{Guid.NewGuid():N}", KSeFReferenceNumber = "HISTORY",
            InvoiceNumber = "FV/7/10/2026", VendorName = "Dostawca Testowy", VendorNip = "1111111111", Direction = InvoiceDirection.Received,
            IssueDate = now, FirstSeenAt = now, LastUpdatedAt = now,
        };
        var targetUserId = $"history-target-{Guid.NewGuid():N}";
        await WithDbAsync(async db =>
        {
            db.InvoiceHeaders.Add(invoice);
            db.UserProfiles.Add(new UserProfile { UserId = targetUserId, Email = "jan@example.invalid", DisplayName = "Jan Testowy", UpdatedAt = now });
            return await db.SaveChangesAsync();
        });

        var paid = Event(_tenantId, AuditEvent.InvoicePaid, Base, invoice.Id.ToString());
        var paidGone = Event(_tenantId, AuditEvent.InvoiceUnpaid, Base.AddMinutes(1), Guid.NewGuid().ToString());
        var exported = Event(_tenantId, AuditEvent.InvoicesExported, Base.AddMinutes(2), "od=2026-10-01;do=2026-10-31;kierunki=sprzedaz,zakup;sprzedaz=12;zakup=30");
        var roleChanged = Event(_tenantId, AuditEvent.MemberRoleChanged, Base.AddMinutes(3), "Accountant->Administrator", targetUserId, "jan@example.invalid");
        var invited = Event(_tenantId, AuditEvent.InviteCreated, Base.AddMinutes(4), "Employee", targetEmail: "nowy@example.invalid");
        var claim = Event(_tenantId, AuditEvent.OwnershipClaimed, Base.AddMinutes(5), "CredentialsManage,InvoiceRead;auth:20261126-AU-SECRET", targetUserId, "jan@example.invalid");
        var unknown = Event(_tenantId, "future.event", Base.AddMinutes(6), "secret", targetUserId, "jan@example.invalid");
        claim.ActorUserId = "deleted";
        claim.ActorEmail = null;
        claim.ActorName = null;
        await SeedAsync([paid, paidGone, exported, roleChanged, invited, claim, unknown]);

        var items = (await HistoryAsync()).Items.ToDictionary(i => i.Id);

        Assert.Equal(("Anna Testowa", false, "FV/7/10/2026"), (items[paid.Id].Actor, items[paid.Id].ActorDeleted, items[paid.Id].Target));
        Assert.Null(items[paidGone.Id].Target);
        Assert.Equal(exported.Target, items[exported.Id].Target);
        Assert.Equal(("Jan Testowy", "jan@example.invalid", "Accountant->Administrator"),
            (items[roleChanged.Id].TargetName, items[roleChanged.Id].TargetEmail, items[roleChanged.Id].Target));
        Assert.Equal((null, "nowy@example.invalid", "Employee"), (items[invited.Id].TargetName, items[invited.Id].TargetEmail, items[invited.Id].Target));
        Assert.Equal(new TenantHistoryEventResponse(unknown.Id, items[unknown.Id].OccurredAt, "future.event", "Anna Testowa", false, null, null, null), items[unknown.Id]);

        var raw = await _owner.GetStringAsync($"/api/tenants/{_tenantId}/history");
        Assert.Contains(
            $$"""{"id":"{{claim.Id}}","occurredAt":"2026-10-01T08:05:00Z","action":"tenant.ownershipClaimed","actor":null,"actorDeleted":true,"targetName":null,"targetEmail":null,"target":null}""",
            raw);
        Assert.DoesNotContain("SECRET", raw);
        Assert.DoesNotContain("CredentialsManage", raw);
        Assert.DoesNotContain("\"secret\"", raw);
    }

    [Fact]
    public async Task UnknownCursor_ReturnsEmptyPage()
    {
        await SeedAsync([Event(_otherTenantId, AuditEvent.CredentialDeleted, Base)]);
        var foreign = await WithDbAsync(db => db.AuditEvents.Where(a => a.TenantId == _otherTenantId).Select(a => a.Id).FirstAsync());

        var page = await _owner.GetFromJsonAsync<TenantHistoryPageResponse>($"/api/tenants/{_tenantId}/history?before={foreign}");

        Assert.Equal((0, false), (page!.Items.Count, page.HasMore));
    }

    private static AuditEvent Event(Guid? tenantId, string action, DateTime at, string? target = null, string? targetUserId = null, string? targetEmail = null) => new()
    {
        Id = Guid.NewGuid(),
        OccurredAt = at,
        TenantId = tenantId,
        ActorUserId = "history-actor",
        ActorEmail = "anna@example.invalid",
        ActorName = "Anna Testowa",
        Action = action,
        Target = target,
        TargetUserId = targetUserId,
        TargetEmail = targetEmail,
    };

    private Task SeedAsync(IEnumerable<AuditEvent> events) => WithDbAsync(db =>
    {
        db.AuditEvents.AddRange(events);
        return db.SaveChangesAsync();
    });

    private async Task<TenantHistoryPageResponse> HistoryAsync() =>
        (await _owner.GetFromJsonAsync<TenantHistoryPageResponse>($"/api/tenants/{_tenantId}/history"))!;

    private async Task<Guid> CreateTenantAsync()
    {
        var response = await _owner.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), displayName = "History test" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private sealed record IdDto(Guid Id);
}
