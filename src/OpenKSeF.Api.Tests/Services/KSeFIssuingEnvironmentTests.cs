using KSeF.Client.Core.Interfaces.Services;
using NSubstitute;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Tests.Services;

public class KSeFIssuingEnvironmentTests
{
    private static KSeFIssuingEnvironment Create(string? environment, string? baseUrl)
    {
        var systemConfig = Substitute.For<ISystemConfigService>();
        systemConfig.GetValue(SystemConfigKeys.KSeFEnvironment).Returns(environment);
        systemConfig.GetValue(SystemConfigKeys.KSeFBaseUrl).Returns(baseUrl);
        return new KSeFIssuingEnvironment(systemConfig, Substitute.For<IVerificationLinkService>());
    }

    [Theory]
    [InlineData("production", null, "production")]
    [InlineData("production", "https://ksef-test.mf.gov.pl/api", "production")]
    [InlineData(null, "https://ksef.podatki.gov.pl/api", "production")]
    [InlineData(null, "https://ksef-test.mf.gov.pl/api", "test")]
    [InlineData(null, null, "test")]
    public void Current_IsTheCanonicalKey(string? environment, string? baseUrl, string expected) =>
        Assert.Equal(expected, Create(environment, baseUrl).Current);

    [Theory]
    [InlineData("tset", null)]
    [InlineData(null, "https://ksef.mf.gov.pl/api")]
    public void Current_UnknownValue_Throws(string? environment, string? baseUrl) =>
        Assert.Throws<InvalidOperationException>(() => Create(environment, baseUrl).Current);
}
