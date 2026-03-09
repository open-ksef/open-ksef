using System.Text.Json.Serialization;

namespace OpenKSeF.Api.Models;

public record SetupTokenResponse(
    [property: JsonPropertyName("setupToken")] string SetupToken,
    [property: JsonPropertyName("expiresInSeconds")] int ExpiresInSeconds);

public record RedeemSetupTokenRequest(
    [property: JsonPropertyName("setupToken")] string SetupToken);

public record RedeemTokenResponse(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("refreshToken")] string RefreshToken,
    [property: JsonPropertyName("expiresIn")] int ExpiresIn);
