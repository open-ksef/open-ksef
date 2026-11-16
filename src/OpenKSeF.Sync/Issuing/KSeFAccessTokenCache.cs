using System.Collections.Concurrent;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Sync.Issuing;

/// <summary>KSeF access tokens of issuing logins, reused across send cycles while the credential and environment stay the same.</summary>
public sealed class KSeFAccessTokenCache
{
    private readonly ConcurrentDictionary<Guid, (object Key, KSeFSession Login)> _tokens = new();

    public string? Find(Guid tenantId, string environment, KSeFCredential credential, DateTime validAtLeastUntil) =>
        _tokens.TryGetValue(tenantId, out var entry) && entry.Key.Equals(Key(environment, credential)) && entry.Login.ExpiresAtUtc > validAtLeastUntil
            ? entry.Login.Token
            : null;

    public void Store(Guid tenantId, string environment, KSeFCredential credential, KSeFSession login) =>
        _tokens[tenantId] = (Key(environment, credential), login);

    public void Forget(Guid tenantId) => _tokens.TryRemove(tenantId, out _);

    private static object Key(string environment, KSeFCredential credential) =>
        (environment, credential.Id, credential.Type, credential.EncryptedToken, credential.EncryptedCertificateData);
}
