using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenKSeF.PushRelay.Tests;

public sealed class InstanceStoreTests : IDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "relay-tests-" + Guid.NewGuid().ToString("N"));
    private readonly InstanceStore _store;

    public InstanceStoreTests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Relay:DataDir"] = _dataDir }).Build();
        _store = new InstanceStore(config, NullLogger<InstanceStore>.Instance);
    }

    public void Dispose()
    {
        _store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dataDir, true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task ReadsDuringConcurrentRegistrations_NeverThrow()
    {
        var (instanceId, apiKey) = await _store.RegisterAsync("https://instance.invalid");

        var writes = Enumerable.Range(0, 200).Select(i => Task.Run(() => _store.RegisterAsync("https://instance.invalid", i % 2 == 0 ? null : Guid.NewGuid().ToString("N"))));
        var reads = Enumerable.Range(0, 2000).Select(i => Task.Run(async () =>
        {
            if (i % 50 == 0)
                await _store.ListAsync();
            Assert.Equal(apiKey, (await _store.GetAsync(instanceId))!.ApiKey);
            await _store.UpdateLastSeenAsync(instanceId);
        }));

        await Task.WhenAll(writes.Cast<Task>().Concat(reads));

        Assert.Equal(201, (await _store.ListAsync()).Count);
    }
}
