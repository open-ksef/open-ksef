using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Api.Services;

/// <summary>MF white list (wl-api.mf.gov.pl) NIP search, cached per NIP and day to save the daily search quota.</summary>
public class WhiteListClient(HttpClient http, IMemoryCache cache, TimeProvider timeProvider, ILogger<WhiteListClient> logger)
{
    public static readonly Uri BaseAddress = new("https://wl-api.mf.gov.pl/");

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    public async Task<NipLookupResponse?> LookupAsync(string nip, CancellationToken ct)
    {
        var date = IssuedInvoice.PolandDate(timeProvider.GetUtcNow()).ToString("yyyy-MM-dd");
        var key = $"wl:{nip}:{date}";
        if (cache.TryGetValue(key, out NipLookupResponse? cached))
            return cached;

        var result = await FetchAsync(nip, date, ct);
        cache.Set(key, result, CacheDuration);
        return result;
    }

    private async Task<NipLookupResponse?> FetchAsync(string nip, string date, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync($"api/search/nip/{nip}?date={date}", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "MF white list request failed");
            throw new WhiteListUnavailableException();
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.BadRequest)
                throw new WhiteListInvalidNipException();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("MF white list returned {StatusCode}", (int)response.StatusCode);
                throw new WhiteListUnavailableException();
            }

            var body = await response.Content.ReadFromJsonAsync<SearchResponse>(ct);
            var subject = body?.Result?.Subject;
            if (subject?.Name is null)
                return null;

            var (line1, line2) = SplitAddress(subject.WorkingAddress ?? subject.ResidenceAddress);
            return new NipLookupResponse(nip, subject.Name, line1, line2, subject.StatusVat ?? string.Empty, subject.AccountNumbers ?? []);
        }
    }

    private static (string? Line1, string? Line2) SplitAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return (null, null);

        var comma = address.LastIndexOf(',');
        return comma < 0 ? (address.Trim(), null) : (address[..comma].Trim(), address[(comma + 1)..].Trim());
    }

    private sealed record SearchResponse(SearchResult? Result);

    private sealed record SearchResult(Subject? Subject);

    private sealed record Subject(
        string? Name,
        string? StatusVat,
        string? ResidenceAddress,
        string? WorkingAddress,
        IReadOnlyList<string>? AccountNumbers);
}

public sealed class WhiteListUnavailableException : Exception;

public sealed class WhiteListInvalidNipException : Exception;
