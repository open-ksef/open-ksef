namespace OpenKSeF.Portal.Services;

public interface IDeviceTokenOverviewService
{
    Task<IReadOnlyList<DeviceTokenOverviewRow>> ListAsync(Guid? tenantId = null);
}
