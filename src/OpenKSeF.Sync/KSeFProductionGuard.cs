namespace OpenKSeF.Sync;

/// <summary>Stops HTTP calls to every production KSeF host. Used by the integration tests and by stacks that set KSeF:ForbidProduction.</summary>
public sealed class KSeFProductionGuard : DelegatingHandler
{
    private static int _blocked;

    public static int Blocked => Volatile.Read(ref _blocked);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!IsProductionHost(request.RequestUri?.Host ?? ""))
            return base.SendAsync(request, cancellationToken);

        Interlocked.Increment(ref _blocked);
        throw new InvalidOperationException($"Blocked a call to KSeF production: {request.RequestUri}");
    }

    private static bool IsProductionHost(string host) =>
        (host.Equals("ksef.mf.gov.pl", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".ksef.mf.gov.pl", StringComparison.OrdinalIgnoreCase))
        && !host.Contains("-test", StringComparison.OrdinalIgnoreCase)
        && !host.Contains("-demo", StringComparison.OrdinalIgnoreCase);
}
