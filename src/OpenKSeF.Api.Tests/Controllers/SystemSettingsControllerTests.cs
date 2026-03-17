using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Tests.Controllers;

public class SystemSettingsControllerTests
{
    private readonly ISystemConfigService _systemConfig;
    private readonly ISystemSetupService _setupService;
    private readonly ISystemSettingsService _settingsService;
    private readonly ILogger<SystemSettingsController> _logger;

    public SystemSettingsControllerTests()
    {
        _systemConfig = Substitute.For<ISystemConfigService>();
        _setupService = Substitute.For<ISystemSetupService>();
        _settingsService = Substitute.For<ISystemSettingsService>();
        _logger = Substitute.For<ILogger<SystemSettingsController>>();
    }

    private SystemSettingsController CreateController() =>
        new(_systemConfig, _setupService, _settingsService, _logger);

    [Fact]
    public async Task GetSettings_NotInitialized_Returns403()
    {
        _systemConfig.IsInitialized.Returns(false);
        var controller = CreateController();
        var request = new SettingsAuthRequest { KcAdminUsername = "admin", KcAdminPassword = "admin" };

        var result = await controller.GetSettings(request, CancellationToken.None) as ObjectResult;

        Assert.Equal(StatusCodes.Status403Forbidden, result!.StatusCode);
    }

    [Fact]
    public async Task GetSettings_InvalidKcCredentials_Returns401()
    {
        _systemConfig.IsInitialized.Returns(true);
        _setupService.AuthenticateAdminAsync("admin", "wrong").Returns((string?)null);
        var controller = CreateController();
        var request = new SettingsAuthRequest { KcAdminUsername = "admin", KcAdminPassword = "wrong" };

        var result = await controller.GetSettings(request, CancellationToken.None) as UnauthorizedObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetSettings_ValidCredentials_ReturnsSettings()
    {
        _systemConfig.IsInitialized.Returns(true);
        _setupService.AuthenticateAdminAsync("admin", "admin").Returns("kc-token");

        var expectedSettings = new SettingsResponse
        {
            ExternalBaseUrl = "http://localhost:8080",
            KSeFEnvironment = "test",
            RegistrationAllowed = true,
        };
        _settingsService.GetSettingsAsync("kc-token", Arg.Any<CancellationToken>()).Returns(expectedSettings);

        var controller = CreateController();
        var request = new SettingsAuthRequest { KcAdminUsername = "admin", KcAdminPassword = "admin" };

        var result = await controller.GetSettings(request, CancellationToken.None) as OkObjectResult;

        var response = Assert.IsType<SettingsResponse>(result!.Value);
        Assert.Equal("http://localhost:8080", response.ExternalBaseUrl);
        Assert.Equal("test", response.KSeFEnvironment);
    }

    [Fact]
    public async Task UpdateSettings_NotInitialized_Returns403()
    {
        _systemConfig.IsInitialized.Returns(false);
        var controller = CreateController();
        var request = new SettingsUpdateRequest
        {
            KcAdminUsername = "admin",
            KcAdminPassword = "admin",
            ExternalBaseUrl = "http://new-url.com",
        };

        var result = await controller.UpdateSettings(request, CancellationToken.None) as ObjectResult;

        Assert.Equal(StatusCodes.Status403Forbidden, result!.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_InvalidKcCredentials_Returns401()
    {
        _systemConfig.IsInitialized.Returns(true);
        _setupService.AuthenticateAdminAsync("admin", "wrong").Returns((string?)null);
        var controller = CreateController();
        var request = new SettingsUpdateRequest
        {
            KcAdminUsername = "admin",
            KcAdminPassword = "wrong",
        };

        var result = await controller.UpdateSettings(request, CancellationToken.None) as UnauthorizedObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task UpdateSettings_Success_ReturnsOk()
    {
        _systemConfig.IsInitialized.Returns(true);
        _setupService.AuthenticateAdminAsync("admin", "admin").Returns("kc-token");
        _settingsService.UpdateSettingsAsync(Arg.Any<SettingsUpdateRequest>(), "kc-token", Arg.Any<CancellationToken>())
            .Returns(new SettingsUpdateResponse(true, null));

        var controller = CreateController();
        var request = new SettingsUpdateRequest
        {
            KcAdminUsername = "admin",
            KcAdminPassword = "admin",
            ExternalBaseUrl = "http://new-url.com",
        };

        var result = await controller.UpdateSettings(request, CancellationToken.None) as OkObjectResult;

        var response = Assert.IsType<SettingsUpdateResponse>(result!.Value);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task UpdateSettings_ServiceError_ReturnsBadRequest()
    {
        _systemConfig.IsInitialized.Returns(true);
        _setupService.AuthenticateAdminAsync("admin", "admin").Returns("kc-token");
        _settingsService.UpdateSettingsAsync(Arg.Any<SettingsUpdateRequest>(), "kc-token", Arg.Any<CancellationToken>())
            .Returns(new SettingsUpdateResponse(false, "KSeF env change blocked"));

        var controller = CreateController();
        var request = new SettingsUpdateRequest
        {
            KcAdminUsername = "admin",
            KcAdminPassword = "admin",
            KSeFEnvironment = "production",
        };

        var result = await controller.UpdateSettings(request, CancellationToken.None) as BadRequestObjectResult;

        Assert.NotNull(result);
        var response = Assert.IsType<SettingsUpdateResponse>(result!.Value);
        Assert.False(response.Success);
    }
}
