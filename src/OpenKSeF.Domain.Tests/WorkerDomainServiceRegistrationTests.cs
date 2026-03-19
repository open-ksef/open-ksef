using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Push;
using OpenKSeF.Domain.Services;
using OpenKSeF.Worker.Extensions;

namespace OpenKSeF.Domain.Tests;

public class WorkerDomainServiceRegistrationTests
{
    private static IConfiguration EmptyConfiguration =>
        new ConfigurationBuilder().Build();

    [Fact]
    public void AddWorkerDomainServices_RegistersNoOpEmailService()
    {
        var services = new ServiceCollection();

        services.AddWorkerDomainServices(EmptyConfiguration);

        var descriptor = Assert.Single(services.Where(d => d.ServiceType == typeof(IEmailService)));
        Assert.Equal(typeof(NoOpEmailService), descriptor.ImplementationType);
    }

    [Fact]
    public void AddWorkerDomainServices_RegistersPushProviders()
    {
        var services = new ServiceCollection();

        services.AddWorkerDomainServices(EmptyConfiguration);

        var pushProviders = services.Where(d => d.ServiceType == typeof(IPushProvider)).ToList();
        Assert.Equal(3, pushProviders.Count);
        Assert.Contains(pushProviders, d => d.ImplementationType == typeof(RelayPushProvider));
        Assert.Contains(pushProviders, d => d.ImplementationType == typeof(FcmPushProvider));
    }

    [Fact]
    public async Task NoOpEmailService_SendNewInvoiceEmailAsync_CompletesSuccessfully()
    {
        var sut = new NoOpEmailService(NullLogger<NoOpEmailService>.Instance);
        var evt = new NewInvoiceDetectedEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Vendor",
            "FV/2026/001",
            123.45m,
            "PLN");

        var exception = await Record.ExceptionAsync(() => sut.SendNewInvoiceEmailAsync("tenant@example.com", evt));

        Assert.Null(exception);
    }
}
