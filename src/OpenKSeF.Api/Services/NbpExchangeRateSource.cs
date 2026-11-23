using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Api.Services;

/// <summary>NBP table A mid rates (api.nbp.pl). A range query covers weekends and holidays without a calendar: NBP lists working days only. When the range query fails, single-date queries walk back day by day.</summary>
public class NbpExchangeRateSource(HttpClient http, TimeProvider timeProvider, ILogger<NbpExchangeRateSource> logger) : IExchangeRateSource
{
    public static readonly Uri BaseAddress = new("https://api.nbp.pl/");

    private const int LookbackDays = 14;
    private const int FallbackDays = 7;
    private static readonly DateOnly FirstTable = new(2002, 1, 2);

    public async Task<ExchangeRateQuote?> LatestMidBeforeAsync(string currency, DateOnly date, CancellationToken ct = default)
    {
        if (date <= FirstTable || date.AddDays(-1) > IssuedInvoice.PolandDate(timeProvider.GetUtcNow()))
            return null;

        var prefix = $"api/exchangerates/rates/a/{currency.ToLowerInvariant()}";
        var to = date.AddDays(-1);

        var (answered, quote) = await FetchAsync($"{prefix}/{Format(to.AddDays(-LookbackDays))}/{Format(to)}/?format=json", ct);
        if (answered)
            return quote;

        for (var day = to; day > to.AddDays(-FallbackDays); day = day.AddDays(-1))
        {
            (answered, quote) = await FetchAsync($"{prefix}/{Format(day)}/?format=json", ct);
            if (!answered || quote is not null)
                return quote;
        }

        return null;
    }

    private async Task<(bool Answered, ExchangeRateQuote? Quote)> FetchAsync(string path, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(path, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return (true, null);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("NBP rate request returned {StatusCode}", (int)response.StatusCode);
                return (false, null);
            }

            var table = await response.Content.ReadFromJsonAsync<RateTable>(ct);
            var latest = table?.Rates?.MaxBy(r => r.EffectiveDate);
            return (true, latest is { Mid: > 0 } ? new ExchangeRateQuote(latest.Mid, latest.EffectiveDate) : null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "NBP rate request failed");
            return (false, null);
        }
    }

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record RateTable(IReadOnlyList<Rate>? Rates);

    private sealed record Rate(DateOnly EffectiveDate, decimal Mid);
}
