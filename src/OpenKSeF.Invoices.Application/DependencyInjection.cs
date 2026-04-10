using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Invoices.Application.Commands.ApproveInvoice;
using OpenKSeF.Invoices.Application.Commands.CreateCorrectionFromOriginal;
using OpenKSeF.Invoices.Application.Commands.CreateFinalInvoiceFromAdvances;
using OpenKSeF.Invoices.Application.Commands.CreateInvoice;
using OpenKSeF.Invoices.Application.Commands.RecordKsefAcceptance;
using OpenKSeF.Invoices.Application.Commands.RecordKsefRejection;
using OpenKSeF.Invoices.Application.Commands.ReopenInvoice;
using OpenKSeF.Invoices.Application.Commands.UpdateInvoiceDraft;
using OpenKSeF.Invoices.Application.Projection;
using OpenKSeF.Invoices.Contracts.Dtos;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Policies;
using OpenKSeF.Invoices.Domain.Projection;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;
using System.Reflection;

namespace OpenKSeF.Invoices.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddInvoiceApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICreateInvoiceHandler, CreateInvoiceHandler>();
        services.AddScoped<IUpdateInvoiceDraftHandler, UpdateInvoiceDraftHandler>();
        services.AddScoped<IApproveInvoiceHandler, ApproveInvoiceHandler>();
        services.AddScoped<IReopenInvoiceHandler, ReopenInvoiceHandler>();
        services.AddScoped<ICreateCorrectionFromOriginalHandler, CreateCorrectionFromOriginalHandler>();
        services.AddScoped<ICreateFinalInvoiceFromAdvancesHandler, CreateFinalInvoiceFromAdvancesHandler>();
        services.AddScoped<IRecordKsefAcceptanceHandler, RecordKsefAcceptanceHandler>();
        services.AddScoped<IRecordKsefRejectionHandler, RecordKsefRejectionHandler>();

        services.AddScoped<InvoiceReadDtoProjector>();
        services.AddScoped<IInvoiceReadModelProjector<InvoiceReadDto>, InvoiceReadDtoProjector>();
        services.AddScoped<Func<PrintVariant, InvoicePrintModelProjector>>(sp =>
        {
            var policy = sp.GetRequiredService<IApprovedEditPolicy>();
            return variant => new InvoicePrintModelProjector(variant, policy);
        });
        services.AddScoped<Func<PrintVariant, IInvoiceReadModelProjector<InvoicePrintModel>>>(sp =>
        {
            var factory = sp.GetRequiredService<Func<PrintVariant, InvoicePrintModelProjector>>();
            return variant => factory(variant);
        });

        services.AddScoped<ApprovalValidationService>();
        services.AddScoped<DraftValidationService>();
        services.AddScoped<KsefSubmissionValidationService>();

        services.AddScoped<IApprovedEditPolicy, DefaultApprovedEditPolicy>();
        services.AddScoped<ICorrectionPolicy, DefaultCorrectionPolicy>();
        services.AddSingleton<IClock, SystemClock>();

        RegisterClosedImplementations(services, typeof(IDomainValidationRule<>), typeof(ValidationContext).Assembly);
        RegisterClosedImplementations(services, typeof(IStateTransitionRule<>), typeof(ValidationContext).Assembly);
        RegisterClosedImplementations(services, typeof(IKsefTechnicalValidationRule<>), typeof(ValidationContext).Assembly);

        return services;
    }

    private static void RegisterClosedImplementations(
        IServiceCollection services,
        Type openGenericServiceType,
        Assembly assembly)
    {
        var implementationTypes = assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false });

        foreach (var implementationType in implementationTypes)
        {
            var serviceTypes = implementationType
                .GetInterfaces()
                .Where(interfaceType =>
                    interfaceType.IsGenericType &&
                    interfaceType.GetGenericTypeDefinition() == openGenericServiceType);

            foreach (var serviceType in serviceTypes)
            {
                services.AddScoped(serviceType, implementationType);
            }
        }
    }

    private sealed class DefaultApprovedEditPolicy : IApprovedEditPolicy
    {
        public bool CanReopen(Invoice invoice) => invoice.Status == DocumentStatus.Approved;
    }

    private sealed class DefaultCorrectionPolicy : ICorrectionPolicy
    {
        public bool CanCorrect(Invoice original) =>
            original.Kind != DocumentKind.Proforma &&
            original.Status == DocumentStatus.AcceptedByKsef;
    }

    private sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
