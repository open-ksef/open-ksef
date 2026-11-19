using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Models;
using KSeF.Client.Core.Models.Authorization;
using OpenKSeF.Sync.Issuing;

namespace OpenKSeF.Sync;

/// <summary>AuthCoordinator polls 60 s for status 200 and hides a rejection in a TimeoutException; this stops the poll on the real status.</summary>
internal sealed class RejectionAwareAuthClient(IAuthorizationClient inner) : IAuthorizationClient
{
    public string? ReferenceNumber { get; private set; }

    public async Task<AuthStatus> GetAuthStatusAsync(string authOperationReferenceNumber, string authenticationToken, CancellationToken cancellationToken = default)
    {
        var status = await inner.GetAuthStatusAsync(authOperationReferenceNumber, authenticationToken, cancellationToken);
        KSeFInvoiceSubmitter.ThrowIfAuthRejected(status.Status);
        return status;
    }

    public Task<AuthenticationChallengeResponse> GetAuthChallengeAsync(CancellationToken cancellationToken = default) =>
        inner.GetAuthChallengeAsync(cancellationToken);

    public Task<SignatureResponse> SubmitXadesAuthRequestAsync(string signedXML, bool verifyCertificateChain = false, bool enforceXadesCompliance = false, CancellationToken cancellationToken = default) =>
        inner.SubmitXadesAuthRequestAsync(signedXML, verifyCertificateChain, enforceXadesCompliance, cancellationToken);

    public async Task<SignatureResponse> SubmitKsefTokenAuthRequestAsync(AuthenticationKsefTokenRequest requestPayload, CancellationToken cancellationToken = default)
    {
        var response = await inner.SubmitKsefTokenAuthRequestAsync(requestPayload, cancellationToken);
        ReferenceNumber = response.ReferenceNumber;
        return response;
    }

    public Task<AuthenticationOperationStatusResponse> GetAccessTokenAsync(string authenticationToken, CancellationToken cancellationToken = default) =>
        inner.GetAccessTokenAsync(authenticationToken, cancellationToken);

    public Task<RefreshTokenResponse> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        inner.RefreshAccessTokenAsync(refreshToken, cancellationToken);
}
