using System.Security.Claims;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _httpContextAccessor = httpContextAccessor;
    }

    public string UserId =>
        _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? _httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value
        ?? throw new InvalidOperationException("User is not authenticated.");
}
