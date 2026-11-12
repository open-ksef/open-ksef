using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Tests.Controllers;

public class AccountControllerTests
{
    [Fact]
    public async Task GenerateSetupToken_SigningKeyCannotBeDecrypted_Returns503_KeepsRow()
    {
        var systemConfig = Substitute.For<ISystemConfigService>();
        systemConfig.CannotDecrypt(SystemConfigKeys.SetupTokenSigningKey).Returns(true);
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("user-1");
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var controller = new AccountController(
            currentUser,
            new SetupTokenService(systemConfig, NullLogger<SetupTokenService>.Instance),
            Substitute.For<IKeycloakTokenExchangeService>(),
            Substitute.For<IKeycloakUserService>(),
            db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.GenerateSetupToken();

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(result).StatusCode);
        await systemConfig.DidNotReceive().SetValueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
