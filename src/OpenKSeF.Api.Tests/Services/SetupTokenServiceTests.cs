using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Tests.Services;

public class SetupTokenServiceTests
{
    private readonly ISystemConfigService _systemConfig = Substitute.For<ISystemConfigService>();
    private string? _storedKey;

    public SetupTokenServiceTests()
    {
        _systemConfig.GetValue(SystemConfigKeys.SetupTokenSigningKey).Returns(_ => _storedKey);
        _systemConfig.SetValueAsync(SystemConfigKeys.SetupTokenSigningKey, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => _storedKey = call.ArgAt<string>(1));
    }

    [Fact]
    public async Task ValidateAndConsume_RejectsTokenSignedWithLegacyDevKey()
    {
        var service = new SetupTokenService(_systemConfig, NullLogger<SetupTokenService>.Instance);
        await service.GenerateSetupTokenAsync("user-1", null, null);

        var forged = CreateToken(Encoding.UTF8.GetBytes("dev-setup-token-key-must-be-32b!"), DateTime.UtcNow.AddMinutes(5));

        Assert.Null(await service.ValidateAndConsumeAsync(forged));
    }

    [Fact]
    public async Task ValidateAndConsume_AcceptsOnce_ThenRejectsReplay()
    {
        var service = new SetupTokenService(_systemConfig, NullLogger<SetupTokenService>.Instance);
        var token = await service.GenerateSetupTokenAsync("user-1", "a@b.pl", "Jan");

        var principal = await service.ValidateAndConsumeAsync(token);

        Assert.NotNull(principal);
        Assert.Equal("user-1", principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Null(await service.ValidateAndConsumeAsync(token));
    }

    [Fact]
    public async Task ValidateAndConsume_RejectsWrongPurpose()
    {
        var service = new SetupTokenService(_systemConfig, NullLogger<SetupTokenService>.Instance);
        await service.GenerateSetupTokenAsync("user-1", null, null);

        var token = CreateToken(Convert.FromBase64String(_storedKey!), DateTime.UtcNow.AddMinutes(5), purpose: "other");

        Assert.Null(await service.ValidateAndConsumeAsync(token));
    }

    [Fact]
    public async Task ValidateAndConsume_RejectsExpiredToken()
    {
        var service = new SetupTokenService(_systemConfig, NullLogger<SetupTokenService>.Instance);
        await service.GenerateSetupTokenAsync("user-1", null, null);

        var token = CreateToken(Convert.FromBase64String(_storedKey!), DateTime.UtcNow.AddMinutes(-2));

        Assert.Null(await service.ValidateAndConsumeAsync(token));
    }

    [Fact]
    public async Task SigningKey_CreatedOnceAndReused()
    {
        var service = new SetupTokenService(_systemConfig, NullLogger<SetupTokenService>.Instance);

        var first = await service.GenerateSetupTokenAsync("user-1", null, null);
        var second = await service.GenerateSetupTokenAsync("user-2", null, null);

        await _systemConfig.Received(1).SetValueAsync(SystemConfigKeys.SetupTokenSigningKey, Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(32, Convert.FromBase64String(_storedKey!).Length);
        Assert.NotNull(await service.ValidateAndConsumeAsync(first));
        Assert.NotNull(await service.ValidateAndConsumeAsync(second));
    }

    [Fact]
    public async Task SigningKey_ExistingRow_IsUsedWithoutWrite()
    {
        _storedKey = Convert.ToBase64String(new byte[32]);
        var service = new SetupTokenService(_systemConfig, NullLogger<SetupTokenService>.Instance);

        var token = await service.GenerateSetupTokenAsync("user-1", null, null);

        await _systemConfig.DidNotReceive().SetValueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.NotNull(await new SetupTokenService(_systemConfig, NullLogger<SetupTokenService>.Instance).ValidateAndConsumeAsync(token));
    }

    [Fact]
    public async Task KeyStoreFailure_GenerateThrows_ValidateReturnsNull()
    {
        _systemConfig.SetValueAsync(SystemConfigKeys.SetupTokenSigningKey, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db down"));
        var service = new SetupTokenService(_systemConfig, NullLogger<SetupTokenService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateSetupTokenAsync("user-1", null, null));
        var forged = CreateToken(Encoding.UTF8.GetBytes("dev-setup-token-key-must-be-32b!"), DateTime.UtcNow.AddMinutes(5));
        Assert.Null(await service.ValidateAndConsumeAsync(forged));
    }

    private static string CreateToken(byte[] key, DateTime expires, string purpose = "mobile-setup")
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "openksef-setup",
            audience: "openksef-mobile",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("purpose", purpose),
            ],
            notBefore: expires.AddMinutes(-10),
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
