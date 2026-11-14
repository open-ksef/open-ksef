using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class TwoFactorRealmTests : IAsyncLifetime
{
    private const string Password = "Test1234!";

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;

    public TwoFactorRealmTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture);
    }

    public Task InitializeAsync() => SaveSettingsAsync();

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task SettingsSave_AppliesTwoFactorPolicyOnce_SecondSaveChangesNothing()
    {
        var before = await TwoFactorStateAsync();

        await SaveSettingsAsync();

        Assert.True(JsonNode.DeepEquals(before, await TwoFactorStateAsync()));
        var realm = await AdminGetAsync("");
        Assert.Equal(SystemSetupService.BrowserFlowAlias, (string?)realm["browserFlow"]);
        Assert.Equal(SystemSetupService.RegistrationFlowAlias, (string?)realm["registrationFlow"]);
        Assert.Equal(SystemSetupService.ResetCredentialsFlowAlias, (string?)realm["resetCredentialsFlow"]);
        Assert.Equal("totp", (string?)realm["otpPolicyType"]);
        Assert.Equal("HmacSHA1", (string?)realm["otpPolicyAlgorithm"]);
        Assert.Equal(6, (int?)realm["otpPolicyDigits"]);
        Assert.Equal(30, (int?)realm["otpPolicyPeriod"]);
        Assert.Equal(1, (int?)realm["otpPolicyLookAheadWindow"]);
        Assert.Null(realm["loginTheme"]);
        Assert.Null(realm["accountTheme"]);
        Assert.Contains("UPDATE_TOTP", realm["enabledEventTypes"]!.AsArray().Select(e => (string?)e));

        var browser = await ExecutionsAsync(SystemSetupService.BrowserFlowAlias);
        var otpIfNoTwoFactor = browser.Single(e => (string?)e["displayName"] == SystemSetupService.OtpIfNoTwoFactorFlowAlias);
        Assert.Equal("CONDITIONAL", (string?)otpIfNoTwoFactor["requirement"]);
        Assert.Equal("ALTERNATIVE", (string?)browser.Single(e => (string?)e["providerId"] == "auth-recovery-authn-code-form")["requirement"]);
        Assert.Equal(2, browser.Count(e => (string?)e["providerId"] == "auth-otp-form"));
        var condition = (await ExecutionsAsync(SystemSetupService.OtpIfNoTwoFactorFlowAlias)).Single(e => (string?)e["providerId"] == "conditional-sub-flow-executed");
        var config = await AdminGetAsync($"/authentication/config/{condition["authenticationConfig"]}");
        Assert.Equal("not-executed", (string?)config["config"]!["check_result"]);

        var registration = await ExecutionsAsync(SystemSetupService.RegistrationFlowAlias);
        Assert.Equal("REQUIRED", (string?)registration.Single(e => (string?)e["providerId"] == "auth-otp-form")["requirement"]);

        var reset = await ExecutionsAsync(SystemSetupService.ResetCredentialsFlowAlias);
        Assert.DoesNotContain(reset, e => (string?)e["providerId"] == "reset-otp");
        Assert.All(reset.Where(e => (string?)e["providerId"] is "auth-otp-form" or "auth-recovery-authn-code-form"),
            e => Assert.Equal("ALTERNATIVE", (string?)e["requirement"]));
        Assert.Equal(2, reset.Count(e => (string?)e["providerId"] is "auth-otp-form" or "auth-recovery-authn-code-form"));

        Assert.True((bool?)(await AdminGetAsync("/authentication/required-actions/CONFIGURE_RECOVERY_AUTHN_CODES"))["enabled"]);
        Assert.Equal("true", (string?)(await AdminGetAsync("/authentication/required-actions/CONFIGURE_TOTP/config"))["config"]!["add-recovery-codes"]);
    }

    [Fact]
    public async Task SettingsSave_UnboundCopy_IsRebuiltAndBoundWithoutOrphans()
    {
        await _fixture.UpdateRealmAsync(new { browserFlow = "browser" });

        await SaveSettingsAsync();

        Assert.Equal(SystemSetupService.BrowserFlowAlias, (string?)(await AdminGetAsync(""))["browserFlow"]);
        var flows = (await AdminGetAsync("/authentication/flows")).AsArray();
        Assert.Single(flows, f => (string?)f!["alias"] == SystemSetupService.BrowserFlowAlias);
        Assert.Single(await ExecutionsAsync(SystemSetupService.BrowserFlowAlias), e => (string?)e["displayName"] == SystemSetupService.OtpIfNoTwoFactorFlowAlias);
    }

    [Fact]
    public async Task PasswordUserWithoutOtp_IsForcedToSetUp_ThenRecoveryCodes_ThenLogsIn_ThenIsAskedForCode()
    {
        var email = NewEmail();
        await _fixture.CreateUserAsync(email, Password);
        using var browser = new KeycloakBrowser();

        await browser.GetAsync(KeycloakBrowser.AuthUrl(_fixture.KeycloakAuthority));
        await browser.SubmitLoginAsync(email, Password);
        var enrolment = await browser.EnrolAsync();

        Assert.False(string.IsNullOrEmpty(browser.Code));
        Assert.Equal(12, enrolment.RecoveryCodes.Length);

        using var second = new KeycloakBrowser();
        await second.GetAsync(KeycloakBrowser.AuthUrl(_fixture.KeycloakAuthority));
        await second.SubmitLoginAsync(email, Password);
        Assert.True(second.OnPage("kc-otp-login-form"));
        await second.SubmitAsync("kc-otp-login-form", ("otp", "000000"));
        Assert.True(second.OnPage("kc-otp-login-form"));
        Assert.Null(second.Code);
        await second.SubmitAsync("kc-otp-login-form", ("otp", enrolment.Totp.Next()));
        Assert.False(string.IsNullOrEmpty(second.Code));
    }

    [Fact]
    public async Task RecoveryCode_LogsInOnce()
    {
        var (email, userId, enrolment) = await EnrolledUserAsync();

        var used = await LoginWithRecoveryCodeAsync(email, enrolment, null);

        var codes = (await _fixture.GetUserCredentialsAsync(userId)).Single(c => c.GetProperty("type").GetString() == "recovery-authn-codes");
        Assert.Equal(11, JsonDocument.Parse(codes.GetProperty("credentialData").GetString()!).RootElement.GetProperty("remainingCodes").GetInt32());
        await LoginWithRecoveryCodeAsync(email, enrolment, used);
    }

    [Fact]
    public async Task DirectGrant_EnrolledUserNeedsTotp_UnenrolledUserDoesNot()
    {
        var (email, _, enrolment) = await EnrolledUserAsync();
        var unenrolled = NewEmail();
        await _fixture.CreateUserAsync(unenrolled, Password);

        var withoutTotp = await PasswordGrantAsync(email);
        var withTotp = await PasswordGrantAsync(email, enrolment.Totp.Next());
        var plain = await PasswordGrantAsync(unenrolled);

        Assert.Equal(HttpStatusCode.BadRequest, withoutTotp.StatusCode);
        Assert.Equal("invalid_grant", (await withoutTotp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.OK, withTotp.StatusCode);
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
    }

    [Fact]
    public async Task GoogleBrokerLogin_NeverAsksForOtp_AlsoForAPasswordUserWithOtp()
    {
        await using var google = await FakeGoogle.CreateAsync(_fixture);
        var newGoogleUser = await google.CreateUserAsync(NewEmail());
        using (var browser = new KeycloakBrowser())
        {
            await browser.GetAsync(KeycloakBrowser.AuthUrl(_fixture.KeycloakAuthority, "&kc_idp_hint=google"));
            await browser.SubmitLoginAsync(newGoogleUser.Email, FakeGoogle.Password);
            Assert.False(string.IsNullOrEmpty(browser.Code), browser.Html);
        }
        var created = (await _fixture.FindUserAsync(newGoogleUser.Email))!.Value;
        Assert.Empty(created.GetProperty("requiredActions").EnumerateArray());
        Assert.DoesNotContain(await _fixture.GetUserCredentialsAsync(created.GetProperty("id").GetString()!), c => c.GetProperty("type").GetString() == "otp");

        var (email, userId, _) = await EnrolledUserAsync();
        var linked = await google.CreateUserAsync(email);
        using var admin = await _fixture.CreateAdminClientAsync();
        (await admin.PostAsJsonAsync($"{_fixture.AdminRealmUrl}/users/{userId}/federated-identity/google",
            new { identityProvider = "google", userId = linked.Id, userName = email })).EnsureSuccessStatusCode();

        using var linkedBrowser = new KeycloakBrowser();
        await linkedBrowser.GetAsync(KeycloakBrowser.AuthUrl(_fixture.KeycloakAuthority, "&kc_idp_hint=google"));
        await linkedBrowser.SubmitLoginAsync(email, FakeGoogle.Password);
        Assert.False(string.IsNullOrEmpty(linkedBrowser.Code), linkedBrowser.Html);
    }

    [Fact]
    public async Task PasswordReset_ByEmail_KeepsOtp_AndAsksForTheCode()
    {
        var mailpit = new ContainerBuilder("axllent/mailpit:v1.30.0")
            .WithNetwork(_fixture.Network)
            .WithNetworkAliases("mailpit")
            .WithPortBinding(8025, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPath("/api/v1/messages").ForPort(8025)))
            .Build();
        await mailpit.StartAsync();
        await using var _ = mailpit;
        var realm = await AdminGetAsync("");
        try
        {
            await _fixture.UpdateRealmAsync(new
            {
                resetPasswordAllowed = true,
                smtpServer = new Dictionary<string, string> { ["host"] = "mailpit", ["port"] = "1025", ["from"] = "noreply@openksef.test" },
            });
            var (email, userId, enrolment) = await EnrolledUserAsync();
            using var browser = new KeycloakBrowser();
            await browser.GetAsync(KeycloakBrowser.AuthUrl(_fixture.KeycloakAuthority));
            await browser.GetAsync(new Uri(browser.Url, browser.Link("reset-credentials")).ToString());
            await browser.SubmitAsync("kc-reset-password-form", ("username", email));

            var link = await ResetLinkAsync($"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(8025)}", email);
            await browser.GetAsync(link);
            Assert.True(browser.OnPage("kc-otp-login-form"), browser.Html);
            await browser.SubmitAsync("kc-otp-login-form", ("otp", enrolment.Totp.Next()));
            await browser.SubmitAsync("kc-passwd-update-form", ("password-new", "Changed1234!"), ("password-confirm", "Changed1234!"));

            Assert.False(string.IsNullOrEmpty(browser.Code), browser.Html);
            Assert.Contains(await _fixture.GetUserCredentialsAsync(userId), c => c.GetProperty("type").GetString() == "otp");
        }
        finally
        {
            await _fixture.UpdateRealmAsync(new { resetPasswordAllowed = (bool)realm["resetPasswordAllowed"]!, smtpServer = realm["smtpServer"] ?? new JsonObject() });
        }
    }

    private static async Task<string> ResetLinkAsync(string mailpitUrl, string email)
    {
        using var http = new HttpClient();
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var search = await http.GetFromJsonAsync<JsonElement>($"{mailpitUrl}/api/v1/search?query={Uri.EscapeDataString($"to:{email}")}");
            var messages = search.GetProperty("messages");
            if (messages.GetArrayLength() > 0)
            {
                var message = await http.GetFromJsonAsync<JsonElement>($"{mailpitUrl}/api/v1/message/{messages[0].GetProperty("ID").GetString()}");
                return System.Text.RegularExpressions.Regex.Match(message.GetProperty("Text").GetString()!, @"https?://\S+action-token\S+").Value;
            }
            await Task.Delay(500);
        }
        throw new TimeoutException($"No reset e-mail for {email}");
    }

    private async Task<string> LoginWithRecoveryCodeAsync(string email, TwoFactorEnrolment enrolment, string? expectRejected)
    {
        using var browser = new KeycloakBrowser();
        await browser.GetAsync(KeycloakBrowser.AuthUrl(_fixture.KeycloakAuthority));
        await browser.SubmitLoginAsync(email, Password);
        await browser.SubmitAsync("kc-select-try-another-way-form");
        var recoveryExecution = (await ExecutionsAsync(SystemSetupService.BrowserFlowAlias))
            .Single(e => (string?)e["providerId"] == "auth-recovery-authn-code-form")["id"]!.ToString();
        await browser.SubmitAsync("kc-select-credential-form", ("authenticationExecution", recoveryExecution));
        Assert.True(browser.OnPage("kc-recovery-code-login-form"), browser.Html);

        if (expectRejected is not null)
        {
            await browser.SubmitAsync("kc-recovery-code-login-form", ("recoveryCodeInput", expectRejected));
            Assert.Null(browser.Code);
            Assert.True(browser.OnPage("kc-recovery-code-login-form"));
            return expectRejected;
        }

        var number = int.Parse(System.Text.RegularExpressions.Regex.Match(browser.Html, @"#(\d+)").Groups[1].Value);
        var code = enrolment.RecoveryCodes[number - 1];
        await browser.SubmitAsync("kc-recovery-code-login-form", ("recoveryCodeInput", code));
        Assert.False(string.IsNullOrEmpty(browser.Code), browser.Html);
        return code;
    }

    private async Task<(string Email, string UserId, TwoFactorEnrolment Enrolment)> EnrolledUserAsync()
    {
        var email = NewEmail();
        var userId = await _fixture.CreateUserAsync(email, Password);
        using var browser = new KeycloakBrowser();
        await browser.GetAsync(KeycloakBrowser.AuthUrl(_fixture.KeycloakAuthority));
        await browser.SubmitLoginAsync(email, Password);
        var enrolment = await browser.EnrolAsync();
        Assert.False(string.IsNullOrEmpty(browser.Code));
        return (email, userId, enrolment);
    }

    private async Task<HttpResponseMessage> PasswordGrantAsync(string email, string? totp = null)
    {
        using var http = new HttpClient();
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "openksef-mobile",
            ["username"] = email,
            ["password"] = Password,
        };
        if (totp is not null)
            form["totp"] = totp;
        return await http.PostAsync($"{_fixture.KeycloakAuthority}/protocol/openid-connect/token", new FormUrlEncodedContent(form));
    }

    private async Task<JsonNode> TwoFactorStateAsync()
    {
        var realm = await AdminGetAsync("");
        return new JsonObject
        {
            ["realm"] = realm.DeepClone(),
            ["flows"] = (await AdminGetAsync("/authentication/flows")).DeepClone(),
            ["browser"] = new JsonArray((await ExecutionsAsync(SystemSetupService.BrowserFlowAlias)).Select(e => e.DeepClone()).ToArray()),
            ["registration"] = new JsonArray((await ExecutionsAsync(SystemSetupService.RegistrationFlowAlias)).Select(e => e.DeepClone()).ToArray()),
            ["reset"] = new JsonArray((await ExecutionsAsync(SystemSetupService.ResetCredentialsFlowAlias)).Select(e => e.DeepClone()).ToArray()),
            ["requiredActions"] = (await AdminGetAsync("/authentication/required-actions")).DeepClone(),
        };
    }

    private async Task<List<JsonNode>> ExecutionsAsync(string flowAlias) =>
        (await AdminGetAsync($"/authentication/flows/{Uri.EscapeDataString(flowAlias)}/executions")).AsArray().Select(e => e!).ToList();

    private async Task<JsonNode> AdminGetAsync(string path)
    {
        using var http = await _fixture.CreateAdminClientAsync();
        return JsonNode.Parse(await http.GetStringAsync($"{_fixture.AdminRealmUrl}{path}"))!;
    }

    private async Task SaveSettingsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISystemSettingsService>().UpdateSettingsAsync(
            new SettingsUpdateRequest { KcAdminUsername = "admin", KcAdminPassword = "admin" },
            await _fixture.GetAdminTokenAsync());
        Assert.True(result.Success, result.Error);
    }

    private static string NewEmail() => $"t726-{Guid.NewGuid():N}@openksef.test";
}
