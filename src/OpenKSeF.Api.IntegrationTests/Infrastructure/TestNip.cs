namespace OpenKSeF.Api.IntegrationTests.Infrastructure;

/// <summary>Random NIP with a valid mod-11 checksum, which tenant creation requires.</summary>
public static class TestNip
{
    private static readonly int[] Weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];

    public static string New(string prefix = "999")
    {
        while (true)
        {
            var body = prefix + Random.Shared.Next(0, 1_000_000).ToString("D6");
            var checksum = body.Select((c, i) => (c - '0') * Weights[i]).Sum() % 11;
            if (checksum < 10)
                return body + checksum;
        }
    }
}
