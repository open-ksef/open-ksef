using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKSeF.Api.Services;

namespace OpenKSeF.Api.Tests.Services;

public class NbpExchangeRateSourceTests
{
    private static readonly (string Date, decimal Mid)[] WorkingDays =
    [
        ("2026-11-02", 5.1002m),
        ("2026-11-03", 5.1003m),
        ("2026-11-04", 5.1004m),
        ("2026-11-05", 5.1005m),
        ("2026-11-06", 5.1006m),
        ("2026-11-09", 5.1009m),
        ("2026-11-10", 5.1010m),
        ("2026-11-12", 5.1012m)
    ];

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage NbpRange(HttpRequestMessage request)
    {
        var range = Regex.Match(request.RequestUri!.AbsolutePath, @"/(\d{4}-\d{2}-\d{2})/(\d{4}-\d{2}-\d{2})/?$");
        var rows = WorkingDays
            .Where(d => string.CompareOrdinal(d.Date, range.Groups[1].Value) >= 0 && string.CompareOrdinal(d.Date, range.Groups[2].Value) <= 0)
            .Select(d => $$"""{"no":"1/A/NBP/2026","effectiveDate":"{{d.Date}}","mid":{{d.Mid.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""")
            .ToList();
        return rows.Count == 0
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Json($$"""{"table":"A","currency":"funt szterling","code":"GBP","rates":[{{string.Join(",", rows)}}]}""");
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (NbpExchangeRateSource Source, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new NbpExchangeRateSource(new HttpClient(handler) { BaseAddress = NbpExchangeRateSource.BaseAddress }, NullLogger<NbpExchangeRateSource>.Instance), handler);
    }

    [Theory]
    [InlineData("2026-11-06", "2026-11-05", 5.1005)]
    [InlineData("2026-11-09", "2026-11-06", 5.1006)]
    [InlineData("2026-11-08", "2026-11-06", 5.1006)]
    [InlineData("2026-11-12", "2026-11-10", 5.1010)]
    public async Task ReturnsLastWorkingDayBeforeTheDate(string date, string tableDate, double mid)
    {
        var (source, _) = Create(NbpRange);

        var quote = await source.LatestMidBeforeAsync("GBP", DateOnly.Parse(date));

        Assert.Equal(DateOnly.Parse(tableDate), quote!.TableDate);
        Assert.Equal((decimal)mid, quote.Rate);
    }

    [Fact]
    public async Task AsksTableAForTheCurrencyUpToTheDayBefore()
    {
        var (source, handler) = Create(NbpRange);

        await source.LatestMidBeforeAsync("GBP", new DateOnly(2026, 11, 6));

        Assert.Equal("https://api.nbp.pl/api/exchangerates/rates/a/gbp/2026-10-22/2026-11-05/?format=json", Assert.Single(handler.Requests).ToString());
    }

    [Fact]
    public async Task UnknownCurrencyOrNoTable_GivesNull()
    {
        var (source, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await source.LatestMidBeforeAsync("XXX", new DateOnly(2026, 11, 6)));
    }

    [Fact]
    public async Task ServerError_GivesNull()
    {
        var (source, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        Assert.Null(await source.LatestMidBeforeAsync("GBP", new DateOnly(2026, 11, 6)));
    }

    [Fact]
    public async Task NetworkFailure_GivesNull()
    {
        var (source, _) = Create(_ => throw new HttpRequestException("down"));

        Assert.Null(await source.LatestMidBeforeAsync("GBP", new DateOnly(2026, 11, 6)));
    }

    [Fact]
    public async Task Timeout_GivesNull()
    {
        var (source, _) = Create(_ => throw new TaskCanceledException("timeout"));

        Assert.Null(await source.LatestMidBeforeAsync("GBP", new DateOnly(2026, 11, 6)));
    }

    [Fact]
    public async Task MalformedBody_GivesNull()
    {
        var (source, _) = Create(_ => Json("<html>maintenance</html>"));

        Assert.Null(await source.LatestMidBeforeAsync("GBP", new DateOnly(2026, 11, 6)));
    }

    [Fact]
    public async Task CallerCancellation_IsNotSwallowed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var (source, _) = Create(_ => throw new TaskCanceledException("caller"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.LatestMidBeforeAsync("GBP", new DateOnly(2026, 11, 6), cts.Token));
    }
}
