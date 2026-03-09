using Microsoft.AspNetCore.Components;
using OpenKSeF.Portal.Services;

namespace OpenKSeF.Portal.Components;

public abstract class TenantScopedComponentBase : ComponentBase
{
    [Inject] protected ITenantResolver TenantResolver { get; set; } = default!;

    protected Guid? CurrentTenantId { get; private set; }
    protected bool IsLoading { get; private set; } = true;
    protected string? ErrorMessage { get; set; }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            CurrentTenantId = await TenantResolver.GetCurrentTenantIdAsync();
        }
        catch
        {
            ErrorMessage = "Unable to resolve tenant context.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    protected Guid EnsureTenantAccess()
    {
        if (CurrentTenantId is null)
        {
            throw new UnauthorizedAccessException("No tenant access available for current user.");
        }

        return CurrentTenantId.Value;
    }
}
