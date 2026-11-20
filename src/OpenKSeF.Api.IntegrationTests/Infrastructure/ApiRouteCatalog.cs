using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace OpenKSeF.Api.IntegrationTests.Infrastructure;

/// <summary>Every controller action of the running API as (method, template, route and query parameters), read from MVC's action descriptors, so a test over all endpoints sees a new one with its inferred binding sources.</summary>
public static class ApiRouteCatalog
{
    public sealed record ApiRoute(string Method, string Template, IReadOnlyList<string> RouteParameters, IReadOnlyList<string> QueryParameters)
    {
        public string Key => $"{Method} {Template}";

        public IEnumerable<string> Parameters => RouteParameters.Concat(QueryParameters);
    }

    public static IReadOnlyList<ApiRoute> Discover(IServiceProvider services) =>
        services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .SelectMany(action => (action.ActionConstraints ?? []).OfType<HttpMethodActionConstraint>()
                .SelectMany(constraint => constraint.HttpMethods)
                .Select(method => Route(action, method)))
            .OrderBy(r => r.Template).ThenBy(r => r.Method)
            .ToList();

    private static ApiRoute Route(ActionDescriptor action, string method)
    {
        var pattern = RoutePatternFactory.Parse(action.AttributeRouteInfo!.Template!);
        var template = string.Join('/', pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content.ToLowerInvariant(),
            RoutePatternSeparatorPart separator => separator.Content,
            RoutePatternParameterPart parameter => $"{{{parameter.Name}}}",
            _ => throw new InvalidOperationException($"Unknown route part in {action.DisplayName}"),
        }))));
        var queryParameters = action.Parameters
            .Where(p => p.BindingInfo?.BindingSource == BindingSource.Query)
            .Select(p => p.BindingInfo!.BinderModelName ?? p.Name);
        return new ApiRoute(method, template, pattern.Parameters.Select(p => p.Name).ToList(), queryParameters.ToList());
    }
}
