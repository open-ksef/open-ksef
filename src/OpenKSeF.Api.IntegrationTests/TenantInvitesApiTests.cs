using System.Net;
using System.Net.Http.Json;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
public sealed class TenantInvitesApiTests : IAsyncLifetime
{
    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly List<HttpClient> _clients = [];
    private HttpClient _owner = null!;
    private string _ownerEmail = null!;
    private Guid _tenantId;

    public TenantInvitesApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture);
    }

    public async Task InitializeAsync()
    {
        (_, _ownerEmail, _owner) = await NewUserAsync(true, signIn: true);
        _tenantId = await CreateTenantAsync(_owner, "Biuro Zapraszające");
    }

    public Task DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task ExistingVerifiedAccount_GetsAccessAtOnce_WithoutEmail()
    {
        var (userId, email, invitee) = await NewUserAsync(true, signIn: true);

        var response = await InviteAsync(_owner, email.ToUpperInvariant(), "Accountant");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CreateDto>())!;
        Assert.Equal(("granted", "Test User", (bool?)null), (body.Status, body.DisplayName, body.EmailSent));
        var tenant = Assert.Single((await invitee.GetFromJsonAsync<List<TenantDto>>("/api/tenants"))!);
        Assert.Equal((_tenantId, "Accountant"), (tenant.Id, tenant.Role));
        await WithDbAsync(async db =>
        {
            Assert.False(await db.TenantInvites.AnyAsync(i => i.TenantId == _tenantId));
            var audit = await db.AuditEvents.SingleAsync(a => a.TenantId == _tenantId);
            Assert.Equal((AuditEvent.AccessGranted, userId, "Accountant"), (audit.Action, audit.TargetUserId, audit.Target));
        });
    }

    [Fact]
    public async Task UnverifiedAccount_StaysPendingOnSignIn_JoinsByLink_TokenStoredOnlyAsHash()
    {
        var email = NewEmail();
        var created = await CreatePendingAsync(email, "Employee");
        Assert.False(created.EmailSent);
        var token = TokenOf(created.Link!);

        var (userId, _, invitee) = await NewUserAsync(false, email, signIn: true);
        Assert.Empty((await invitee.GetFromJsonAsync<List<TenantDto>>("/api/tenants"))!);
        await WithDbAsync(async db =>
        {
            Assert.Equal(TenantInviteService.Hash(token), (await db.TenantInvites.SingleAsync(i => i.Id == created.InviteId)).TokenHash);
            var row = await db.Database.SqlQuery<string>($"""SELECT row_to_json(t)::text AS "Value" FROM "TenantInvites" t WHERE "Id" = {created.InviteId}""").SingleAsync();
            Assert.DoesNotContain(token, row);
        });

        var accept = await invitee.PostAsJsonAsync("/api/invites/accept", new { token });

        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.Equal(new AcceptDto(_tenantId, "Biuro Zapraszające", "Employee"), await accept.Content.ReadFromJsonAsync<AcceptDto>());
        Assert.Equal(HttpStatusCode.OK, (await invitee.GetAsync($"/api/tenants/{_tenantId}")).StatusCode);
        await AssertInvalidAsync(await invitee.PostAsJsonAsync("/api/invites/accept", new { token }));
        await WithDbAsync(async db =>
        {
            var invite = await db.TenantInvites.SingleAsync(i => i.Id == created.InviteId);
            Assert.Equal((TenantInviteStatus.Accepted, userId), (invite.Status, invite.AcceptedByUserId));
        });
    }

    [Fact]
    public async Task PendingInvite_VerifiedSignIn_BecomesMemberWithoutLink()
    {
        var email = NewEmail();
        var created = await CreatePendingAsync(email, "Administrator");

        var (userId, _, invitee) = await NewUserAsync(true, email, signIn: true);

        var tenant = Assert.Single((await invitee.GetFromJsonAsync<List<TenantDto>>("/api/tenants"))!);
        Assert.Equal((_tenantId, "Administrator"), (tenant.Id, tenant.Role));
        await WithDbAsync(async db =>
        {
            Assert.Equal(TenantInviteStatus.Accepted, (await db.TenantInvites.SingleAsync(i => i.Id == created.InviteId)).Status);
            var audit = await db.AuditEvents.Where(a => a.TenantId == _tenantId).OrderBy(a => a.OccurredAt).Select(a => new { a.Action, a.ActorUserId }).ToListAsync();
            Assert.Equal([AuditEvent.InviteCreated, AuditEvent.InviteAccepted], audit.Select(a => a.Action));
            Assert.Equal(userId, audit[1].ActorUserId);
        });
    }

    [Fact]
    public async Task OtherEmail_Forbidden_InviteStaysPending()
    {
        var created = await CreatePendingAsync(NewEmail(), "Employee");
        var (_, _, stranger) = await NewUserAsync(false, signIn: true);

        var response = await stranger.PostAsJsonAsync("/api/invites/accept", new { token = TokenOf(created.Link!) });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(TenantInviteService.WrongEmailMessage, (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/tenants/{_tenantId}")).StatusCode);
        await WithDbAsync(async db => Assert.Equal(TenantInviteStatus.Pending, (await db.TenantInvites.SingleAsync(i => i.Id == created.InviteId)).Status));
    }

    [Fact]
    public async Task ExpiredRevokedUnknown_GenericNotFound_ListShowsPendingOnly()
    {
        var expiredEmail = NewEmail();
        var revokedEmail = NewEmail();
        var expired = await CreatePendingAsync(expiredEmail, "Employee");
        var revoked = await CreatePendingAsync(revokedEmail, "Accountant");
        await WithDbAsync(db => db.TenantInvites.Where(i => i.Id == expired.InviteId).ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))));
        Assert.Equal(HttpStatusCode.NoContent, (await _owner.DeleteAsync($"/api/tenants/{_tenantId}/invites/{revoked.InviteId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _owner.DeleteAsync($"/api/tenants/{_tenantId}/invites/{revoked.InviteId}")).StatusCode);

        var (_, _, expiredUser) = await NewUserAsync(false, expiredEmail, signIn: true);
        var (_, _, revokedUser) = await NewUserAsync(false, revokedEmail, signIn: true);
        await AssertInvalidAsync(await expiredUser.PostAsJsonAsync("/api/invites/accept", new { token = TokenOf(expired.Link!) }));
        await AssertInvalidAsync(await revokedUser.PostAsJsonAsync("/api/invites/accept", new { token = TokenOf(revoked.Link!) }));
        await AssertInvalidAsync(await revokedUser.PostAsJsonAsync("/api/invites/accept", new { token = "unknown-token" }));

        var list = await _owner.GetAsync($"/api/tenants/{_tenantId}/invites");
        var json = await list.Content.ReadAsStringAsync();
        Assert.DoesNotContain("link", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        var item = Assert.Single((await list.Content.ReadFromJsonAsync<List<InviteDto>>())!);
        Assert.Equal((expired.InviteId!.Value, expiredEmail, "Employee", "Test User", true), (item.Id, item.Email, item.Role, item.InvitedByName, item.IsExpired));
        await WithDbAsync(async db => Assert.False(await db.TenantMembers.AnyAsync(m => m.TenantId == _tenantId && m.Role != TenantRole.Owner)));
    }

    [Fact]
    public async Task Resend_KillsOldLink_AndResetsExpiry()
    {
        var email = NewEmail();
        var created = await CreatePendingAsync(email, "Employee");
        await WithDbAsync(db => db.TenantInvites.Where(i => i.Id == created.InviteId).ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))));

        var resend = await _owner.PostAsync($"/api/tenants/{_tenantId}/invites/{created.InviteId}/resend", null);

        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var renewed = (await resend.Content.ReadFromJsonAsync<LinkDto>())!;
        Assert.NotEqual(created.Link, renewed.Link);
        Assert.False(renewed.EmailSent);
        Assert.InRange(renewed.ExpiresAt, DateTime.UtcNow.AddDays(14).AddMinutes(-5), DateTime.UtcNow.AddDays(14).AddMinutes(5));
        var (_, _, invitee) = await NewUserAsync(false, email, signIn: true);
        await AssertInvalidAsync(await invitee.PostAsJsonAsync("/api/invites/accept", new { token = TokenOf(created.Link!) }));
        Assert.Equal(HttpStatusCode.OK, (await invitee.PostAsJsonAsync("/api/invites/accept", new { token = TokenOf(renewed.Link) })).StatusCode);
    }

    [Fact]
    public async Task ReInvitingPendingEmail_ReplacesTokenAndRole()
    {
        var email = NewEmail();
        var first = await CreatePendingAsync(email, "Employee");

        var second = await CreatePendingAsync(email, "Accountant");

        Assert.Equal(first.InviteId, second.InviteId);
        var (_, _, invitee) = await NewUserAsync(false, email, signIn: true);
        await AssertInvalidAsync(await invitee.PostAsJsonAsync("/api/invites/accept", new { token = TokenOf(first.Link!) }));
        var accept = await invitee.PostAsJsonAsync("/api/invites/accept", new { token = TokenOf(second.Link!) });
        Assert.Equal("Accountant", (await accept.Content.ReadFromJsonAsync<AcceptDto>())!.Role);
    }

    [Fact]
    public async Task OwnOrMemberEmail_Conflict_NothingChanges()
    {
        var (_, email, _) = await NewUserAsync(true, signIn: true);
        Assert.Equal(HttpStatusCode.OK, (await InviteAsync(_owner, email, "Employee")).StatusCode);

        await AssertErrorAsync(await InviteAsync(_owner, email, "Administrator"), HttpStatusCode.Conflict, TenantInviteService.AlreadyMemberMessage);
        await AssertErrorAsync(await InviteAsync(_owner, $"  {_ownerEmail.ToUpperInvariant()} ", "Employee"), HttpStatusCode.Conflict, TenantInviteService.AlreadyMemberMessage);

        await WithDbAsync(async db =>
        {
            Assert.Equal([TenantRole.Owner, TenantRole.Employee], await db.TenantMembers.Where(m => m.TenantId == _tenantId).OrderBy(m => m.CreatedAt).Select(m => m.Role).ToListAsync());
            Assert.False(await db.TenantInvites.AnyAsync(i => i.TenantId == _tenantId));
        });
    }

    [Fact]
    public async Task Administrator_InvitingOwner_Forbidden_EmployeeAllowed()
    {
        var (adminId, _, admin) = await NewUserAsync(true);
        await WithDbAsync(async db =>
        {
            db.TenantMembers.Add(new TenantMember { TenantId = _tenantId, UserId = adminId, Role = TenantRole.Administrator, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        var ownerInvite = await CreatePendingAsync(NewEmail(), "Owner");

        await AssertErrorAsync(await InviteAsync(admin, NewEmail(), "Owner"), HttpStatusCode.Forbidden, TenantAccessAttribute.ForbiddenMessage);
        await AssertErrorAsync(await admin.PostAsync($"/api/tenants/{_tenantId}/invites/{ownerInvite.InviteId}/resend", null), HttpStatusCode.Forbidden, TenantAccessAttribute.ForbiddenMessage);
        await AssertErrorAsync(await admin.DeleteAsync($"/api/tenants/{_tenantId}/invites/{ownerInvite.InviteId}"), HttpStatusCode.Forbidden, TenantAccessAttribute.ForbiddenMessage);
        Assert.Equal(HttpStatusCode.OK, (await InviteAsync(admin, NewEmail(), "Employee")).StatusCode);
    }

    [Fact]
    public async Task TwentyInvitesPerTenantPerDay_Returns429()
    {
        var now = DateTime.UtcNow;
        await SeedAuditAsync(_tenantId, "someone-else", 19, now.AddHours(-23));
        await SeedAuditAsync(_tenantId, "someone-else", 5, now.AddHours(-25));
        var last = await CreatePendingAsync(NewEmail(), "Employee");

        await AssertErrorAsync(await InviteAsync(_owner, NewEmail(), "Employee"), HttpStatusCode.TooManyRequests, TenantInviteService.LimitMessage);
        await AssertErrorAsync(await _owner.PostAsync($"/api/tenants/{_tenantId}/invites/{last.InviteId}/resend", null), HttpStatusCode.TooManyRequests, TenantInviteService.LimitMessage);
    }

    [Fact]
    public async Task FiftyInvitesPerActorPerDay_AcrossCompanies_Returns429()
    {
        var (actorId, _, actor) = await NewUserAsync(true, signIn: true);
        var tenantId = await CreateTenantAsync(actor, "Inna firma");
        await SeedAuditAsync(null, actorId, 49, DateTime.UtcNow.AddHours(-1));

        Assert.Equal(HttpStatusCode.OK, (await InviteAsync(actor, NewEmail(), "Employee", tenantId)).StatusCode);
        await AssertErrorAsync(await InviteAsync(actor, NewEmail(), "Employee", tenantId), HttpStatusCode.TooManyRequests, TenantInviteService.LimitMessage);
    }

    [Fact]
    public async Task EmailEnabled_SendsInviteAndAccessGranted_EmailSentTrue()
    {
        await using IContainer mailpit = Mailpit.Create();
        await mailpit.StartAsync();
        using var smtp = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Email:Enabled", "true");
            builder.UseSetting("Email:SmtpHost", mailpit.Hostname);
            builder.UseSetting("Email:SmtpPort", mailpit.GetMappedPublicPort(Mailpit.SmtpPort).ToString());
            builder.UseSetting("Email:UseSsl", "false");
        });
        using var owner = smtp.CreateClient();
        owner.DefaultRequestHeaders.Authorization = _owner.DefaultRequestHeaders.Authorization;
        owner.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.99");
        var pendingEmail = NewEmail();
        var (_, grantedEmail, _) = await NewUserAsync(true, signIn: true);

        var pending = (await (await InviteAsync(owner, pendingEmail, "Employee")).Content.ReadFromJsonAsync<CreateDto>())!;
        var granted = await InviteAsync(owner, grantedEmail, "Employee");

        Assert.Equal(("pending", true), (pending.Status, pending.EmailSent));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        var messages = await Mailpit.MessagesAsync(mailpit);
        Assert.Contains(("Zaproszenie do firmy Biuro Zapraszające", pendingEmail), messages);
        Assert.Contains(("Masz dostęp do firmy Biuro Zapraszające", grantedEmail), messages);
    }

    private async Task<CreateDto> CreatePendingAsync(string email, string role)
    {
        var response = await InviteAsync(_owner, email, role);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CreateDto>())!;
        Assert.Equal("pending", body.Status);
        Assert.NotNull(body.InviteId);
        return body;
    }

    private Task<HttpResponseMessage> InviteAsync(HttpClient client, string email, string role, Guid? tenantId = null) =>
        client.PostAsJsonAsync($"/api/tenants/{tenantId ?? _tenantId}/invites", new { email, role });

    private static string TokenOf(string link)
    {
        var token = link.Split("/zaproszenie/")[1];
        Assert.Equal(43, token.Length);
        return token;
    }

    private static string NewEmail() => $"invite-{Guid.NewGuid():N}@openksef.test";

    private async Task<(string UserId, string Email, HttpClient Client)> NewUserAsync(bool emailVerified, string? email = null, bool signIn = false)
    {
        email ??= NewEmail();
        var userId = await _fixture.CreateUserAsync(email, emailVerified: emailVerified);
        var client = _factory.CreateClient();
        client.SetBearerToken(await _fixture.GetAccessTokenAsync(email));
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"203.0.113.{60 + _clients.Count}");
        _clients.Add(client);
        if (signIn)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tenants")).StatusCode);
        return (userId, email, client);
    }

    private static async Task<Guid> CreateTenantAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), displayName = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TenantDto>())!.Id;
    }

    private Task SeedAuditAsync(Guid? tenantId, string actorUserId, int count, DateTime at) => WithDbAsync(async db =>
    {
        db.AuditEvents.AddRange(Enumerable.Range(0, count).Select(_ => new AuditEvent
        {
            Id = Guid.NewGuid(), OccurredAt = at, TenantId = tenantId, ActorUserId = actorUserId, Action = AuditEvent.InviteCreated,
        }));
        await db.SaveChangesAsync();
    });

    private static Task AssertInvalidAsync(HttpResponseMessage response) =>
        AssertErrorAsync(response, HttpStatusCode.NotFound, TenantInviteService.InvalidMessage);

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

    private sealed record TenantDto(Guid Id, string Role);

    private sealed record CreateDto(string Status, string? DisplayName, Guid? InviteId, string? Link, DateTime? ExpiresAt, bool? EmailSent);

    private sealed record LinkDto(string Link, DateTime ExpiresAt, bool EmailSent);

    private sealed record InviteDto(Guid Id, string Email, string Role, string? InvitedByName, DateTime ExpiresAt, bool IsExpired);

    private sealed record AcceptDto(Guid TenantId, string DisplayName, string Role);

    private sealed record ErrorDto(string Error);
}
