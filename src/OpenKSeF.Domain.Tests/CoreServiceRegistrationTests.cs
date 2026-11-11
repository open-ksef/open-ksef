using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Push;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests;

public class CoreServiceRegistrationTests
{
    private static IConfiguration Configuration(Dictionary<string, string?>? settings = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Db"] = "Host=localhost;Database=core_registration;Username=u;Password=p"
            })
            .AddInMemoryCollection(settings ?? [])
            .Build();

    [Fact]
    public void AddOpenKSeFCore_RegistersEmailServiceWithEmailOptions()
    {
        var services = new ServiceCollection();

        services.AddOpenKSeFCore(Configuration(new()
        {
            ["Email:Enabled"] = "true",
            ["Email:SmtpHost"] = "smtp.example.invalid",
            ["Email:SmtpPort"] = "465"
        }));

        var descriptor = Assert.Single(services.Where(d => d.ServiceType == typeof(IEmailService)));
        Assert.Equal(typeof(EmailService), descriptor.ImplementationType);
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<EmailOptions>>().Value;
        Assert.True(options.Enabled);
        Assert.Equal("smtp.example.invalid", options.SmtpHost);
        Assert.Equal(465, options.SmtpPort);
    }

    [Fact]
    public void AddOpenKSeFCore_RegistersRelayFcmApnsInOrder_AndNoUserPushProvider()
    {
        var services = new ServiceCollection();

        services.AddOpenKSeFCore(Configuration());

        var pushProviders = services.Where(d => d.ServiceType == typeof(IPushProvider)).ToList();
        Assert.Equal(3, pushProviders.Count);
        Assert.Equal(typeof(RelayPushProvider), pushProviders[0].ImplementationType);
        Assert.Equal(typeof(FcmPushProvider), pushProviders[1].ImplementationType);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IUserPushProvider));

        using var provider = services.AddLogging().BuildServiceProvider();
        Assert.IsType<ApnsPushProvider>(pushProviders[2].ImplementationFactory!(provider));
    }

    [Fact]
    public void AddOpenKSeFCore_UsesNpgsql_AndResolvesEscalationService()
    {
        var configuration = Configuration();
        using var provider = new ServiceCollection().AddLogging().AddSingleton(configuration).AddOpenKSeFCore(configuration).BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.True(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.IsNpgsql());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IssuedInvoiceEscalationService>());
    }

    [Fact]
    public void AddOpenKSeFCore_WithoutConnectionString_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new ServiceCollection().AddOpenKSeFCore(new ConfigurationBuilder().Build()));
    }
}
