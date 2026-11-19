using System.Net;
using KSeF.Client.Core.Exceptions;
using KSeF.Client.Core.Interfaces;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models;
using KSeF.Client.Core.Models.Authorization;
using KSeF.Client.DI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;

using CirfmfKSeFClient = KSeF.Client.Core.Interfaces.Clients.IKSeFClient;

namespace OpenKSeF.Api.Tests.Services;

public class KSeFGatewayTokenCheckTests
{
    private const string Nip = "1111111111";
    private const string Reference = "20261119-EC-0000000000-0000000000-00";
    private static readonly string Token = $"{Reference}|nip-{Nip}|secret";

    private readonly CirfmfKSeFClient _client = Substitute.For<CirfmfKSeFClient>();
    private readonly ICryptographyService _cryptography = Substitute.For<ICryptographyService>();

    public KSeFGatewayTokenCheckTests()
    {
        _client.GetAuthChallengeAsync(Arg.Any<CancellationToken>()).Returns(new AuthenticationChallengeResponse { Challenge = "20261119-CR-0000000000-0000000000-00", Timestamp = DateTimeOffset.UtcNow, TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        _cryptography.EncryptKsefTokenWithRSAUsingPublicKey(Arg.Any<byte[]>()).Returns([1, 2, 3]);
        _client.SubmitKsefTokenAuthRequestAsync(Arg.Any<AuthenticationKsefTokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SignatureResponse { ReferenceNumber = "20261119-AU-0000000000-0000000000-00", AuthenticationToken = new OperationToken { Token = "operation" } });
        AuthCode(200);
        _client.GetAccessTokenAsync("operation", Arg.Any<CancellationToken>()).Returns(new AuthenticationOperationStatusResponse
        {
            AccessToken = new TokenInfo { Token = "access", ValidUntil = DateTimeOffset.UtcNow.AddMinutes(15) },
            RefreshToken = new TokenInfo { Token = "refresh", ValidUntil = DateTimeOffset.UtcNow.AddDays(7) },
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("exp-20271119|")]
    public async Task ActiveTokenOfThisNip_ReturnsItsPermissionsAndAuthReference_ClosesSession(string expirySegment)
    {
        Meta(AuthenticationKsefTokenStatus.Active, Nip, KsefTokenPermissionType.CredentialsManage, KsefTokenPermissionType.InvoiceRead);

        var grant = await Gateway().CheckTokenAsync(Nip, $"{Reference}|nip-{Nip}|{expirySegment}secret");

        Assert.NotNull(grant);
        Assert.Equal(["CredentialsManage", "InvoiceRead"], grant.Permissions);
        Assert.Equal("20261119-AU-0000000000-0000000000-00", grant.AuthReferenceNumber);
        await _client.Received(1).GetKsefTokenAsync(Reference, "access", Arg.Any<CancellationToken>());
        await _client.Received(1).RevokeCurrentSessionAsync("access", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(AuthenticationKsefTokenStatus.Revoked, Nip)]
    [InlineData(AuthenticationKsefTokenStatus.Pending, Nip)]
    [InlineData(AuthenticationKsefTokenStatus.Active, "3333333333")]
    public async Task InactiveOrOtherContext_Null_ClosesSession(AuthenticationKsefTokenStatus status, string context)
    {
        Meta(status, context, KsefTokenPermissionType.CredentialsManage);

        Assert.Null(await Gateway().CheckTokenAsync(Nip, Token));
        await _client.Received(1).RevokeCurrentSessionAsync("access", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TokenMetadataRefused_Null_ClosesSession()
    {
        _client.GetKsefTokenAsync(Reference, "access", Arg.Any<CancellationToken>()).ThrowsAsync(new KsefApiException("KSeF HTTP 404", HttpStatusCode.NotFound));

        Assert.Null(await Gateway().CheckTokenAsync(Nip, Token));
        await _client.Received(1).RevokeCurrentSessionAsync("access", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthRejected_Null_NoMetadataRead()
    {
        AuthCode(450);

        Assert.Null(await Gateway().CheckTokenAsync(Nip, Token));
        await _client.DidNotReceiveWithAnyArgs().GetKsefTokenAsync(default!, default!, default);
        await _client.DidNotReceiveWithAnyArgs().RevokeCurrentSessionAsync(default!, default);
    }

    [Fact]
    public async Task MetadataServerError_Throws_ClosesSession()
    {
        _client.GetKsefTokenAsync(Reference, "access", Arg.Any<CancellationToken>()).ThrowsAsync(new KsefApiException("KSeF HTTP 503", HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<KsefApiException>(() => Gateway().CheckTokenAsync(Nip, Token));
        await _client.Received(1).RevokeCurrentSessionAsync("access", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("no-separator")]
    [InlineData("a|b")]
    [InlineData("|nip-1111111111|secret")]
    public async Task NotTheTokenShape_NullWithoutKSeFCall(string token)
    {
        Assert.Null(await Gateway().CheckTokenAsync(Nip, token));
        Assert.Empty(_client.ReceivedCalls());
    }

    private void AuthCode(int code) =>
        _client.GetAuthStatusAsync(Arg.Any<string>(), "operation", Arg.Any<CancellationToken>()).Returns(new AuthStatus { Status = new OperationStatusInfo { Code = code, Description = "status" } });

    private void Meta(AuthenticationKsefTokenStatus status, string contextNip, params KsefTokenPermissionType[] permissions) =>
        _client.GetKsefTokenAsync(Reference, "access", Arg.Any<CancellationToken>()).Returns(new AuthenticationKsefToken
        {
            ReferenceNumber = Reference,
            Status = status,
            ContextIdentifier = new AuthenticationTokenContextIdentifier { Type = AuthenticationTokenContextIdentifierType.Nip, Value = contextNip },
            RequestedPermissions = permissions.ToList(),
        });

    private KSeFGateway Gateway() =>
        new(_client, Substitute.For<IAuthCoordinator>(), _cryptography, Substitute.For<IEncryptionService>(), Substitute.For<IHttpClientFactory>(), new KSeFClientOptions(), NullLogger<KSeFGateway>.Instance);
}
