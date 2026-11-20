using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.Authorization;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public sealed class TenantIsolationTests : IAsyncLifetime
{
    private const string OtherUser = "otheruser";
    private const string Read = TenantPermissions.InvoicesRead;
    private const string Draft = TenantPermissions.IssuingDraft;

    private static readonly object DraftBody = new
    {
        buyer = new { kind = "Company", nip = "1111111111", name = "Nabywca Testowy" },
        paymentMethod = "Transfer",
        splitPayment = false,
    };

    private sealed record Case(object? Body = null, string? Permission = null, HttpStatusCode Allowed = HttpStatusCode.OK);

    private static readonly Dictionary<string, Case> Cases = new()
    {
        ["GET api/tenants/{tenantId}"] = new(null, Read),
        ["PUT api/tenants/{tenantId}"] = new(new { displayName = "Hijacked" }, TenantPermissions.TenantEdit),
        ["DELETE api/tenants/{tenantId}"] = new(null, TenantPermissions.TenantDelete, HttpStatusCode.NoContent),
        ["GET api/tenants/{tenantId}/members"] = new(null, TenantPermissions.MembersManage),
        ["PUT api/tenants/{tenantId}/members/{userId}"] = new(new { role = "Employee" }, TenantPermissions.MembersManage, HttpStatusCode.NotFound),
        ["DELETE api/tenants/{tenantId}/members/{userId}"] = new(null, TenantPermissions.MembersManage, HttpStatusCode.NotFound),
        ["POST api/tenants/{tenantId}/invites"] = new(new { email = "isolation-invitee@example.invalid", role = "Employee" }, TenantPermissions.MembersManage),
        ["GET api/tenants/{tenantId}/invites"] = new(null, TenantPermissions.MembersManage),
        ["POST api/tenants/{tenantId}/invites/{id}/resend"] = new(null, TenantPermissions.MembersManage),
        ["DELETE api/tenants/{tenantId}/invites/{id}"] = new(null, TenantPermissions.MembersManage, HttpStatusCode.NoContent),
        ["PUT api/tenants/{tenantId}/members/me/notifications"] = new(new { enabled = true }, TenantPermissions.NotificationsSelf, HttpStatusCode.NoContent),
        ["POST api/tenants/{tenantId}/credentials"] = new(new { type = 0 }, TenantPermissions.CredentialManage, HttpStatusCode.BadRequest),
        ["DELETE api/tenants/{tenantId}/credentials"] = new(null, TenantPermissions.CredentialManage, HttpStatusCode.NoContent),
        ["GET api/tenants/{tenantId}/credentials/status"] = new(null, Read),
        ["POST api/tenants/{tenantId}/credentials/sync"] = new(null, TenantPermissions.SyncRun, HttpStatusCode.Conflict),
        ["GET api/tenants/{tenantId}/credentials/sync"] = new(null, TenantPermissions.SyncRun),
        ["POST api/tenants/{tenantId}/credentials/full-resync"] = new(null, TenantPermissions.SyncFullResync, HttpStatusCode.Conflict),
        ["GET api/tenants/{tenantId}/invoices"] = new(null, Read),
        ["GET api/tenants/{tenantId}/invoices/{id}"] = new(null, Read),
        ["GET api/tenants/{tenantId}/invoices/by-number/{ksefInvoiceNumber}"] = new(null, Read),
        ["GET api/tenants/{tenantId}/invoices/{id}/transfer"] = new(null, Read),
        ["GET api/tenants/{tenantId}/invoices/{id}/xml"] = new(null, Read),
        ["PATCH api/tenants/{tenantId}/invoices/{id}/paid"] = new(new { isPaid = true }, TenantPermissions.InvoicesMarkPaid),
        ["GET api/invoices"] = new(null, Read),
        ["GET api/invoices/by-number/{ksefInvoiceNumber}"] = new(null, Read),
        ["GET api/tenants/{tenantId}/issued-invoices"] = new(null, Read),
        ["GET api/tenants/{tenantId}/issued-invoices/feed"] = new(null, Read),
        ["GET api/tenants/{tenantId}/issued-invoices/exchange-rate"] = new(null, Read, HttpStatusCode.BadRequest),
        ["GET api/tenants/{tenantId}/issued-invoices/{id}"] = new(null, Read),
        ["POST api/tenants/{tenantId}/issued-invoices"] = new(DraftBody, Draft, HttpStatusCode.Created),
        ["PUT api/tenants/{tenantId}/issued-invoices/{id}"] = new(DraftBody, Draft),
        ["DELETE api/tenants/{tenantId}/issued-invoices/{id}"] = new(null, Draft, HttpStatusCode.NoContent),
        ["POST api/tenants/{tenantId}/issued-invoices/{id}/issue"] = new(null, TenantPermissions.IssuingSubmit, HttpStatusCode.UnprocessableEntity),
        ["POST api/tenants/{tenantId}/issued-invoices/{id}/copy"] = new(null, Draft, HttpStatusCode.Created),
        ["GET api/tenants/{tenantId}/issued-invoices/{id}/xml"] = new(null, Read, HttpStatusCode.Conflict),
        ["GET api/tenants/{tenantId}/issued-invoices/{id}/upo"] = new(null, Read, HttpStatusCode.Conflict),
        ["GET api/tenants/{tenantId}/seller-profile"] = new(null, Read),
        ["PUT api/tenants/{tenantId}/seller-profile"] = new(new { legalName = "Hijacked", cashAccounting = false }, TenantPermissions.TenantEdit),
        ["POST api/devices/{id}/test-notification"] = new(),
        ["DELETE api/devices/{id}"] = new(),
    };

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _bare;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _owner;
    private readonly HttpClient _other;
    private Seed _seed = null!;

    public TenantIsolationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _bare = new OpenKSeFWebApplicationFactory(fixture);
        _factory = _bare.WithWebHostBuilder(builder => builder.UseSetting("APP_EXTERNAL_BASE_URL", "https://openksef.example.test"));
        _owner = _factory.CreateClient();
        _other = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        _owner.SetBearerToken(await _fixture.GetAccessTokenAsync());
        _other.SetBearerToken(await _fixture.GetAccessTokenAsync(OtherUser));

        var tenantA = await CreateTenantAsync(_owner);
        var tenantB = await CreateTenantAsync(_other);
        var issuedId = await CreateDraftAsync(tenantA);
        var inviteId = await SeedInviteAsync(tenantA);

        var now = DateTime.UtcNow;
        var invoice = new InvoiceHeader
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            KSeFInvoiceNumber = $"ISOLATION-{Guid.NewGuid():N}",
            KSeFReferenceNumber = "ISOLATION",
            VendorName = "Dostawca Testowy",
            VendorNip = "1111111111",
            Direction = InvoiceDirection.Received,
            IssueDate = now,
            FirstSeenAt = now,
            LastUpdatedAt = now,
        };
        var device = Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            var ownerId = (await db.Tenants.SingleAsync(t => t.Id == tenantA)).UserId;
            db.InvoiceHeaders.Add(invoice);
            db.InvoiceXmls.Add(new InvoiceXml { InvoiceHeaderId = invoice.Id, Content = "<Faktura/>"u8.ToArray(), Sha256 = Convert.ToBase64String(SHA256.HashData("<Faktura/>"u8)), StoredAt = now });
            db.DeviceTokens.Add(new DeviceToken
            {
                Id = device, UserId = ownerId, Token = $"isolation-{device:N}", Platform = Platform.Android,
                TenantId = tenantA, CreatedAt = now, UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        });

        _seed = new Seed(tenantA, tenantB, invoice.Id, invoice.KSeFInvoiceNumber, issuedId, device, inviteId);
    }

    public Task DisposeAsync()
    {
        _owner.Dispose();
        _other.Dispose();
        return _bare.DisposeAsync().AsTask();
    }

    private static bool IsTenantScoped(ApiRouteCatalog.ApiRoute route) =>
        route.Parameters.Contains("tenantId")
        || route.Template.StartsWith("api/devices/{id}", StringComparison.Ordinal);

    [Fact]
    public void EveryTenantScopedEndpoint_HasAnIsolationCase()
    {
        var discovered = ApiRouteCatalog.Discover(_factory.Services).Where(IsTenantScoped).Select(r => r.Key).ToHashSet();

        Assert.Empty(discovered.Except(Cases.Keys));
        Assert.Empty(Cases.Keys.Except(discovered));
        Assert.All(Cases.Where(c => c.Key.Contains("tenantId") || c.Key.StartsWith("GET api/invoices", StringComparison.Ordinal)),
            c => Assert.NotNull(c.Value.Permission));
    }

    [Fact]
    public async Task NonMember_GetsNotFound_OnEveryTenantScopedEndpoint()
    {
        var routes = ApiRouteCatalog.Discover(_factory.Services).Where(IsTenantScoped).ToList();
        Assert.NotEmpty(routes);

        var failures = new List<string>();
        foreach (var route in routes)
        {
            var response = await SendAsync(_other, route, _seed.TenantA);
            if (!IsActionNotFound(response))
                failures.Add($"{route.Key}: {(int)response.StatusCode} {response.Content.Headers.ContentType?.MediaType}");
        }

        Assert.Empty(failures);

        var tenant = await _owner.GetFromJsonAsync<NameDto>($"/api/tenants/{_seed.TenantA}");
        Assert.NotEqual("Hijacked", tenant!.DisplayName);
        Assert.Equal(HttpStatusCode.OK, (await _owner.GetAsync($"/api/tenants/{_seed.TenantA}/issued-invoices/{_seed.IssuedId}")).StatusCode);
        await WithDbAsync(async db =>
        {
            Assert.False((await db.InvoiceHeaders.SingleAsync(i => i.Id == _seed.InvoiceId)).IsPaid);
            Assert.True(await db.DeviceTokens.AnyAsync(d => d.Id == _seed.DeviceId));
            Assert.False(await db.SellerProfiles.AnyAsync(p => p.TenantId == _seed.TenantA));
            Assert.False(await db.KSeFCredentials.AnyAsync(c => c.TenantId == _seed.TenantA));
        });
    }

    [Fact]
    public async Task NonMember_AllTenantsInvoiceRoutes_DoNotReturnForeignInvoice()
    {
        var list = await _other.GetFromJsonAsync<InvoicePageDto>("/api/invoices?pageSize=100");
        var byNumber = await _other.GetAsync($"/api/invoices/by-number/{_seed.KsefNumber}");

        Assert.DoesNotContain(list!.Items, i => i.Id == _seed.InvoiceId);
        Assert.Equal(HttpStatusCode.NotFound, byNumber.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _owner.GetAsync($"/api/invoices/by-number/{_seed.KsefNumber}")).StatusCode);
    }

    [Theory]
    [InlineData("PUT", "api/tenants/{0}")]
    [InlineData("POST", "api/tenants/{0}/credentials")]
    public async Task NonMember_InvalidBody_GetsNotFoundBeforeValidation(string method, string template)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "/" + string.Format(template, _seed.TenantA)) { Content = JsonContent.Create(new { type = "nope", notificationEmail = "x" }) };

        Assert.True(IsActionNotFound(await _other.SendAsync(request)));
    }

    [Fact]
    public async Task ForeignResourceIdUnderOwnTenant_Returns404()
    {
        var routes = ApiRouteCatalog.Discover(_factory.Services).Where(r => IsTenantScoped(r) && r.RouteParameters.Any(p => p != "tenantId")).ToList();
        Assert.NotEmpty(routes);

        var failures = new List<string>();
        foreach (var route in routes)
        {
            var response = await SendAsync(_other, route, _seed.TenantB);
            if (!IsActionNotFound(response))
                failures.Add($"{route.Key}: {(int)response.StatusCode} {response.Content.Headers.ContentType?.MediaType}");
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task RoleMatrix_EveryTenantRoute_ReturnsExpectedStatusPerRole()
    {
        _other.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");
        var clients = new List<(string Name, TenantRole? Role, HttpClient Client)> { ("non-member", null, _other) };
        foreach (var role in new[] { TenantRole.Employee, TenantRole.Accountant, TenantRole.Administrator })
            clients.Add((role.ToString(), role, await CreateMemberClientAsync(role)));
        clients.Add(("Owner", TenantRole.Owner, _owner));
        Assert.Equal(HttpStatusCode.OK, (await _owner.PutAsJsonAsync($"/api/tenants/{_seed.TenantA}/seller-profile", new { legalName = "Sprzedawca Testowy", cashAccounting = false })).StatusCode);

        var routes = ApiRouteCatalog.Discover(_factory.Services).Where(r => r.Parameters.Contains("tenantId"))
            .OrderBy(r => r.Key == "DELETE api/tenants/{tenantId}")
            .ToList();
        Assert.Equal(Cases.Count(c => c.Value.Permission is not null), routes.Count);

        var failures = new List<string>();
        try
        {
            foreach (var route in routes)
            {
                var expected = Cases[route.Key];
                foreach (var (name, role, client) in clients)
                {
                    Guid? issuedId = route.Template.Contains("/issued-invoices/{id}") ? await CreateDraftAsync(_seed.TenantA)
                        : route.Template.Contains("/invites/{id}") ? await SeedInviteAsync(_seed.TenantA)
                        : null;
                    if (route.Key == "DELETE api/tenants/{tenantId}/credentials")
                        await WithDbAsync(db => SeedCredentialAsync(db, _seed.TenantA));

                    var response = await SendAsync(client, route, _seed.TenantA, issuedId);
                    var outcome = await DescribeAsync(response);
                    var want = role is null ? "404"
                        : TenantPermissions.For(role.Value).Contains(expected.Permission!) ? ((int)expected.Allowed).ToString()
                        : $"403 {TenantAccessAttribute.ForbiddenMessage}";
                    if (outcome != want)
                        failures.Add($"{route.Key} as {name}: got {outcome}, want {want}");

                    await WithDbAsync(db => db.KSeFCredentials.Where(c => c.TenantId == _seed.TenantA).ExecuteDeleteAsync());
                }
            }
        }
        finally
        {
            foreach (var (_, role, client) in clients)
                if (role is TenantRole.Employee or TenantRole.Accountant or TenantRole.Administrator)
                    client.Dispose();
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task TenantList_ReturnsCallersRoleAndPermissions()
    {
        using var employee = await CreateMemberClientAsync(TenantRole.Employee);

        var ownerView = (await _owner.GetFromJsonAsync<List<RoleDto>>("/api/tenants"))!.Single(t => t.Id == _seed.TenantA);
        var employeeView = Assert.Single((await employee.GetFromJsonAsync<List<RoleDto>>("/api/tenants"))!);

        Assert.Equal("Owner", ownerView.Role);
        Assert.Equal(TenantPermissions.For(TenantRole.Owner).Order(StringComparer.Ordinal), ownerView.Permissions);
        Assert.Equal(11, ownerView.Permissions.Count);
        Assert.Equal(_seed.TenantA, employeeView.Id);
        Assert.Equal("Employee", employeeView.Role);
        Assert.Equal(TenantPermissions.For(TenantRole.Employee).Order(StringComparer.Ordinal), employeeView.Permissions);
        Assert.Empty((await _other.GetFromJsonAsync<List<RoleDto>>("/api/tenants"))!.Where(t => t.Id == _seed.TenantA));
    }

    private async Task<HttpClient> CreateMemberClientAsync(TenantRole role)
    {
        var email = $"member-{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@openksef.test";
        var userId = await _fixture.CreateUserAsync(email);
        await WithDbAsync(async db =>
        {
            db.TenantMembers.Add(new TenantMember { TenantId = _seed.TenantA, UserId = userId, Role = role, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        var client = _factory.CreateClient();
        client.SetBearerToken(await _fixture.GetAccessTokenAsync(email));
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"203.0.113.{10 + (int)role}");
        return client;
    }

    private static async Task SeedCredentialAsync(ApplicationDbContext db, Guid tenantId)
    {
        var now = DateTime.UtcNow;
        db.KSeFCredentials.Add(new KSeFCredential { Id = Guid.NewGuid(), TenantId = tenantId, EncryptedToken = "unused", CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
    }

    private static async Task<string> DescribeAsync(HttpResponseMessage response)
    {
        if (IsActionNotFound(response))
            return "404";
        if (response.StatusCode != HttpStatusCode.Forbidden)
            return ((int)response.StatusCode).ToString();
        var body = await response.Content.ReadFromJsonAsync<ErrorDto>();
        return $"403 {body?.Error}";
    }

    private Task<HttpResponseMessage> SendAsync(HttpClient client, ApiRouteCatalog.ApiRoute route, Guid tenantId, Guid? issuedId = null)
    {
        var path = route.Template
            .Replace("{tenantId}", tenantId.ToString())
            .Replace("{ksefInvoiceNumber}", _seed.KsefNumber)
            .Replace("{userId}", "no-such-member")
            .Replace("{id}", ResourceId(route.Template, issuedId).ToString());
        if (path.Contains('{'))
            throw new InvalidOperationException($"Unfilled route parameter in {route.Key}");
        if (route.Parameters.Contains("tenantId") && !route.Template.Contains("{tenantId}"))
            path += $"?tenantId={tenantId}";

        var request = new HttpRequestMessage(new HttpMethod(route.Method), "/" + path);
        if (Cases[route.Key].Body is { } body)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    private static bool IsActionNotFound(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.NotFound
        && response.Content.Headers.ContentType?.MediaType == "application/problem+json";

    private Guid ResourceId(string template, Guid? resourceId) => template switch
    {
        _ when !template.Contains("{id}") => Guid.Empty,
        _ when template.StartsWith("api/devices/{id}", StringComparison.Ordinal) => _seed.DeviceId,
        _ when template.Contains("/issued-invoices/{id}", StringComparison.Ordinal) => resourceId ?? _seed.IssuedId,
        _ when template.Contains("/invites/{id}", StringComparison.Ordinal) => resourceId ?? _seed.InviteId,
        _ when template.Contains("/invoices/{id}", StringComparison.Ordinal) => _seed.InvoiceId,
        _ => throw new InvalidOperationException($"No seeded resource for {template}"),
    };

    private async Task<Guid> CreateDraftAsync(Guid tenantId)
    {
        var response = await _owner.PostAsJsonAsync($"/api/tenants/{tenantId}/issued-invoices", DraftBody);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private async Task<Guid> SeedInviteAsync(Guid tenantId)
    {
        var invite = new TenantInvite
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Email = $"isolation-{Guid.NewGuid():N}@example.invalid", Role = TenantRole.Employee,
            TokenHash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(14), InvitedByUserId = "isolation",
        };
        await WithDbAsync(async db =>
        {
            db.TenantInvites.Add(invite);
            await db.SaveChangesAsync();
        });
        return invite.Id;
    }

    private static async Task<Guid> CreateTenantAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), displayName = "Isolation test" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private async Task WithDbAsync(Func<ApplicationDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private sealed record Seed(Guid TenantA, Guid TenantB, Guid InvoiceId, string KsefNumber, Guid IssuedId, Guid DeviceId, Guid InviteId);

    private sealed record IdDto(Guid Id);

    private sealed record InvoicePageDto(List<IdDto> Items);

    private sealed record NameDto(string DisplayName);

    private sealed record ErrorDto(string Error);

    private sealed record RoleDto(Guid Id, string Role, List<string> Permissions);
}
