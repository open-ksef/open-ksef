using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Push;
using OpenKSeF.Domain.Services;
using OpenKSeF.Worker.Extensions;

namespace OpenKSeF.Domain.Tests;

public class WorkerDomainServiceRegistrationTests
{
    private static IConfiguration EmptyConfiguration =>
        new ConfigurationBuilder().Build();

    [Fact]
    public void AddWorkerDomainServices_RegistersEmailServiceWithEmailOptions()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Enabled"] = "true",
                ["Email:SmtpHost"] = "smtp.example.invalid",
                ["Email:SmtpPort"] = "465"
            })
            .Build();

        services.AddWorkerDomainServices(configuration);

        var descriptor = Assert.Single(services.Where(d => d.ServiceType == typeof(IEmailService)));
        Assert.Equal(typeof(EmailService), descriptor.ImplementationType);
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<EmailOptions>>().Value;
        Assert.True(options.Enabled);
        Assert.Equal("smtp.example.invalid", options.SmtpHost);
        Assert.Equal(465, options.SmtpPort);
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
}
