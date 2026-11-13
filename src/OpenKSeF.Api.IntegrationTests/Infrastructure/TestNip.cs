namespace OpenKSeF.Api.IntegrationTests.Infrastructure;

/// <summary>NIP with a valid mod-11 checksum, which tenant creation requires, unique per process for each prefix.</summary>
public static class TestNip
{
    private static readonly int[] Weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
    private static long _next = Random.Shared.Next(0, 1_000_000);

    public static string New(string prefix = "999")
    {
        var digits = 9 - prefix.Length;
        var range = (long)Math.Pow(10, digits);
        while (true)
        {
            var body = prefix + (Interlocked.Increment(ref _next) % range).ToString($"D{digits}");
            var checksum = body.Select((c, i) => (c - '0') * Weights[i]).Sum() % 11;
            if (checksum < 10)
                return body + checksum;
        }
    }
}
