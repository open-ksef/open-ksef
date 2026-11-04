using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Api.Services;

/// <summary>NBP table A mid rates (api.nbp.pl). A range query covers weekends and holidays without a calendar: NBP lists working days only.</summary>
public class NbpExchangeRateSource(HttpClient http, TimeProvider timeProvider, ILogger<NbpExchangeRateSource> logger) : IExchangeRateSource
{
    public static readonly Uri BaseAddress = new("https://api.nbp.pl/");

    private const int LookbackDays = 14;
    private static readonly DateOnly FirstTable = new(2002, 1, 2);

    public async Task<ExchangeRateQuote?> LatestMidBeforeAsync(string currency, DateOnly date, CancellationToken ct = default)
    {
        if (date <= FirstTable || date.AddDays(-1) > IssuedInvoice.PolandDate(timeProvider.GetUtcNow()))
            return null;

        var to = date.AddDays(-1);
        var from = to.AddDays(-LookbackDays);
        var path = $"api/exchangerates/rates/a/{currency.ToLowerInvariant()}/{Format(from)}/{Format(to)}/?format=json";

        try
        {
            using var response = await http.GetAsync(path, ct);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode != HttpStatusCode.NotFound)
                    logger.LogWarning("NBP rate request returned {StatusCode}", (int)response.StatusCode);
                return null;
            }

            var table = await response.Content.ReadFromJsonAsync<RateTable>(ct);
            var latest = table?.Rates?.MaxBy(r => r.EffectiveDate);
            return latest is { Mid: > 0 } ? new ExchangeRateQuote(latest.Mid, latest.EffectiveDate) : null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "NBP rate request failed");
            return null;
        }
    }

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record RateTable(IReadOnlyList<Rate>? Rates);

    private sealed record Rate(DateOnly EffectiveDate, decimal Mid);
}
