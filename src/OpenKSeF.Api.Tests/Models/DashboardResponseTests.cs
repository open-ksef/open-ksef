using OpenKSeF.Api.Models;

namespace OpenKSeF.Api.Tests.Models;

public class DashboardResponseTests
{
    [Fact]
    public void TenantDashboardSummaryResponse_StoresValues()
    {
        var now = DateTime.UtcNow;

        var response = new TenantDashboardSummaryResponse(
            TenantId: Guid.NewGuid(),
            Nip: "1234567890",
            DisplayName: "Main tenant",
            LastSyncedAt: now,
            LastSuccessfulSync: now,
            TotalInvoices: 15,
            InvoicesLast7Days: 4,
            InvoicesLast30Days: 10,
            SyncStatus: "Success");

        Assert.Equal("1234567890", response.Nip);
        Assert.Equal("Main tenant", response.DisplayName);
        Assert.Equal(15, response.TotalInvoices);
        Assert.Equal(4, response.InvoicesLast7Days);
        Assert.Equal(10, response.InvoicesLast30Days);
        Assert.Equal("Success", response.SyncStatus);
    }
}
