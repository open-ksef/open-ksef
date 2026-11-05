namespace OpenKSeF.Domain.Abstractions;

/// <summary>Proposes an exchange rate for a draft. A failure or an unknown currency gives null, and the user types the rate.</summary>
public interface IExchangeRateSource
{
    Task<ExchangeRateQuote?> LatestMidBeforeAsync(string currency, DateOnly date, CancellationToken ct = default);
}

public sealed record ExchangeRateQuote(decimal Rate, DateOnly TableDate);
