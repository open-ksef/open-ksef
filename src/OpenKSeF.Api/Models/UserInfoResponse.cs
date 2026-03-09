namespace OpenKSeF.Api.Models;

public record UserInfoResponse(
    string UserId,
    string? Email,
    string? Name);
