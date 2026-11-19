using System.Net;
using System.Net.Http.Json;
using KSeF.Client.Core.Exceptions;
using KSeF.Client.DI;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Api.IntegrationTests;

/// <summary>REQ-0054 company claim by KSeF token, with the KSeF gateway and e-mail substituted.</summary>
[Collection(TestcontainersCollection.Name)]
public sealed class TenantClaimApiTests : IAsyncLifetime
{
    private const string Url = "/api/tenants/claim";
    private const string Cm = "CredentialsManage";

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly IKSeFGateway _gateway = Substitute.For<IKSeFGateway>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly IKSeFIssuingEnvironment _environment = Substitute.For<IKSeFIssuingEnvironment>();
    private readonly LogCapture _log = new(LogLevel.Trace);
    private readonly List<HttpClient> _clients = [];
    private readonly string _nip = TestNip.New("998");
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _a1 = $"claim-a1-{Guid.NewGuid():N}";
    private readonly string _a2 = $"claim-a2-{Guid.NewGuid():N}";
    private readonly string _admin = $"claim-d-{Guid.NewGuid():N}";
    private readonly string _employee = $"claim-e-{Guid.NewGuid():N}";

    public TenantClaimApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _environment.Current.Returns("production");
        _baseFactory = new OpenKSeFWebApplicationFactory(fixture);
        _factory = _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(_gateway);
            services.AddSingleton(_email);
            services.AddSingleton(_environment);
            services.AddSingleton<ILoggerFactory>(new LoggerFactory([_log]));
        }));
    }

    public async Task InitializeAsync()
    {
        _factory.Services.GetRequiredService<KSeFClientOptions>().BaseUrl = KsefEnvironmentsUris.PROD;
        var now = DateTime.UtcNow;
        await WithDbAsync(async db =>
        {
            db.Tenants.Add(new Tenant { Id = _tenantId, UserId = _a1, Nip = _nip, DisplayName = "Firma C", NotificationEmail = "biuro@example.invalid", CreatedAt = now, UpdatedAt = now });
            foreach (var (userId, role) in new[] { (_a1, TenantRole.Owner), (_a2, TenantRole.Owner), (_admin, TenantRole.Administrator), (_employee, TenantRole.Employee) })
            {
                db.TenantMembers.Add(new TenantMember { TenantId = _tenantId, UserId = userId, Role = role, CreatedAt = now });
                db.UserProfiles.Add(new UserProfile { UserId = userId, Email = $"{userId}@example.invalid", DisplayName = userId, UpdatedAt = now });
            }
            db.KSeFCredentials.Add(new KSeFCredential { Id = Guid.NewGuid(), TenantId = _tenantId, EncryptedToken = "stored-credential", CreatedAt = now, UpdatedAt = now });
            db.InvoiceHeaders.Add(new InvoiceHeader
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, KSeFInvoiceNumber = $"CLAIM-{Guid.NewGuid():N}", KSeFReferenceNumber = "CLAIM",
                VendorName = "Dostawca", VendorNip = "1111111111", Direction = InvoiceDirection.Received,
                IssueDate = now, FirstSeenAt = now, LastUpdatedAt = now, IsPaid = true, PaidAt = now, PaidByUserId = _a1,
            });
            db.IssuedInvoices.Add(IssuedInvoice.Draft(Guid.NewGuid(), _tenantId, DateOnly.FromDateTime(now), now));
            db.InvoiceNumberCounters.Add(new InvoiceNumberCounter { TenantId = _tenantId, Period = "2026-11", Next = 7 });
            await db.SaveChangesAsync();
        });
    }

    public async Task DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _factory.DisposeAsync();
        await _baseFactory.DisposeAsync();
    }

    [Fact]
    public async Task Claim_ByNonMember_SoleOwner_FormerOwnersRemovedAndEmailed_OtherRolesAndDataKept()
    {
        var (claimantId, claimant) = await ClientAsync();
        Grant(Cm, "InvoiceRead");
        var before = await SnapshotAsync();

        var response = await claimant.PostAsJsonAsync(Url, Body());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tenant = (await response.Content.ReadFromJsonAsync<TenantDto>())!;
        Assert.Equal((_tenantId, "Owner"), (tenant.Id, tenant.Role));
        Assert.Equal(before, await SnapshotAsync());
        await WithDbAsync(async db =>
        {
            var members = await db.TenantMembers.Where(m => m.TenantId == _tenantId).ToListAsync();
            Assert.Equal(new[] { (claimantId, TenantRole.Owner), (_admin, TenantRole.Administrator), (_employee, TenantRole.Employee) }.Order(), members.Select(m => (m.UserId, m.Role)).Order());
            var audit = await db.AuditEvents.Where(a => a.TenantId == _tenantId).ToListAsync();
            var claimed = Assert.Single(audit, a => a.Action == AuditEvent.OwnershipClaimed);
            Assert.Equal(claimantId, claimed.ActorUserId);
            Assert.Equal("CredentialsManage,InvoiceRead;auth:20261119-AU-TEST", claimed.Target);
            Assert.Equal([_a1, _a2], audit.Where(a => a.Action == AuditEvent.MemberRemoved).Select(a => a.TargetUserId).Order());
            Assert.All(audit.Where(a => a.Action == AuditEvent.MemberRemoved), a => Assert.Equal("Owner", a.Target));
            Assert.Equal(3, audit.Count);
            Assert.Equal(_a1, (await db.Tenants.SingleAsync(t => t.Id == _tenantId)).UserId);
        });
        await _gateway.Received(1).CheckTokenAsync(_nip, Token(), Arg.Any<CancellationToken>());
        var recipients = _email.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IEmailService.SendOwnershipClaimedEmailAsync)).Select(c => (string)c.GetArguments()[0]!).Order();
        Assert.Equal([$"{_a1}@example.invalid", $"{_a2}@example.invalid"], recipients);
        await _email.Received().SendOwnershipClaimedEmailAsync(Arg.Any<string>(), _tenantId, "Firma C", _nip, Arg.Any<string>());
    }

    [Fact]
    public async Task Claim_NotificationEmailOfRemovedOwner_IsCleared()
    {
        await WithDbAsync(db => db.Tenants.Where(t => t.Id == _tenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.NotificationEmail, $" {_a1.ToUpperInvariant()}@EXAMPLE.invalid")));
        var (claimantId, claimant) = await ClientAsync();
        Grant(Cm);

        Assert.Equal(HttpStatusCode.OK, (await claimant.PostAsJsonAsync(Url, Body())).StatusCode);

        await WithDbAsync(async db =>
        {
            Assert.Null((await db.Tenants.SingleAsync(t => t.Id == _tenantId)).NotificationEmail);
            var cleared = await db.AuditEvents.SingleAsync(a => a.TenantId == _tenantId && a.Action == AuditEvent.NotificationEmailCleared);
            Assert.Equal((claimantId, _a1), (cleared.ActorUserId, cleared.TargetUserId));
        });
    }

    [Fact]
    public async Task Claim_NotProven_400_NothingChanges()
    {
        var (_, claimant) = await ClientAsync();
        _gateway.CheckTokenAsync(default!, default!, default).ReturnsForAnyArgs((KSeFTokenGrant?)null);
        var before = await StateAsync();

        await AssertErrorAsync(await claimant.PostAsJsonAsync(Url, Body()), HttpStatusCode.BadRequest, TenantClaimService.NotProvenMessage(_nip));

        Assert.Equal(before, await StateAsync());
    }

    [Fact]
    public async Task Claim_TokenWithoutCredentialsManage_400_NothingChanges()
    {
        var (_, claimant) = await ClientAsync();
        Grant("InvoiceRead", "InvoiceWrite", "CredentialsRead");
        var before = await StateAsync();

        await AssertErrorAsync(await claimant.PostAsJsonAsync(Url, Body()), HttpStatusCode.BadRequest, TenantClaimService.NotProvenMessage(_nip));

        Assert.Equal(before, await StateAsync());
    }

    [Fact]
    public async Task Claim_TokenOfAnotherNip_400_WithoutKSeFCall()
    {
        var (_, claimant) = await ClientAsync();

        await AssertErrorAsync(await claimant.PostAsJsonAsync(Url, new { nip = _nip, token = "20261119-EC-0000000000-0000000000-00|nip-1111111111|secret" }), HttpStatusCode.BadRequest, TenantClaimService.NotProvenMessage(_nip));

        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task Claim_ClientNotOnProductionUrl_409_WithoutKSeFCall()
    {
        _factory.Services.GetRequiredService<KSeFClientOptions>().BaseUrl = KsefEnvironmentsUris.TEST;
        var (_, claimant) = await ClientAsync();

        await AssertErrorAsync(await claimant.PostAsJsonAsync(Url, Body()), HttpStatusCode.Conflict, TenantClaimService.ProductionOnlyMessage);

        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task Claim_ClaimantBecomesMemberWhileWaitingForRowLock_409AlreadyMember_OwnersKept()
    {
        var (userId, claimant) = await ClientAsync();
        Grant(Cm);
        using var scope = _factory.Services.CreateScope();
        var holder = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await holder.Database.BeginTransactionAsync();
        await TenantInviteService.LockTenantAsync(holder, _tenantId);

        var claim = claimant.PostAsJsonAsync(Url, Body());
        await TenantMembersApiTests.WaitForLockWaitersAsync(holder, 1);
        holder.TenantMembers.Add(new TenantMember { TenantId = _tenantId, UserId = userId, Role = TenantRole.Employee, CreatedAt = DateTime.UtcNow });
        await holder.SaveChangesAsync();
        await transaction.CommitAsync();

        await AssertErrorAsync(await claim, HttpStatusCode.Conflict, "Masz już dostęp do firmy z tym NIP-em: Firma C.");
        await WithDbAsync(async db =>
            Assert.Equal([_a1, _a2], (await db.TenantMembers.Where(m => m.TenantId == _tenantId && m.Role == TenantRole.Owner).Select(m => m.UserId).ToListAsync()).Order()));
    }

    [Fact]
    public async Task Claim_ClaimantWithBlankDisplayName_EmailNamesTheirAddress()
    {
        var (userId, claimant) = await ClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await claimant.GetAsync("/api/tenants")).StatusCode);
        string email = null!;
        await WithDbAsync(async db =>
        {
            await db.UserProfiles.Where(p => p.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(p => p.DisplayName, " "));
            email = await db.UserProfiles.Where(p => p.UserId == userId).Select(p => p.Email!).SingleAsync();
        });
        Grant(Cm);

        Assert.Equal(HttpStatusCode.OK, (await claimant.PostAsJsonAsync(Url, Body())).StatusCode);

        await _email.Received(2).SendOwnershipClaimedEmailAsync(Arg.Any<string>(), _tenantId, "Firma C", _nip, email);
    }

    [Theory]
    [InlineData("test")]
    [InlineData("demo")]
    public async Task Claim_OnTestOrDemo_409_WithoutKSeFCall(string environment)
    {
        _environment.Current.Returns(environment);
        var (_, claimant) = await ClientAsync();
        var before = await StateAsync();

        await AssertErrorAsync(await claimant.PostAsJsonAsync(Url, Body()), HttpStatusCode.Conflict, TenantClaimService.ProductionOnlyMessage);

        Assert.Empty(_gateway.ReceivedCalls());
        Assert.Equal(before, await StateAsync());
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("network")]
    [InlineData("5xx")]
    [InlineData("429")]
    public async Task Claim_KSeFUnavailable_503_NothingChanges(string failure)
    {
        var (_, claimant) = await ClientAsync();
        Exception error = failure switch
        {
            "timeout" => new TimeoutException(),
            "network" => new HttpRequestException(),
            "5xx" => new KsefApiException("KSeF HTTP 503", HttpStatusCode.ServiceUnavailable),
            _ => KsefRateLimitException.FromRetryAfterHeader("KSeF HTTP 429", "30"),
        };
        _gateway.CheckTokenAsync(default!, default!, default).ThrowsAsyncForAnyArgs(error);
        var before = await StateAsync();

        await AssertErrorAsync(await claimant.PostAsJsonAsync(Url, Body()), HttpStatusCode.ServiceUnavailable, TenantClaimService.UnavailableMessage);

        Assert.Equal(before, await StateAsync());
    }

    [Fact]
    public async Task Claim_SixthAttemptOfUserInAnHour_429_WithoutKSeFCall()
    {
        var (_, claimant) = await ClientAsync();
        _gateway.CheckTokenAsync(default!, default!, default).ReturnsForAnyArgs((KSeFTokenGrant?)null);

        for (var i = 0; i < TenantClaimLimiter.UserLimit; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await claimant.PostAsJsonAsync(Url, Body())).StatusCode);
        await AssertErrorAsync(await claimant.PostAsJsonAsync(Url, Body()), HttpStatusCode.TooManyRequests, TenantClaimService.LimitedMessage);

        await _gateway.ReceivedWithAnyArgs(TenantClaimLimiter.UserLimit).CheckTokenAsync(default!, default!, default);
    }

    [Fact]
    public async Task Claim_EleventhAttemptOnNipInADay_429_WithoutKSeFCall()
    {
        var limiter = _factory.Services.GetRequiredService<TenantClaimLimiter>();
        for (var i = 0; i < TenantClaimLimiter.NipLimit; i++)
            Assert.True(limiter.TryAcquire($"other-{i}", _nip));
        var (_, claimant) = await ClientAsync();

        await AssertErrorAsync(await claimant.PostAsJsonAsync(Url, Body()), HttpStatusCode.TooManyRequests, TenantClaimService.LimitedMessage);

        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task Claim_TwoClaimantsAtOnce_BothSucceed_LaterIsTheOnlyOwner()
    {
        var (bId, b) = await ClientAsync();
        var (fId, f) = await ClientAsync();
        var gate = new TaskCompletionSource<KSeFTokenGrant?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _gateway.CheckTokenAsync(default!, default!, default).ReturnsForAnyArgs(_ => gate.Task);

        var claims = new[] { b.PostAsJsonAsync(Url, Body()), f.PostAsJsonAsync(Url, Body()) };
        for (var i = 0; i < 100 && _gateway.ReceivedCalls().Count() < 2; i++)
            await Task.Delay(50);
        Assert.Equal(2, _gateway.ReceivedCalls().Count());
        gate.SetResult(new KSeFTokenGrant([Cm], "20261119-AU-TEST"));

        Assert.All(await Task.WhenAll(claims), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await WithDbAsync(async db =>
        {
            var owner = Assert.Single(await db.TenantMembers.Where(m => m.TenantId == _tenantId && m.Role == TenantRole.Owner).ToListAsync());
            var claimed = await db.AuditEvents.Where(a => a.TenantId == _tenantId && a.Action == AuditEvent.OwnershipClaimed).OrderBy(a => a.OccurredAt).ToListAsync();
            Assert.Equal(2, claimed.Count);
            Assert.Equal(owner.UserId, claimed[1].ActorUserId);
            var first = claimed[0].ActorUserId;
            Assert.Contains(first, new[] { bId, fId });
            Assert.True(await db.AuditEvents.AnyAsync(a => a.TenantId == _tenantId && a.Action == AuditEvent.MemberRemoved && a.TargetUserId == first && a.ActorUserId == owner.UserId));
            Assert.Equal(2, await db.TenantMembers.CountAsync(m => m.TenantId == _tenantId && m.Role != TenantRole.Owner));
        });
    }

    [Fact]
    public async Task Claim_ByMember_409AlreadyMember_WithoutKSeFCall()
    {
        var (userId, member) = await ClientAsync();
        await WithDbAsync(async db =>
        {
            db.TenantMembers.Add(new TenantMember { TenantId = _tenantId, UserId = userId, Role = TenantRole.Employee, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        await AssertErrorAsync(await member.PostAsJsonAsync(Url, Body()), HttpStatusCode.Conflict, "Masz już dostęp do firmy z tym NIP-em: Firma C.");

        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task Claim_NipWithTwoActiveCompanies_409_NothingChanges()
    {
        await WithDbAsync(async db =>
        {
            db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), UserId = $"claim-dup-{Guid.NewGuid():N}", Nip = _nip, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        var (_, claimant) = await ClientAsync();
        var before = await StateAsync();

        await AssertErrorAsync(await claimant.PostAsJsonAsync(Url, Body()), HttpStatusCode.Conflict, TenantClaimService.DuplicatesMessage);

        Assert.Empty(_gateway.ReceivedCalls());
        Assert.Equal(before, await StateAsync());
    }

    [Fact]
    public async Task Claim_NoActiveCompany_409_InvalidNip_400()
    {
        var (_, claimant) = await ClientAsync();

        await AssertErrorAsync(await claimant.PostAsJsonAsync(Url, new { nip = TestNip.New("997"), token = "a|b|c" }), HttpStatusCode.Conflict, TenantClaimService.NoCompanyMessage);
        await AssertErrorAsync(await claimant.PostAsJsonAsync(Url, new { nip = "1111111112", token = "a|b|c" }), HttpStatusCode.BadRequest, TenantNipGuard.InvalidNipMessage);

        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task Claim_AnyOutcome_TokenAppearsInNoLogDbRowOrResponse()
    {
        var token = $"REF-MARK|nip-{_nip}|SECRET-MARK";
        var (_, claimant) = await ClientAsync();
        var bodies = new List<string>();
        _gateway.CheckTokenAsync(default!, default!, default).ThrowsAsyncForAnyArgs(new TimeoutException());
        bodies.Add(await (await claimant.PostAsJsonAsync(Url, new { nip = _nip, token })).Content.ReadAsStringAsync());
        _gateway.CheckTokenAsync(default!, default!, default).ReturnsForAnyArgs((KSeFTokenGrant?)null);
        bodies.Add(await (await claimant.PostAsJsonAsync(Url, new { nip = _nip, token })).Content.ReadAsStringAsync());
        Grant(Cm);
        var ok = await claimant.PostAsJsonAsync(Url, new { nip = _nip, token });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        bodies.Add(await ok.Content.ReadAsStringAsync());

        var rows = new List<string>();
        await WithDbAsync(async db =>
        {
            foreach (var table in new[] { "AuditEvents", "TenantMembers", "KSeFCredentials", "Tenants", "UserProfiles" })
                rows.AddRange(await db.Database.SqlQueryRaw<string>("SELECT row_to_json(x)::text AS \"Value\" FROM \"" + table + "\" x").ToListAsync());
        });

        Assert.NotEmpty(_log.Entries);
        Assert.Contains(_log.Entries, e => e.Contains(_nip));
        foreach (var text in _log.Entries.Concat(rows).Concat(bodies))
        {
            Assert.DoesNotContain("SECRET-MARK", text);
            Assert.DoesNotContain("REF-MARK", text);
        }
    }

    private object Body() => new { nip = _nip, token = Token() };

    private string Token() => $"20261119-EC-0000000000-0000000000-00|nip-{_nip}|secret";

    private void Grant(params string[] permissions) =>
        _gateway.CheckTokenAsync(default!, default!, default).ReturnsForAnyArgs(new KSeFTokenGrant(permissions, "20261119-AU-TEST"));

    private async Task<(string UserId, HttpClient Client)> ClientAsync()
    {
        var email = $"claim-{Guid.NewGuid():N}@openksef.test";
        var userId = await _fixture.CreateUserAsync(email);
        var client = _factory.CreateClient();
        client.SetBearerToken(await _fixture.GetAccessTokenAsync(email));
        _clients.Add(client);
        return (userId, client);
    }

    private async Task<List<string>> SnapshotAsync()
    {
        var rows = new List<string>();
        await WithDbAsync(async db =>
        {
            var tables = await db.Database.SqlQuery<string>(
                $"""SELECT table_name AS "Value" FROM information_schema.columns WHERE table_schema = 'public' AND column_name = 'TenantId' AND table_name NOT IN ('TenantMembers', 'AuditEvents') ORDER BY table_name""").ToListAsync();
            Assert.Contains("InvoiceHeaders", tables);
            foreach (var table in tables.Append("Tenants"))
            {
                var key = table == "Tenants" ? "Id" : "TenantId";
                rows.AddRange(await db.Database.SqlQueryRaw<string>(
                    "SELECT '" + table + ":' || row_to_json(x)::text AS \"Value\" FROM \"" + table + "\" x WHERE \"" + key + "\" = {0} ORDER BY 1", _tenantId).ToListAsync());
            }
        });
        Assert.Contains(rows, r => r.StartsWith("IssuedInvoices:"));
        return rows;
    }

    private async Task<List<string>> StateAsync()
    {
        var rows = await SnapshotAsync();
        await WithDbAsync(async db =>
        {
            rows.AddRange(await db.TenantMembers.Where(m => m.TenantId == _tenantId).OrderBy(m => m.UserId).Select(m => m.UserId + ":" + m.Role).ToListAsync());
            rows.Add($"audit:{await db.AuditEvents.CountAsync(a => a.TenantId == _tenantId)}");
        });
        return rows;
    }

    private async Task WithDbAsync(Func<ApplicationDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string error)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(error, (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error);
    }

    private sealed record ErrorDto(string Error);

    private sealed record TenantDto(Guid Id, string Role);
}
