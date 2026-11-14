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
        var resetTwoFactor = reset.Where(e => (string?)e["providerId"] is "auth-otp-form" or "auth-recovery-authn-code-form"
            && ParentOf(reset, e) != SystemSetupService.ResetOtpIfNoTwoFactorFlowAlias).ToList();
        Assert.Equal(2, resetTwoFactor.Count);
        Assert.All(resetTwoFactor, e => Assert.Equal("ALTERNATIVE", (string?)e["requirement"]));
        Assert.Equal("CONDITIONAL", (string?)reset.Single(e => (string?)e["displayName"] == SystemSetupService.ResetOtpIfNoTwoFactorFlowAlias)["requirement"]);
        var resetInner = await ExecutionsAsync(SystemSetupService.ResetOtpIfNoTwoFactorFlowAlias);
        Assert.Equal("REQUIRED", (string?)resetInner.Single(e => (string?)e["providerId"] == "auth-otp-form")["requirement"]);
        var resetCondition = await AdminGetAsync($"/authentication/config/{resetInner.Single(e => (string?)e["providerId"] == "conditional-sub-flow-executed")["authenticationConfig"]}");
        Assert.Equal("not-executed", (string?)resetCondition["config"]!["check_result"]);

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
    public async Task SettingsSave_RealmBornOn26_0_AddsRecoveryForm_AppliesTwoFactor_SecondSaveChangesNothing()
    {
        try
        {
            await ImportRealmWithBrowserFlowOf26_0Async();
            var shaped = await ExecutionsAsync("browser");
            var builtInOtp = shaped.Single(e => (string?)e["providerId"] == "auth-otp-form");
            Assert.Equal("REQUIRED", (string?)builtInOtp["requirement"]);
            Assert.DoesNotContain(shaped, e => (string?)e["providerId"] == "auth-recovery-authn-code-form");
            Assert.Equal(["conditional-user-configured", "auth-otp-form"],
                shaped.Where(e => ParentOf(shaped, e) == ParentOf(shaped, builtInOtp)).Select(e => (string?)e["providerId"]));

            await SaveSettingsAsync();
            var applied = await TwoFactorStateAsync();
            await SaveSettingsAsync();

            Assert.True(JsonNode.DeepEquals(applied, await TwoFactorStateAsync()));
            Assert.Equal(SystemSetupService.BrowserFlowAlias, (string?)(await AdminGetAsync(""))["browserFlow"]);
            var browser = await ExecutionsAsync(SystemSetupService.BrowserFlowAlias);
            var copiedOtp = browser.Single(e => (string?)e["providerId"] == "auth-otp-form" && ParentOf(browser, e) != SystemSetupService.OtpIfNoTwoFactorFlowAlias);
            var recovery = browser.Single(e => (string?)e["providerId"] == "auth-recovery-authn-code-form");
            Assert.Equal(ParentOf(browser, copiedOtp), ParentOf(browser, recovery));
            Assert.Equal("ALTERNATIVE", (string?)copiedOtp["requirement"]);
            Assert.Equal("ALTERNATIVE", (string?)recovery["requirement"]);

            var (email, _, enrolment) = await EnrolledUserAsync();
            using var second = new KeycloakBrowser();
            await second.GetAsync(KeycloakBrowser.AuthUrl(_fixture.KeycloakAuthority));
            await second.SubmitLoginAsync(email, Password);
            Assert.True(second.OnPage("kc-otp-login-form"), second.Html);
            await LoginWithRecoveryCodeAsync(email, enrolment, null);
        }
        finally
        {
            await _fixture.RestoreTestRealmAsync();
        }
    }

    // Built-in flows cannot be edited over the Admin API, so the realm is re-imported with the 26.0 browser flow.
    private async Task ImportRealmWithBrowserFlowOf26_0Async()
    {
        using var admin = await _fixture.CreateAdminClientAsync();
        var export = JsonNode.Parse(await (await admin.PostAsync($"{_fixture.AdminRealmUrl}/partial-export", null)).EnsureSuccessStatusCode().Content.ReadAsStringAsync())!;
        var flows = new JsonArray(export["authenticationFlows"]!.AsArray()
            .Where(f => !((string)f!["alias"]!).StartsWith("openksef ")).Select(f => f!.DeepClone()).ToArray());
        var configs = new JsonArray(export["authenticatorConfig"]!.AsArray()
            .Where(c => !((string)c!["alias"]!).StartsWith("openksef ")).Select(c => c!.DeepClone()).ToArray());

        JsonNode Flow(string alias) => flows.Single(f => (string?)f!["alias"] == alias)!;
        IEnumerable<JsonNode> Reachable(JsonNode flow) => flow["authenticationExecutions"]!.AsArray()
            .Where(e => (bool?)e!["authenticatorFlow"] == true).SelectMany(e => Reachable(Flow((string)e!["flowAlias"]!)).Prepend(Flow((string)e["flowAlias"]!)));
        var twoFactor = Reachable(Flow("browser")).Single(f => f["authenticationExecutions"]!.AsArray().Any(e => (string?)e!["authenticator"] == "auth-otp-form"));
        var kept = twoFactor["authenticationExecutions"]!.AsArray()
            .Where(e => (string?)e!["authenticator"] is "conditional-user-configured" or "auth-otp-form").Select(e => e!.DeepClone()).ToArray();
        kept.Single(e => (string?)e["authenticator"] == "auth-otp-form")["requirement"] = "REQUIRED";
        twoFactor["authenticationExecutions"] = new JsonArray(kept);

        var realm = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "realm-test.json")))!;
        realm["authenticationFlows"] = flows;
        realm["authenticatorConfig"] = configs;
        await _fixture.DeleteRealmAsync();
        (await admin.PostAsJsonAsync($"{_fixture.KeycloakBaseUrl}/admin/realms", realm)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ChangeApp_NeedsAUniqueLabel_OldOtpAndCodesDeletedByAccountApi_NewCodes_OtpPageFirst_OldCodeRejected()
    {
        var email = NewEmail();
        var userId = await _fixture.CreateUserAsync(email, Password);
        using var browser = new KeycloakBrowser();
        await browser.GetAsync(KeycloakBrowser.AuthUrl(_fixture.KeycloakAuthority));
        await browser.SubmitLoginAsync(email, Password);
        var first = await browser.EnrolAsync();
        var firstCode = browser.Code;

        await browser.GetAsync(KeycloakBrowser.AuthUrl(_fixture.KeycloakAuthority, "&kc_action=CONFIGURE_TOTP"));
        var duplicate = new Totp(browser.Hidden("kc-totp-settings-form", "totpSecret"));
        await browser.SubmitAsync("kc-totp-settings-form", ("totp", duplicate.Next()), ("userLabel", "Telefon"));
        Assert.True(browser.OnPage("kc-totp-settings-form"), browser.Html);
        var replacement = new Totp(browser.Hidden("kc-totp-settings-form", "totpSecret"));
        await browser.SubmitAsync("kc-totp-settings-form", ("totp", replacement.Next()), ("userLabel", "Telefon 2"));
        if (browser.OnPage("kc-recovery-codes-settings-form"))
            await browser.SubmitAsync("kc-recovery-codes-settings-form");
        Assert.False(string.IsNullOrEmpty(browser.Code), browser.Html);
        Assert.NotEqual(firstCode, browser.Code);

        var otps = (await _fixture.GetUserCredentialsAsync(userId)).Where(c => c.GetProperty("type").GetString() == "otp")
            .OrderBy(c => c.GetProperty("createdDate").GetInt64()).ToList();
        Assert.Equal(["Telefon", "Telefon 2"], otps.Select(c => c.GetProperty("userLabel").GetString()));

        var token = await PasswordGrantAsync(email, first.Totp.Next(), "openksef-portal-web");
        Assert.True(token.IsSuccessStatusCode, await token.Content.ReadAsStringAsync());
        using var account = new HttpClient();
        account.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        account.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
            (await token.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString());
        var deleted = await account.DeleteAsync($"{_fixture.KeycloakAuthority}/account/credentials/{otps[0].GetProperty("id").GetString()}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var left = Assert.Single(await _fixture.GetUserCredentialsAsync(userId), c => c.GetProperty("type").GetString() == "otp");
        Assert.Equal("Telefon 2", left.GetProperty("userLabel").GetString());

        var oldCodes = (await _fixture.GetUserCredentialsAsync(userId)).Single(c => c.GetProperty("type").GetString() == "recovery-authn-codes");
        Assert.Equal(HttpStatusCode.NoContent,
            (await account.DeleteAsync($"{_fixture.KeycloakAuthority}/account/credentials/{oldCodes.GetProperty("id").GetString()}")).StatusCode);
        await browser.GetAsync(KeycloakBrowser.AuthUrl(_fixture.KeycloakAuthority, "&kc_action=CONFIGURE_RECOVERY_AUTHN_CODES"));
        var newCodes = browser.Hidden("kc-recovery-codes-settings-form", "generatedRecoveryAuthnCodes").Split(',');
        await browser.SubmitAsync("kc-recovery-codes-settings-form");
        Assert.DoesNotContain(first.RecoveryCodes[0], newCodes);

        using var next = new KeycloakBrowser();
        await next.GetAsync(KeycloakBrowser.AuthUrl(_fixture.KeycloakAuthority));
        await next.SubmitLoginAsync(email, Password);
        Assert.True(next.OnPage("kc-otp-login-form"), next.Html);
        Assert.Equal(HttpStatusCode.OK, (await PasswordGrantAsync(email, replacement.Next())).StatusCode);
        await LoginWithRecoveryCodeAsync(email, first, first.RecoveryCodes[0]);
        await LoginWithRecoveryCodeAsync(email, new TwoFactorEnrolment(replacement, newCodes), null);
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
    public async Task PasswordReset_ByEmail_KeepsOtp_AndAsksForTheCode_UnenrolledUserMustSetUpTotp()
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

            var unenrolled = NewEmail();
            var unenrolledId = await _fixture.CreateUserAsync(unenrolled, Password);
            using var fresh = new KeycloakBrowser();
            await fresh.GetAsync(KeycloakBrowser.AuthUrl(_fixture.KeycloakAuthority));
            await fresh.GetAsync(new Uri(fresh.Url, fresh.Link("reset-credentials")).ToString());
            await fresh.SubmitAsync("kc-reset-password-form", ("username", unenrolled));
            await fresh.GetAsync(await ResetLinkAsync($"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(8025)}", unenrolled));
            var totpShown = false;
            for (var step = 0; step < 4 && fresh.Code is null; step++)
            {
                if (fresh.OnPage("kc-passwd-update-form"))
                    await fresh.SubmitAsync("kc-passwd-update-form", ("password-new", "Changed1234!"), ("password-confirm", "Changed1234!"));
                else if (fresh.OnPage("kc-totp-settings-form"))
                {
                    totpShown = true;
                    await fresh.SubmitAsync("kc-totp-settings-form", ("totp", new Totp(fresh.Hidden("kc-totp-settings-form", "totpSecret")).Next()), ("userLabel", "Telefon"));
                }
                else if (fresh.OnPage("kc-recovery-codes-settings-form"))
                    await fresh.SubmitAsync("kc-recovery-codes-settings-form");
            }

            Assert.True(totpShown, fresh.Html);

            Assert.False(string.IsNullOrEmpty(fresh.Code), fresh.Html);
            Assert.Contains(await _fixture.GetUserCredentialsAsync(unenrolledId), c => c.GetProperty("type").GetString() == "otp");
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

    private static string? ParentOf(List<JsonNode> executions, JsonNode execution)
    {
        var level = (int)execution["level"]! - 1;
        return level < 0 ? null : (string?)executions.Take(executions.IndexOf(execution))
            .Last(e => (int)e["level"]! == level && (bool?)e["authenticationFlow"] == true)["displayName"];
    }

    private async Task<HttpResponseMessage> PasswordGrantAsync(string email, string? totp = null, string clientId = "openksef-mobile")
    {
        using var http = new HttpClient();
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
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
