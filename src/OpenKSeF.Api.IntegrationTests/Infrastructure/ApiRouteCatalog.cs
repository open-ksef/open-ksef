using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace OpenKSeF.Api.IntegrationTests.Infrastructure;

/// <summary>Every controller action of the API as (method, template, parameters), read by reflection, so a test over all endpoints sees a new one.</summary>
public static partial class ApiRouteCatalog
{
    public sealed record ApiRoute(string Method, string Template, IReadOnlyList<string> Parameters)
    {
        public string Key => $"{Method} {Template}";
    }

    public static IReadOnlyList<ApiRoute> Discover() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(controller => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(action => action.GetCustomAttributes<HttpMethodAttribute>()
                    .SelectMany(http => http.HttpMethods.Select(method => Route(controller, action, http, method)))))
            .OrderBy(r => r.Template).ThenBy(r => r.Method)
            .ToList();

    private static ApiRoute Route(Type controller, MethodInfo action, HttpMethodAttribute http, string method)
    {
        var prefix = (controller.GetCustomAttribute<RouteAttribute>()?.Template ?? "")
            .Replace("[controller]", controller.Name[..^"Controller".Length].ToLowerInvariant());
        var template = RouteConstraint().Replace(
            string.IsNullOrEmpty(http.Template) ? prefix : $"{prefix}/{http.Template}", "{$1}");
        var routeParameters = RouteParameter().Matches(template).Select(m => m.Groups[1].Value);
        var queryParameters = action.GetParameters()
            .Where(p => p.GetCustomAttribute<FromQueryAttribute>() is not null)
            .Select(p => p.Name!);
        return new ApiRoute(method, template, routeParameters.Concat(queryParameters).ToList());
    }

    [GeneratedRegex(@"\{(\w+):[^}]*\}")]
    private static partial Regex RouteConstraint();

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex RouteParameter();
}
