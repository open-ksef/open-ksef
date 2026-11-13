using OpenKSeF.Api.IntegrationTests.Infrastructure;

namespace OpenKSeF.Api.IntegrationTests;

public class TestNipTests
{
    [Fact]
    public void New_100kCalls_NeverRepeats()
    {
        var nips = Enumerable.Range(0, 100_000).Select(_ => TestNip.New()).ToList();

        Assert.Equal(nips.Count, nips.Distinct().Count());
        Assert.All(nips, n => Assert.StartsWith("999", n));
    }

    [Fact]
    public void New_KeepsPrefix()
    {
        Assert.StartsWith("012", TestNip.New("012"));
    }
}
