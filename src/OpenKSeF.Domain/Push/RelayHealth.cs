namespace OpenKSeF.Domain.Push;

/// <summary>In-process memo of the last failed Relay send; the next successful Relay send clears it.</summary>
public sealed class RelayHealth
{
    private volatile RelayFailure? _lastFailure;

    public RelayFailure? LastFailure => _lastFailure;

    public void Failed(int? statusCode) => _lastFailure = new RelayFailure(statusCode, DateTimeOffset.UtcNow);

    public void Succeeded() => _lastFailure = null;
}

public sealed record RelayFailure(int? StatusCode, DateTimeOffset At);
