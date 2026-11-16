using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Api.Tests.Services;

public class IssuedInvoiceStuckHealthCheckTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly DateTime _now = DateTime.UtcNow;

    [Fact]
    public async Task NoEscalatedInvoice_IsHealthy()
    {
        AddIssued(escalatedAt: null);

        Assert.Equal(HealthStatus.Healthy, (await CheckAsync()).Status);
    }

    [Fact]
    public async Task EscalatedInvoice_IsDegraded_DescriptionHasNoBuyerOrNumber()
    {
        AddIssued(escalatedAt: _now.AddHours(-1));
        AddIssued(escalatedAt: null);

        var result = await CheckAsync();

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal("1 issued invoice(s) stuck in KSeF, see docs/issuing-stuck-runbook.md", result.Description);
    }

    private Task<HealthCheckResult> CheckAsync() =>
        new IssuedInvoiceStuckHealthCheck(
                new ServiceCollection().AddSingleton(_db).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                NullLogger<IssuedInvoiceStuckHealthCheck>.Instance)
            .CheckHealthAsync(new HealthCheckContext());

    private void AddIssued(DateTime? escalatedAt)
    {
        var issuedAt = _now.AddDays(-2);
        var invoice = IssuedInvoice.Draft(Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(issuedAt), issuedAt);
        invoice.SetBuyer(BuyerKind.Company, "1111111111", "Nabywca sp. z o.o.", "ul. Długa 2", "00-002 Warszawa");
        invoice.AddLine("Usługa", null, 1, 100m, VatRate.Rate23);
        invoice.SetPayment(PaymentMethod.Cash, false);
        var profile = new SellerProfile(invoice.TenantId);
        profile.Update("Sprzedawca sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", null, false, InvoiceNumbering.DefaultPattern);
        invoice.Issue(profile, "1111111111", "FV/1/11/2026", issuedAt);
        if (escalatedAt is { } at)
            invoice.MarkEscalated(at);
        _db.IssuedInvoices.Add(invoice);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();
}
