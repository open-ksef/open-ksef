using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using OpenKSeF.Api.Filters;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Validation;

namespace OpenKSeF.Api.Tests.Controllers;

public class InvoiceValidationExceptionFilterTests
{
    private static readonly string RuleCodeMappingPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "domain", "rule-code-mapping.md"));

    [Fact]
    public async Task OnExceptionAsync_MapsValidationResultTo422Envelope()
    {
        var filter = new InvoiceValidationExceptionFilter(new InvoiceValidationSpecificationCatalog());
        var context = CreateContext(new InvoiceDomainException(
            "Approval blocked.",
            stage: ValidationStage.Approve,
            validationResult: new ValidationResult(
            [
                new ValidationMessage(
                    "INV-VAL-060",
                    ValidationSeverity.Error,
                    ValidationStage.Approve,
                    "ignored user message",
                    "ignored technical message",
                    "lines[2].vatRate")
            ])));

        await filter.OnExceptionAsync(context);

        var result = Assert.IsType<UnprocessableEntityObjectResult>(context.Result);
        var envelope = Assert.IsType<ValidationEnvelope>(result.Value);

        Assert.Equal("Approve", envelope.Stage);
        var message = Assert.Single(envelope.Messages);
        Assert.Equal("INV-VAL-060", message.Code);
        Assert.Equal("Error", message.Severity);
        Assert.Equal("lines[2].vatRate", message.Field);
        Assert.NotEqual("ignored user message", message.MessagePl);
        Assert.NotEqual("ignored technical message", message.MessageTechnical);
    }

    [Fact]
    public async Task OnExceptionAsync_MapsStateTransitionRuleTo409Envelope()
    {
        var filter = new InvoiceValidationExceptionFilter(new InvoiceValidationSpecificationCatalog());
        var context = CreateContext(new InvoiceDomainException(
            "Reopen is not allowed.",
            ruleCode: "INV-VAL-102",
            stage: ValidationStage.Approve));

        await filter.OnExceptionAsync(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);

        var envelope = Assert.IsType<ValidationEnvelope>(result.Value);
        Assert.Equal("Approve", envelope.Stage);
        var message = Assert.Single(envelope.Messages);
        Assert.Equal("INV-VAL-102", message.Code);
        Assert.Equal("Error", message.Severity);
        Assert.Null(message.Field);
        Assert.NotEmpty(message.MessagePl);
        Assert.NotEmpty(message.MessageTechnical);
    }

    [Fact]
    public void ValidationSpecificationCatalog_ContainsEveryRuleCodeFromMappingRegistry()
    {
        var catalog = new InvoiceValidationSpecificationCatalog();

        var registryCodes = File.ReadAllText(RuleCodeMappingPath)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(line => System.Text.RegularExpressions.Regex.Matches(line, @"INV-VAL-\d{3}")
                .Select(match => match.Value))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(registryCodes);
        Assert.True(
            registryCodes.SetEquals(catalog.GetAllCodes()),
            $"Registry mismatch. Missing: {string.Join(", ", registryCodes.Except(catalog.GetAllCodes()).OrderBy(x => x))}; Extra: {string.Join(", ", catalog.GetAllCodes().Except(registryCodes).OrderBy(x => x))}");
    }

    private static ExceptionContext CreateContext(Exception exception)
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ExceptionContext(actionContext, [])
        {
            Exception = exception
        };
    }
}
