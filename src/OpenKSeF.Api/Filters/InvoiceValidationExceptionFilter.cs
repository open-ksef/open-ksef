using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Validation;

namespace OpenKSeF.Api.Filters;

public sealed class InvoiceValidationExceptionFilter(IInvoiceValidationSpecificationCatalog catalog) : IAsyncExceptionFilter
{
    public Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is not InvoiceDomainException exception)
        {
            return Task.CompletedTask;
        }

        if (exception.ValidationResult is not null)
        {
            var stage = exception.Stage ?? InferStage(exception.ValidationResult);
            var envelope = new ValidationEnvelope(
                stage.ToString(),
                exception.ValidationResult.Messages
                    .Select(message => ToEnvelopeMessage(message.Code, message.Severity, message.Path))
                    .ToArray());

            context.Result = new UnprocessableEntityObjectResult(envelope);
            context.ExceptionHandled = true;
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(exception.RuleCode))
        {
            return Task.CompletedTask;
        }

        var mappedStage = exception.Stage ?? catalog.GetRequired(exception.RuleCode).Stages.First();
        var stateMessage = ToEnvelopeMessage(exception.RuleCode, catalog.GetRequired(exception.RuleCode).Severity, field: null);
        context.Result = new ObjectResult(new ValidationEnvelope(mappedStage.ToString(), [stateMessage]))
        {
            StatusCode = StatusCodes.Status409Conflict
        };
        context.ExceptionHandled = true;
        return Task.CompletedTask;
    }

    private ValidationEnvelopeMessage ToEnvelopeMessage(string code, ValidationSeverity severity, string? field)
    {
        var entry = catalog.GetRequired(code);
        return new ValidationEnvelopeMessage(
            code,
            severity.ToString(),
            field,
            entry.MessagePl,
            entry.MessageTechnical);
    }

    private static ValidationStage InferStage(ValidationResult validationResult) =>
        validationResult.Messages.FirstOrDefault()?.Stage ?? ValidationStage.Draft;
}
