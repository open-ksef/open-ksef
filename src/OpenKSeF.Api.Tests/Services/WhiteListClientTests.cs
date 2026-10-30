using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;

namespace OpenKSeF.Api.Tests.Services;

public class WhiteListClientTests
{
    private const string Nip = "5261040828";

    private const string FoundJson = """
        {"result":{"subject":{"name":"GŁÓWNY URZĄD STATYSTYCZNY","nip":"5261040828","statusVat":"Czynny",
        "residenceAddress":"ALEJA NIEPODLEGŁOŚCI 208, 00-925 WARSZAWA","workingAddress":null,
        "accountNumbers":["79101010100024791398000000","31101010100024791391200000"]},"requestId":"x"}}
        """;

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (WhiteListClient Client, StubHandler Handler, FixedTime Time) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var time = new FixedTime(new DateTimeOffset(2026, 10, 30, 23, 30, 0, TimeSpan.Zero));
        var client = new WhiteListClient(
            new HttpClient(handler) { BaseAddress = WhiteListClient.BaseAddress },
            new MemoryCache(new MemoryCacheOptions()), time, NullLogger<WhiteListClient>.Instance);
        return (client, handler, time);
    }

    [Fact]
    public async Task Lookup_Found_MapsSubjectAndSplitsAddressAtLastComma()
    {
        var (client, handler, _) = Create(_ => Json(HttpStatusCode.OK, FoundJson));

        var result = await client.LookupAsync(Nip, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("GŁÓWNY URZĄD STATYSTYCZNY", result.Name);
        Assert.Equal("ALEJA NIEPODLEGŁOŚCI 208", result.AddressLine1);
        Assert.Equal("00-925 WARSZAWA", result.AddressLine2);
        Assert.Equal("Czynny", result.VatStatus);
        Assert.Equal(2, result.BankAccounts.Count);
        Assert.Equal("https://wl-api.mf.gov.pl/api/search/nip/5261040828?date=2026-10-31", Assert.Single(handler.Requests).ToString());
    }

    [Fact]
    public async Task Lookup_PrefersWorkingAddress()
    {
        var json = FoundJson.Replace("\"workingAddress\":null", "\"workingAddress\":\"UL. ROBOCZA 1, LOK. 2, 00-001 WARSZAWA\"");
        var (client, _, _) = Create(_ => Json(HttpStatusCode.OK, json));

        var result = await client.LookupAsync(Nip, CancellationToken.None);

        Assert.Equal(("UL. ROBOCZA 1, LOK. 2", "00-001 WARSZAWA"), (result!.AddressLine1, result.AddressLine2));
    }

    [Fact]
    public async Task Lookup_NotOnList_ReturnsNull()
    {
        var (client, _, _) = Create(_ => Json(HttpStatusCode.OK, """{"result":{"subject":null,"requestId":"x"}}"""));

        Assert.Null(await client.LookupAsync(Nip, CancellationToken.None));
    }

    [Fact]
    public async Task Lookup_SameNipSameDay_ServedFromCache_NextDayRefetches()
    {
        var (client, handler, time) = Create(_ => Json(HttpStatusCode.OK, FoundJson));

        await client.LookupAsync(Nip, CancellationToken.None);
        await client.LookupAsync(Nip, CancellationToken.None);
        Assert.Single(handler.Requests);

        time.Now = time.Now.AddDays(1);
        await client.LookupAsync(Nip, CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Lookup_MfDownOrThrottled_ThrowsUnavailable_AndDoesNotCache(HttpStatusCode status)
    {
        var (client, handler, _) = Create(_ => Json(status, """{"message":"x","code":"WL-191"}"""));

        await Assert.ThrowsAsync<WhiteListUnavailableException>(() => client.LookupAsync(Nip, CancellationToken.None));
        await Assert.ThrowsAsync<WhiteListUnavailableException>(() => client.LookupAsync(Nip, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Lookup_NetworkError_ThrowsUnavailable()
    {
        var (client, _, _) = Create(_ => throw new HttpRequestException("down"));

        await Assert.ThrowsAsync<WhiteListUnavailableException>(() => client.LookupAsync(Nip, CancellationToken.None));
    }

    [Fact]
    public async Task Lookup_MfRejectsNip_ThrowsInvalid()
    {
        var (client, _, _) = Create(_ => Json(HttpStatusCode.BadRequest, """{"message":"Nieprawidłowy NIP.","code":"WL-115"}"""));

        await Assert.ThrowsAsync<WhiteListInvalidNipException>(() => client.LookupAsync(Nip, CancellationToken.None));
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("12345")]
    [InlineData("abcdefghij")]
    public async Task Controller_InvalidNip_Returns400WithoutCallingMf(string nip)
    {
        var (client, handler, _) = Create(_ => Json(HttpStatusCode.OK, FoundJson));

        var result = await new NipLookupController(client).Get(nip, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Niepoprawny NIP", Assert.IsType<ValidationEnvelope>(bad.Value).Errors.Single().Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Controller_Found_Returns200_NotFound_Returns404_Down_Returns503()
    {
        var found = await new NipLookupController(Create(_ => Json(HttpStatusCode.OK, FoundJson)).Client).Get("526-104-08-28", CancellationToken.None);
        Assert.Equal("5261040828", Assert.IsType<NipLookupResponse>(Assert.IsType<OkObjectResult>(found).Value).Nip);

        var missing = await new NipLookupController(Create(_ => Json(HttpStatusCode.OK, """{"result":{"subject":null}}""")).Client).Get(Nip, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(missing);

        var down = await new NipLookupController(Create(_ => Json(HttpStatusCode.TooManyRequests, "{}")).Client).Get(Nip, CancellationToken.None);
        var status = Assert.IsType<ObjectResult>(down);
        Assert.Equal(503, status.StatusCode);
        Assert.Equal("Wyszukiwarka MF niedostępna, wpisz dane ręcznie", Assert.IsType<ValidationEnvelope>(status.Value).Errors.Single().Message);
    }

    [Fact]
    public void Controller_RequiresAuthAndRateLimit()
    {
        Assert.NotEmpty(typeof(NipLookupController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        var limit = Assert.Single(typeof(NipLookupController).GetCustomAttributes(typeof(EnableRateLimitingAttribute), true));
        Assert.Equal(NipLookupController.RateLimitPolicy, ((EnableRateLimitingAttribute)limit).PolicyName);
    }
}
