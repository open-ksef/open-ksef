namespace OpenKSeF.Domain.Services;

public interface ISystemConfigService
{
    bool IsInitialized { get; }
    string? GetValue(string key);
    Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default);
    Task SetValuesAsync(IDictionary<string, string> values, CancellationToken cancellationToken = default);
    Task RefreshCacheAsync(CancellationToken cancellationToken = default);
}
