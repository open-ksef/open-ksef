namespace OpenKSeF.Portal.E2E;

public class TenantSelectionMarkupTests
{
    private static string LoadInvoiceListSource()
    {
        var filePath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../OpenKSeF.Portal.Web/src/pages/InvoiceList.tsx"));

        return File.ReadAllText(filePath);
    }

    private static string LoadInvoiceDetailsSource()
    {
        var filePath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../OpenKSeF.Portal.Web/src/pages/InvoiceDetails.tsx"));

        return File.ReadAllText(filePath);
    }

    [Test]
    public void ReactInvoiceList_DefinesStableTenantSelectionSelectors()
    {
        var content = LoadInvoiceListSource();

        Assert.That(content, Does.Contain("data-testid=\"invoice-tenant-filter\""));
        Assert.That(content, Does.Contain("applyButtonTestId=\"invoice-apply-filters\""));
        Assert.That(content, Does.Contain("testId=\"invoice-table\""));
        Assert.That(content, Does.Contain("data-testid': 'invoice-row'"));
    }

    [Test]
    public void ReactInvoiceList_DefinesStablePaginationAndFilterSelectors()
    {
        var content = LoadInvoiceListSource();

        Assert.That(content, Does.Contain("data-testid=\"invoice-date-from\""));
        Assert.That(content, Does.Contain("data-testid=\"invoice-date-to\""));
        Assert.That(content, Does.Contain("data-testid=\"invoice-page-size\""));
        Assert.That(content, Does.Contain("data-testid=\"invoice-next-page\""));
        Assert.That(content, Does.Contain("data-testid=\"invoice-prev-page\""));
    }

    [Test]
    public void ReactInvoiceList_DefinesStableInvoiceDetailsNavigationSelectors()
    {
        var content = LoadInvoiceListSource();

        Assert.That(content, Does.Contain("data-testid=\"invoice-view-details\""));
    }

    [Test]
    public void ReactInvoiceDetails_DefinesStableMetadataSelectors()
    {
        var content = LoadInvoiceDetailsSource();

        Assert.That(content, Does.Contain("data-testid=\"invoice-details-card\""));
        Assert.That(content, Does.Contain("data-testid=\"invoice-detail-ksef-number\""));
        Assert.That(content, Does.Contain("data-testid=\"invoice-detail-vendor-name\""));
        Assert.That(content, Does.Contain("data-testid=\"invoice-detail-vendor-nip\""));
        Assert.That(content, Does.Contain("data-testid=\"invoice-detail-issue-date\""));
        Assert.That(content, Does.Contain("data-testid=\"invoice-detail-amount\""));
        Assert.That(content, Does.Contain("data-testid=\"invoice-detail-currency\""));
        Assert.That(content, Does.Contain("data-testid=\"invoice-details-back-link\""));
        Assert.That(content, Does.Contain("emptyTitle=\"Invoice not found\""));
    }
}
