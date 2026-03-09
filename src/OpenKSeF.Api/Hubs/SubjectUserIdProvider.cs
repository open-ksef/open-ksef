using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace OpenKSeF.Api.Hubs;

/// <summary>
/// Maps SignalR connections to user IDs using the JWT "sub" claim,
/// matching how Keycloak identifies users and how DeviceTokens.UserId is stored.
/// </summary>
public class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? connection.User?.FindFirst("sub")?.Value;
    }
}
