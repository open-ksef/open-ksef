using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Tests.Controllers;

public class SystemSetupControllerTests
{
    private readonly ISystemConfigService _systemConfig;
    private readonly ISystemSetupService _setupService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SystemSetupController> _logger;

    public SystemSetupControllerTests()
    {
        _systemConfig = Substitute.For<ISystemConfigService>();
        _setupService = Substitute.For<ISystemSetupService>();
        _configuration = new ConfigurationBuilder().Build();
        _logger = Substitute.For<ILogger<SystemSetupController>>();
    }

    private SystemSetupController CreateController() =>
        new(_systemConfig, _setupService, _configuration, _logger);

    [Fact]
    public void GetSetupStatus_NotInitialized_ReturnsFalse()
    {
        _systemConfig.IsInitialized.Returns(false);
        var controller = CreateController();

        var result = controller.GetSetupStatus() as OkObjectResult;

        var response = Assert.IsType<SetupStatusResponse>(result!.Value);
        Assert.False(response.IsInitialized);
    }

    [Fact]
    public void GetSetupStatus_Initialized_ReturnsTrue()
    {
        _systemConfig.IsInitialized.Returns(true);
        var controller = CreateController();

        var result = controller.GetSetupStatus() as OkObjectResult;

        var response = Assert.IsType<SetupStatusResponse>(result!.Value);
        Assert.True(response.IsInitialized);
    }

    [Fact]
    public async Task Authenticate_AlreadyInitialized_Returns403()
    {
        _systemConfig.IsInitialized.Returns(true);
        var controller = CreateController();
        var request = new SetupAuthenticateRequest("admin", "admin");

        var result = await controller.Authenticate(request) as ObjectResult;

        Assert.Equal(StatusCodes.Status403Forbidden, result!.StatusCode);
    }

    [Fact]
    public async Task Authenticate_InvalidCredentials_ReturnsBadRequest()
    {
        _systemConfig.IsInitialized.Returns(false);
        _setupService.AuthenticateAdminAsync("admin", "wrong").Returns((string?)null);
        var controller = CreateController();
        var request = new SetupAuthenticateRequest("admin", "wrong");

        var result = await controller.Authenticate(request) as BadRequestObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Authenticate_ValidCredentials_ReturnsSetupToken()
    {
        _systemConfig.IsInitialized.Returns(false);
        _setupService.AuthenticateAdminAsync("admin", "admin").Returns("kc-token-123");
        var controller = CreateController();
        var request = new SetupAuthenticateRequest("admin", "admin");

        var result = await controller.Authenticate(request) as OkObjectResult;

        var response = Assert.IsType<SetupAuthenticateResponse>(result!.Value);
        Assert.False(string.IsNullOrEmpty(response.SetupToken));
        Assert.Equal(600, response.ExpiresInSeconds);
    }

    [Fact]
    public async Task Apply_AlreadyInitialized_Returns403()
    {
        _systemConfig.IsInitialized.Returns(true);
        var controller = CreateController();
        var request = new SetupApplyRequest
        {
            ExternalBaseUrl = "http://localhost:8080",
            KSeFBaseUrl = "https://ksef-test.mf.gov.pl/api",
            AdminEmail = "admin@example.com",
            AdminPassword = "Test1234!",
        };

        var result = await controller.Apply("some-token", request, CancellationToken.None) as ObjectResult;

        Assert.Equal(StatusCodes.Status403Forbidden, result!.StatusCode);
    }

    [Fact]
    public async Task Apply_MissingToken_ReturnsBadRequest()
    {
        _systemConfig.IsInitialized.Returns(false);
        var controller = CreateController();
        var request = new SetupApplyRequest
        {
            ExternalBaseUrl = "http://localhost:8080",
            KSeFBaseUrl = "https://ksef-test.mf.gov.pl/api",
            AdminEmail = "admin@example.com",
            AdminPassword = "Test1234!",
        };

        var result = await controller.Apply(null, request, CancellationToken.None) as BadRequestObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Apply_InvalidToken_ReturnsBadRequest()
    {
        _systemConfig.IsInitialized.Returns(false);
        var controller = CreateController();
        var request = new SetupApplyRequest
        {
            ExternalBaseUrl = "http://localhost:8080",
            KSeFBaseUrl = "https://ksef-test.mf.gov.pl/api",
            AdminEmail = "admin@example.com",
            AdminPassword = "Test1234!",
        };

        var result = await controller.Apply("not-a-valid-jwt", request, CancellationToken.None) as BadRequestObjectResult;

        Assert.NotNull(result);
    }
}
