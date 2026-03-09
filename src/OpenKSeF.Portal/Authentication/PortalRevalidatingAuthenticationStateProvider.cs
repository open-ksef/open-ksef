using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace OpenKSeF.Portal.Authentication;

public sealed class PortalRevalidatingAuthenticationStateProvider(ILoggerFactory loggerFactory)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    protected override Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        var identity = authenticationState.User.Identity;
        if (identity is null || !identity.IsAuthenticated)
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(!string.IsNullOrWhiteSpace(identity.Name));
    }
}
