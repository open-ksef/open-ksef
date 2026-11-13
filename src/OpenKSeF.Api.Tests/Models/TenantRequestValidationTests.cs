using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.Models;

namespace OpenKSeF.Api.Tests.Models;

public class TenantRequestValidationTests
{
    private const string TooLong = "Nazwa może mieć najwyżej 200 znaków.";

    public static TheoryData<object> LongDisplayName => new()
    {
        new CreateTenantRequest("1111111111", new string('a', 201), null),
        new UpdateTenantRequest(new string('a', 201)),
    };

    public static TheoryData<object> MaxDisplayName => new()
    {
        new CreateTenantRequest("1111111111", new string('a', 200), null),
        new UpdateTenantRequest(new string('a', 200)),
    };

    [Theory]
    [MemberData(nameof(LongDisplayName))]
    public void DisplayName_Over200_IsInvalidWithPolishMessage(object request)
    {
        var modelState = Validate(request);

        Assert.Equal(TooLong, Assert.Single(modelState["DisplayName"]!.Errors).ErrorMessage);
    }

    [Theory]
    [MemberData(nameof(MaxDisplayName))]
    public void DisplayName_Of200_IsValid(object request)
    {
        Assert.True(Validate(request).IsValid);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" jan@example.com ", "jan@example.com")]
    public void NotificationEmail_CreateAndUpdate_NormalizeTheSame(string email, string? expected)
    {
        var json = JsonSerializer.Serialize(new { nip = "1111111111", notificationEmail = email });
        var create = JsonSerializer.Deserialize<CreateTenantRequest>(json, JsonSerializerOptions.Web)!;
        var update = JsonSerializer.Deserialize<UpdateTenantRequest>(json, JsonSerializerOptions.Web)!;

        Assert.True(Validate(create).IsValid);
        Assert.True(Validate(update).IsValid);
        Assert.Equal(expected, create.NotificationEmail);
        Assert.Equal(expected, update.NotificationEmail);
        Assert.True(update.NotificationEmailSet);
    }

    private static Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary Validate(object model)
    {
        var services = new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider();
        var actionContext = new ActionContext(new DefaultHttpContext { RequestServices = services }, new RouteData(), new ActionDescriptor());
        services.GetRequiredService<IObjectModelValidator>().Validate(actionContext, null, "", model);
        return actionContext.ModelState;
    }
}
