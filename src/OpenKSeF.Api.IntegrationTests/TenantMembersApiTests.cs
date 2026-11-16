using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.Authorization;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public sealed class TenantMembersApiTests : IAsyncLifetime
{
    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _owner;
    private readonly List<HttpClient> _clients = [];
    private Guid _tenantId;
    private string _ownerId = null!;

    public TenantMembersApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture);
        _owner = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        _owner.SetBearerToken(await _fixture.GetAccessTokenAsync());
        var response = await _owner.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), displayName = "Members test" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        _tenantId = (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
        await WithDbAsync(async db => _ownerId = (await db.Tenants.SingleAsync(t => t.Id == _tenantId)).UserId);
    }

    public Task DisposeAsync()
    {
        _owner.Dispose();
        _clients.ForEach(c => c.Dispose());
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task LastOwner_CannotLeaveOrBeDemoted()
    {
        var leave = await _owner.DeleteAsync(Members(_ownerId));
        var demote = await _owner.PutAsJsonAsync(Members(_ownerId), new { role = "Administrator" });

        await AssertErrorAsync(leave, HttpStatusCode.Conflict, TenantMembershipService.LastOwnerMessage);
        await AssertErrorAsync(demote, HttpStatusCode.Conflict, TenantMembershipService.LastOwnerMessage);
        await WithDbAsync(async db =>
        {
            Assert.Equal(TenantRole.Owner, (await db.TenantMembers.SingleAsync(m => m.TenantId == _tenantId)).Role);
            Assert.False(await db.AuditEvents.AnyAsync(a => a.TenantId == _tenantId));
        });
    }

    [Fact]
    public async Task Administrator_ForbiddenOnOwner_ManagesOtherRoles()
    {
        var (_, admin) = await AddMemberAsync(TenantRole.Administrator);
        var (employeeId, _) = await AddMemberAsync(TenantRole.Employee);

        var members = (await admin.GetFromJsonAsync<List<MemberDto>>($"/api/tenants/{_tenantId}/members"))!;
        Assert.Equal(["Owner", "Administrator", "Employee"], members.Select(m => m.Role));
        Assert.Equal("Test User", members.Single(m => m.UserId == employeeId).DisplayName);

        await AssertErrorAsync(await admin.PutAsJsonAsync(Members(_ownerId), new { role = "Employee" }), HttpStatusCode.Forbidden, TenantAccessAttribute.ForbiddenMessage);
        await AssertErrorAsync(await admin.DeleteAsync(Members(_ownerId)), HttpStatusCode.Forbidden, TenantAccessAttribute.ForbiddenMessage);
        await AssertErrorAsync(await admin.PutAsJsonAsync(Members(employeeId), new { role = "Owner" }), HttpStatusCode.Forbidden, TenantAccessAttribute.ForbiddenMessage);
        var unknownRole = await admin.PutAsJsonAsync(Members(employeeId), new { role = "Boss" });
        Assert.Equal(HttpStatusCode.BadRequest, unknownRole.StatusCode);
        Assert.Contains("Nieznana rola.", await unknownRole.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(Members(employeeId), new { role = "Accountant" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(Members(employeeId))).StatusCode);

        await WithDbAsync(async db =>
        {
            Assert.Equal(TenantRole.Owner, (await db.TenantMembers.SingleAsync(m => m.TenantId == _tenantId && m.UserId == _ownerId)).Role);
            Assert.False(await db.TenantMembers.AnyAsync(m => m.TenantId == _tenantId && m.UserId == employeeId));
            var audit = await db.AuditEvents.Where(a => a.TenantId == _tenantId).OrderBy(a => a.OccurredAt).ToListAsync();
            Assert.Equal(
                [(AuditEvent.MemberRoleChanged, "Employee->Accountant"), (AuditEvent.MemberRemoved, "Accountant")],
                audit.Select(a => (a.Action, a.Target)));
            Assert.All(audit, a => Assert.Equal((employeeId, "Test User"), (a.TargetUserId, a.ActorName)));
            Assert.All(audit, a => Assert.NotNull(a.TargetEmail));
        });

        var (leaverId, leaver) = await AddMemberAsync(TenantRole.Employee);
        var adminId = members.Single(m => m.Role == "Administrator").UserId;
        await AssertErrorAsync(await leaver.DeleteAsync(Members(adminId)), HttpStatusCode.Forbidden, TenantAccessAttribute.ForbiddenMessage);
        Assert.Equal(HttpStatusCode.NoContent, (await leaver.DeleteAsync(Members(leaverId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await leaver.GetAsync($"/api/tenants/{_tenantId}")).StatusCode);
    }

    [Fact]
    public async Task OwnerLeaves_GetsNotFound_PaidMarkAndAuditKeepName()
    {
        var (coOwnerId, coOwner) = await AddMemberAsync(TenantRole.Owner);
        var invoiceId = await SeedInvoiceAsync();
        Assert.Equal(HttpStatusCode.OK, (await coOwner.PatchAsJsonAsync($"/api/tenants/{_tenantId}/invoices/{invoiceId}/paid", new { isPaid = true })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await coOwner.DeleteAsync(Members(coOwnerId))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await coOwner.GetAsync($"/api/tenants/{_tenantId}")).StatusCode);
        var invoice = (await _owner.GetFromJsonAsync<PaidDto>($"/api/tenants/{_tenantId}/invoices/{invoiceId}"))!;
        Assert.Equal((true, "Test User"), (invoice.IsPaid, invoice.PaidByName));
        await WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.Where(a => a.TenantId == _tenantId).OrderBy(a => a.OccurredAt).ToListAsync();
            Assert.Equal([AuditEvent.InvoicePaid, AuditEvent.MemberLeft], audit.Select(a => a.Action));
            Assert.All(audit, a => Assert.Equal((coOwnerId, "Test User"), (a.ActorUserId, a.ActorName)));
            Assert.Equal((coOwnerId, "Owner"), (audit[1].TargetUserId, audit[1].Target));
        });
        await AssertErrorAsync(await _owner.DeleteAsync(Members(_ownerId)), HttpStatusCode.Conflict, TenantMembershipService.LastOwnerMessage);
    }

    [Fact]
    public async Task ConcurrentLeave_OfTheOnlyTwoOwners_OneSucceeds()
    {
        var coOwnerId = Guid.NewGuid().ToString();
        await WithDbAsync(async db =>
        {
            db.TenantMembers.Add(new TenantMember { TenantId = _tenantId, UserId = coOwnerId, Role = TenantRole.Owner, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        using var blockerScope = _factory.Services.CreateScope();
        var blocker = blockerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var hold = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlAsync($"""SELECT 1 FROM "TenantMembers" WHERE "TenantId" = {_tenantId} FOR UPDATE""");

        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();
        var leaveA = scopeA.ServiceProvider.GetRequiredService<TenantMembershipService>().RemoveAsync(_tenantId, _ownerId, _ownerId);
        var leaveB = scopeB.ServiceProvider.GetRequiredService<TenantMembershipService>().RemoveAsync(_tenantId, coOwnerId, coOwnerId);
        using (var probe = _factory.Services.CreateScope())
            await WaitForLockWaitersAsync(probe.ServiceProvider.GetRequiredService<ApplicationDbContext>(), 2);
        await hold.RollbackAsync();

        var outcomes = await Task.WhenAll(leaveA, leaveB);

        Assert.Equal([TenantMembershipService.Outcome.Done, TenantMembershipService.Outcome.LastOwner], outcomes.Order());
        await WithDbAsync(async db => Assert.Equal(1, await db.TenantMembers.CountAsync(m => m.TenantId == _tenantId && m.Role == TenantRole.Owner)));
    }

    private static async Task WaitForLockWaitersAsync(ApplicationDbContext db, int count)
    {
        for (var i = 0; i < 100; i++)
        {
            var waiting = await db.Database.SqlQuery<int>(
                $"""SELECT count(*)::int AS "Value" FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND datname = current_database()""").SingleAsync();
            if (waiting >= count)
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException($"Fewer than {count} sessions waiting on a lock");
    }

    private string Members(string userId) => $"/api/tenants/{_tenantId}/members/{userId}";

    private async Task<(string UserId, HttpClient Client)> AddMemberAsync(TenantRole role)
    {
        var email = $"members-{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@openksef.test";
        var userId = await _fixture.CreateUserAsync(email);
        await WithDbAsync(async db =>
        {
            db.TenantMembers.Add(new TenantMember { TenantId = _tenantId, UserId = userId, Role = role, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        var client = _factory.CreateClient();
        client.SetBearerToken(await _fixture.GetAccessTokenAsync(email));
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"203.0.113.{40 + _clients.Count}");
        _clients.Add(client);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/tenants/{_tenantId}")).StatusCode);
        return (userId, client);
    }

    private async Task<Guid> SeedInvoiceAsync()
    {
        var now = DateTime.UtcNow;
        var invoice = new InvoiceHeader
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            KSeFInvoiceNumber = $"MEMBERS-{Guid.NewGuid():N}",
            KSeFReferenceNumber = "MEMBERS",
            VendorName = "Dostawca Testowy",
            VendorNip = "1111111111",
            Direction = InvoiceDirection.Received,
            IssueDate = now,
            FirstSeenAt = now,
            LastUpdatedAt = now,
        };
        await WithDbAsync(async db =>
        {
            db.InvoiceHeaders.Add(invoice);
            await db.SaveChangesAsync();
        });
        return invoice.Id;
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string error)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(error, (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error);
    }

    private async Task WithDbAsync(Func<ApplicationDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private sealed record IdDto(Guid Id);

    private sealed record ErrorDto(string Error);

    private sealed record MemberDto(string UserId, string? DisplayName, string Role);

    private sealed record PaidDto(bool IsPaid, string? PaidByName);
}
