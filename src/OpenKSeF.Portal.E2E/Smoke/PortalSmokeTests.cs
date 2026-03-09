using OpenKSeF.Portal.E2E.Infrastructure;

namespace OpenKSeF.Portal.E2E.Smoke;

public sealed class PortalSmokeTests : BasePortalTest
{
    [Test]
    [Explicit("Requires running portal and installed Playwright browsers")]
    public async Task PortalHomePage_IsReachable()
    {
        await NavigateToPortalAsync("/");

        var title = await Page.TitleAsync();
        Assert.That(title, Is.Not.Null.And.Not.Empty);
    }
}
