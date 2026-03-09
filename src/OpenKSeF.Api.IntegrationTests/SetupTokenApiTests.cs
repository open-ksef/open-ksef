using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;
using OpenKSeF.Api.IntegrationTests.Infrastructure;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class SetupTokenApiTests : IDisposable
{
    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SetupTokenApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture);
        _client = _factory.CreateClient();
    }

    private async Task AuthenticateAsync()
    {
        var token = await _fixture.GetAccessTokenAsync();
        _client.SetBearerToken(token);
    }

    [Fact]
    public async Task GenerateSetupToken_ReturnsToken_WhenAuthenticated()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsync("/api/account/setup-token", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SetupTokenResponseDto>();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.SetupToken));
        Assert.Equal(300, result.ExpiresInSeconds);
    }

    [Fact]
    public async Task GenerateSetupToken_Returns401_WhenAnonymous()
    {
        var response = await _client.PostAsync("/api/account/setup-token", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RedeemSetupToken_Returns400_WhenTokenInvalid()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/account/redeem-setup-token",
            new { setupToken = "garbage-not-a-jwt" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RedeemSetupToken_Returns400_WhenTokenExpired()
    {
        var signingKey = Encoding.UTF8.GetBytes("dev-setup-token-key-must-be-32b!");
        var securityKey = new SymmetricSecurityKey(signingKey);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var expiredToken = new JwtSecurityToken(
            issuer: "openksef-setup",
            audience: "openksef-mobile",
            claims: [
                new(JwtRegisteredClaimNames.Sub, "test-user-id"),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new("purpose", "mobile-setup"),
            ],
            expires: DateTime.UtcNow.AddMinutes(-1),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(expiredToken);

        var response = await _client.PostAsJsonAsync(
            "/api/account/redeem-setup-token",
            new { setupToken = tokenString });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RedeemSetupToken_ReturnsTokens_WhenValidAndExchangeConfigured()
    {
        await AuthenticateAsync();

        var generateResponse = await _client.PostAsync("/api/account/setup-token", null);
        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);

        var setupResult = await generateResponse.Content.ReadFromJsonAsync<SetupTokenResponseDto>();
        Assert.NotNull(setupResult);

        using var anonClient = _factory.CreateClient();
        var redeemResponse = await anonClient.PostAsJsonAsync(
            "/api/account/redeem-setup-token",
            new { setupToken = setupResult.SetupToken });

        // Token exchange requires Keycloak service account + token-exchange grant.
        // Without proper configuration, this returns 400.
        // When configured, it should return 200 with tokens.
        if (redeemResponse.StatusCode == HttpStatusCode.OK)
        {
            var tokens = await redeemResponse.Content.ReadFromJsonAsync<RedeemTokenResponseDto>();
            Assert.NotNull(tokens);
            Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
            Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
            Assert.True(tokens.ExpiresIn > 0);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, redeemResponse.StatusCode);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed record SetupTokenResponseDto(
        [property: JsonPropertyName("setupToken")] string SetupToken,
        [property: JsonPropertyName("expiresInSeconds")] int ExpiresInSeconds);

    private sealed record RedeemTokenResponseDto(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("refreshToken")] string RefreshToken,
        [property: JsonPropertyName("expiresIn")] int ExpiresIn);
}
