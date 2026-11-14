using System.Text.Json;
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
    private readonly ISetupSessionService _sessionService;
    private readonly ILogger<SystemSetupController> _logger;
    private readonly SetupAuthAttemptLimiter _limiter = new(TimeProvider.System);
    private IConfiguration _configuration = Config("MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");

    private static IConfiguration Config(string? key) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ENCRYPTION_KEY"] = key })
        .Build();

    public SystemSetupControllerTests()
    {
        _systemConfig = Substitute.For<ISystemConfigService>();
        _setupService = Substitute.For<ISystemSetupService>();
        _sessionService = Substitute.For<ISetupSessionService>();
        _logger = Substitute.For<ILogger<SystemSetupController>>();
    }

    private SystemSetupController CreateController() =>
        new(_systemConfig, _setupService, _sessionService, _limiter, _configuration, _logger)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

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

    [Theory]
    [InlineData(null, false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void GetSetupStatus_ReturnsStoredRegistrationFlag(string? stored, bool expected)
    {
        _systemConfig.GetValue(SystemConfigKeys.RegistrationAllowed).Returns(stored);
        var controller = CreateController();

        var result = controller.GetSetupStatus() as OkObjectResult;

        var response = Assert.IsType<SetupStatusResponse>(result!.Value);
        Assert.Equal(expected, response.RegistrationAllowed);
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
        _setupService.AuthenticateAdminAsync("admin", "wrong").Returns(((string?)null, false));
        var controller = CreateController();
        var request = new SetupAuthenticateRequest("admin", "wrong");

        var result = Assert.IsType<BadRequestObjectResult>(await controller.Authenticate(request));

        Assert.Equal(SystemSetupService.KeycloakInvalidCredentialsMessage, JsonSerializer.SerializeToElement(result.Value).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Authenticate_KeycloakUnreachable_SaysUnreachableInPolish()
    {
        _systemConfig.IsInitialized.Returns(false);
        _setupService.AuthenticateAdminAsync("admin", "admin").Returns(((string?)null, true));

        var result = Assert.IsType<BadRequestObjectResult>(await CreateController().Authenticate(new SetupAuthenticateRequest("admin", "admin")));

        Assert.Equal(SystemSetupService.KeycloakUnreachableMessage, JsonSerializer.SerializeToElement(result.Value).GetProperty("error").GetString());
        _sessionService.DidNotReceiveWithAnyArgs().CreateSession(default!, default!);
    }

    [Fact]
    public async Task Authenticate_KeycloakUnreachable_DoesNotConsumeRateLimit()
    {
        _systemConfig.IsInitialized.Returns(false);
        _setupService.AuthenticateAdminAsync("admin", "admin").Returns(((string?)null, true), ((string?)null, true), ((string?)null, true),
            ((string?)null, true), ((string?)null, true), ((string?)null, true), ((string?)null, false));

        for (var i = 0; i < SetupAuthAttemptLimiter.PermitLimit + 1; i++)
            Assert.IsType<BadRequestObjectResult>(await CreateController().Authenticate(new SetupAuthenticateRequest("admin", "admin")));

        var result = Assert.IsType<BadRequestObjectResult>(await CreateController().Authenticate(new SetupAuthenticateRequest("admin", "admin")));
        Assert.Equal(SystemSetupService.KeycloakInvalidCredentialsMessage, JsonSerializer.SerializeToElement(result.Value).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Authenticate_RejectedCredentials_AreRateLimited()
    {
        _systemConfig.IsInitialized.Returns(false);
        _setupService.AuthenticateAdminAsync("admin", "wrong").Returns(((string?)null, false));

        for (var i = 0; i < SetupAuthAttemptLimiter.PermitLimit; i++)
            Assert.IsType<BadRequestObjectResult>(await CreateController().Authenticate(new SetupAuthenticateRequest("admin", "wrong")));

        var controller = CreateController();
        var result = Assert.IsType<ObjectResult>(await controller.Authenticate(new SetupAuthenticateRequest("admin", "wrong")));
        Assert.Equal(StatusCodes.Status429TooManyRequests, result.StatusCode);
        Assert.False(string.IsNullOrEmpty(controller.Response.Headers.RetryAfter));
        await _setupService.Received(SetupAuthAttemptLimiter.PermitLimit).AuthenticateAdminAsync("admin", "wrong");
    }

    [Fact]
    public async Task Authenticate_ValidCredentials_ReturnsSetupToken()
    {
        _systemConfig.IsInitialized.Returns(false);
        _setupService.AuthenticateAdminAsync("admin", "admin").Returns(((string?)"kc-token-123", false));
        _sessionService.CreateSession("admin", "admin").Returns("opaque-session-token");
        var controller = CreateController();
        var request = new SetupAuthenticateRequest("admin", "admin");

        var result = await controller.Authenticate(request) as OkObjectResult;

        var response = Assert.IsType<SetupAuthenticateResponse>(result!.Value);
        Assert.Equal("opaque-session-token", response.SetupToken);
        Assert.Equal(600, response.ExpiresInSeconds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("c2hvcnQ=")]
    public async Task Authenticate_NoUsableKey_Returns400WithCode_CreatesNoSession(string? key)
    {
        _configuration = Config(key);
        _systemConfig.IsInitialized.Returns(false);
        _setupService.AuthenticateAdminAsync("admin", "admin").Returns(((string?)"kc-token-123", false));

        var result = Assert.IsType<BadRequestObjectResult>(await CreateController().Authenticate(new SetupAuthenticateRequest("admin", "admin")));

        var body = JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal(SystemSetupService.EncryptionKeyMissingCode, body.GetProperty("code").GetString());
        Assert.Equal(SystemSetupService.EncryptionKeyMissingMessage, body.GetProperty("error").GetString());
        _sessionService.DidNotReceiveWithAnyArgs().CreateSession(default!, default!);
    }

    [Fact]
    public async Task Authenticate_NoUsableKey_WrongPassword_SaysNothingAboutTheKey()
    {
        _configuration = Config(null);
        _systemConfig.IsInitialized.Returns(false);
        _setupService.AuthenticateAdminAsync("admin", "wrong").Returns(((string?)null, false));

        var result = Assert.IsType<BadRequestObjectResult>(await CreateController().Authenticate(new SetupAuthenticateRequest("admin", "wrong")));

        Assert.False(JsonSerializer.SerializeToElement(result.Value).TryGetProperty("code", out _));
    }

    [Fact]
    public async Task Apply_AlreadyInitialized_Returns403()
    {
        _systemConfig.IsInitialized.Returns(true);
        var controller = CreateController();
        var request = new SetupApplyRequest
        {
            ExternalBaseUrl = "http://localhost:8080",
            KSeFBaseUrl = "test",
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
            KSeFBaseUrl = "test",
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
        _sessionService.RedeemSession("not-a-valid-session").Returns((ValueTuple<string, string, DateTimeOffset>?)null);
        var controller = CreateController();
        var request = new SetupApplyRequest
        {
            ExternalBaseUrl = "http://localhost:8080",
            KSeFBaseUrl = "test",
            AdminEmail = "admin@example.com",
            AdminPassword = "Test1234!",
        };

        var result = await controller.Apply("not-a-valid-session", request, CancellationToken.None) as BadRequestObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Apply_ValidToken_IsConsumedOnce()
    {
        _systemConfig.IsInitialized.Returns(false);
        _sessionService.RedeemSession("valid-session")
            .Returns(("admin", "secret", DateTimeOffset.UtcNow.AddMinutes(10)), (ValueTuple<string, string, DateTimeOffset>?)null);
        _setupService.AuthenticateAdminAsync("admin", "secret").Returns(((string?)"kc-token", false));
        _setupService.ApplySetupAsync(Arg.Any<SetupApplyRequest>(), "kc-token", "admin", Arg.Any<CancellationToken>())
            .Returns(new SetupApplyResponse(true, null));
        var controller = CreateController();
        var request = new SetupApplyRequest
        {
            ExternalBaseUrl = "http://localhost:8080",
            KSeFBaseUrl = "test",
            AdminEmail = "admin@example.com",
            AdminPassword = "Test1234!",
        };

        var firstResult = await controller.Apply("valid-session", request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(firstResult);

        // Session has been redeemed — second call returns null
        var secondAttemptCredentials = _sessionService.RedeemSession("valid-session");
        Assert.Null(secondAttemptCredentials);
    }

    [Fact]
    public async Task Apply_Fails_SameTokenWorksForRetry()
    {
        var sessions = new SetupSessionService();
        var token = sessions.CreateSession("admin", "secret");
        _systemConfig.IsInitialized.Returns(false);
        _setupService.AuthenticateAdminAsync("admin", "secret").Returns(((string?)"kc-token", false));
        _setupService.ApplySetupAsync(Arg.Any<SetupApplyRequest>(), "kc-token", "admin", Arg.Any<CancellationToken>())
            .Returns(new SetupApplyResponse(false, "Niepoprawny NIP. Wpisz 10 cyfr z poprawną sumą kontrolną."), new SetupApplyResponse(true, null));
        var controller = new SystemSetupController(_systemConfig, _setupService, sessions, _limiter, _configuration, _logger);
        var request = new SetupApplyRequest
        {
            ExternalBaseUrl = "http://localhost:8080",
            KSeFBaseUrl = "test",
            AdminEmail = "admin@example.com",
            AdminPassword = "Test1234!",
            FirstTenantNip = "1111111112",
        };

        Assert.IsType<BadRequestObjectResult>(await controller.Apply(token, request, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await controller.Apply(token, request, CancellationToken.None));
        Assert.Null(sessions.RedeemSession(token));
    }

    [Fact]
    public async Task Apply_KeycloakDown_SaysUnreachableInPolish_AndKeepsTheToken()
    {
        var sessions = new SetupSessionService();
        var token = sessions.CreateSession("admin", "secret");
        _systemConfig.IsInitialized.Returns(false);
        _setupService.AuthenticateAdminAsync("admin", "secret").Returns(((string?)null, true));
        var controller = new SystemSetupController(_systemConfig, _setupService, sessions, _limiter, _configuration, _logger);
        var request = new SetupApplyRequest
        {
            ExternalBaseUrl = "http://localhost:8080",
            KSeFBaseUrl = "test",
            AdminEmail = "admin@example.com",
            AdminPassword = "Test1234!",
        };

        var result = Assert.IsType<BadRequestObjectResult>(await controller.Apply(token, request, CancellationToken.None));

        Assert.Equal(SystemSetupService.KeycloakUnreachableMessage, Assert.IsType<SetupApplyResponse>(result.Value).Error);
        Assert.NotNull(sessions.RedeemSession(token));
    }

    [Fact]
    public async Task Apply_KeycloakRejectsCredentials_SaysSoInPolish_AndDropsTheToken()
    {
        var sessions = new SetupSessionService();
        var token = sessions.CreateSession("admin", "changed");
        _systemConfig.IsInitialized.Returns(false);
        _setupService.AuthenticateAdminAsync("admin", "changed").Returns(((string?)null, false));
        var controller = new SystemSetupController(_systemConfig, _setupService, sessions, _limiter, _configuration, _logger);
        var request = new SetupApplyRequest
        {
            ExternalBaseUrl = "http://localhost:8080",
            KSeFBaseUrl = "test",
            AdminEmail = "admin@example.com",
            AdminPassword = "Test1234!",
        };

        var result = Assert.IsType<BadRequestObjectResult>(await controller.Apply(token, request, CancellationToken.None));

        Assert.Equal(SystemSetupService.KeycloakInvalidCredentialsMessage, Assert.IsType<SetupApplyResponse>(result.Value).Error);
        Assert.Null(sessions.RedeemSession(token));
    }

    [Fact]
    public void RestoreSession_KeepsTheOriginalExpiry()
    {
        var sessions = new SetupSessionService();
        var token = sessions.CreateSession("admin", "secret");
        var redeemed = sessions.RedeemSession(token)!.Value;

        sessions.RestoreSession(token, redeemed with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) });

        Assert.Null(sessions.RedeemSession(token));
    }
}
