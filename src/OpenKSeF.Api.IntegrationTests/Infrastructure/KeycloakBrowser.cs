using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenKSeF.Api.IntegrationTests.Infrastructure;

/// <summary>Walks Keycloak's hosted login pages over plain HTTP: follows redirects, keeps cookies per path, submits forms by id.</summary>
public sealed partial class KeycloakBrowser(string redirectUri = KeycloakBrowser.PortalRedirectUri) : IDisposable
{
    public const string PortalRedirectUri = "http://localhost/callback";

    // Keycloak marks its cookies Secure, so a cookie container would not send them back over http.
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private readonly Dictionary<(string Name, string Path), string> _cookies = [];

    public string Html { get; private set; } = "";
    public Uri Url { get; private set; } = new("http://localhost/");
    public string? Code { get; private set; }

    public bool OnPage(string formId) => Html.Contains($"id=\"{formId}\"");

    public static string AuthUrl(string authority, string extra = "") =>
        $"{authority}/protocol/openid-connect/auth?client_id=openksef-portal-web&response_type=code&scope=openid" +
        $"&redirect_uri={Uri.EscapeDataString(PortalRedirectUri)}&state=t726{extra}";

    public Task GetAsync(string url) => NavigateAsync(new HttpRequestMessage(HttpMethod.Get, url));

    public Task SubmitAsync(string formId, params (string Name, string Value)[] fields)
    {
        var form = Form(formId);
        var values = HiddenInputs(form).ToDictionary(i => i.Name, i => i.Value);
        foreach (var (name, value) in fields)
            values[name] = value;
        var action = WebUtility.HtmlDecode(FormAction().Match(form).Groups[1].Value);
        return NavigateAsync(new HttpRequestMessage(HttpMethod.Post, new Uri(Url, action)) { Content = new FormUrlEncodedContent(values) });
    }

    public Task SubmitLoginAsync(string username, string password) =>
        SubmitAsync("kc-form-login", ("username", username), ("password", password));

    public string Hidden(string formId, string name) => HiddenInputs(Form(formId)).Single(i => i.Name == name).Value;

    public string Link(string hrefContains) =>
        WebUtility.HtmlDecode(Regex.Match(Html, $"href=\"([^\"]*{Regex.Escape(hrefContains)}[^\"]*)\"").Groups[1].Value);

    public async Task<TwoFactorEnrolment> EnrolAsync()
    {
        Assert.True(OnPage("kc-totp-settings-form"), "expected the TOTP setup page");
        var totp = new Totp(Hidden("kc-totp-settings-form", "totpSecret"));
        await SubmitAsync("kc-totp-settings-form", ("totp", totp.Next()), ("userLabel", "Telefon"));
        Assert.True(OnPage("kc-recovery-codes-settings-form"), "expected the recovery codes page");
        var codes = Hidden("kc-recovery-codes-settings-form", "generatedRecoveryAuthnCodes").Split(',');
        await SubmitAsync("kc-recovery-codes-settings-form");
        return new TwoFactorEnrolment(totp, codes);
    }

    private string Form(string formId)
    {
        var match = Regex.Match(Html, $"<form[^>]*id=\"{Regex.Escape(formId)}\"[^>]*>.*?</form>", RegexOptions.Singleline);
        Assert.True(match.Success, $"form {formId} not on page {Url}:\n{Html}");
        return match.Value;
    }

    private static IEnumerable<(string Name, string Value)> HiddenInputs(string form) =>
        InputTag().Matches(form).Select(m => m.Value)
            .Where(tag => tag.Contains("type=\"hidden\""))
            .Select(tag => (Attribute(tag, "name"), WebUtility.HtmlDecode(Attribute(tag, "value"))));

    private static string Attribute(string tag, string name) => Regex.Match(tag, $"\\s{name}=\"([^\"]*)\"").Groups[1].Value;

    private async Task NavigateAsync(HttpRequestMessage request)
    {
        for (var hop = 0; hop < 20; hop++)
        {
            Url = request.RequestUri!;
            var cookies = _cookies.Where(c => Url.AbsolutePath.StartsWith(c.Key.Path)).Select(c => $"{c.Key.Name}={c.Value}").ToList();
            if (cookies.Count > 0)
                request.Headers.Add("Cookie", string.Join("; ", cookies));
            using var response = await _http.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                foreach (var setCookie in setCookies)
                    StoreCookie(setCookie);

            if (response.StatusCode is not (HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.MovedPermanently or HttpStatusCode.TemporaryRedirect))
            {
                Html = await response.Content.ReadAsStringAsync();
                return;
            }

            var location = response.Headers.Location!.IsAbsoluteUri ? response.Headers.Location : new Uri(Url, response.Headers.Location!);
            if (location.OriginalString.StartsWith(redirectUri))
            {
                Code = Uri.UnescapeDataString(Regex.Match(location.OriginalString, "[?&]code=([^&]+)").Groups[1].Value);
                Html = "";
                return;
            }
            request = new HttpRequestMessage(HttpMethod.Get, location);
        }

        throw new InvalidOperationException($"Too many redirects from {Url}");
    }

    private void StoreCookie(string setCookie)
    {
        var parts = setCookie.Split(';').Select(p => p.Trim()).ToArray();
        var separator = parts[0].IndexOf('=');
        var name = parts[0][..separator];
        var path = parts.FirstOrDefault(p => p.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))?[5..] ?? "/";
        var expired = parts.Any(p => p.Equals("Max-Age=0", StringComparison.OrdinalIgnoreCase)) || parts[0].Length == separator + 1;
        if (expired)
            _cookies.Remove((name, path));
        else
            _cookies[(name, path)] = parts[0][(separator + 1)..];
    }

    public void Dispose() => _http.Dispose();

    [GeneratedRegex("<form[^>]*action=\"([^\"]*)\"")]
    private static partial Regex FormAction();

    [GeneratedRegex("<input[^>]*>")]
    private static partial Regex InputTag();
}

public sealed record TwoFactorEnrolment(Totp Totp, string[] RecoveryCodes);

/// <summary>RFC 6238 SHA1/6/30 over the raw Keycloak secret; never repeats a time step, because Keycloak rejects a reused code.</summary>
public sealed class Totp(string secret)
{
    private readonly HashSet<long> _used = [];

    public string Next()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var step = new[] { now, now + 1, now - 1 }.First(s => !_used.Contains(s));
        _used.Add(step);
        return Code(step);
    }

    private string Code(long step)
    {
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counter);
        var hash = HMACSHA1.HashData(Encoding.UTF8.GetBytes(secret), counter);
        var offset = hash[^1] & 0xf;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }
}
