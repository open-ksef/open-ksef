using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Invoices.Domain.Integration;
using OpenKSeF.Invoices.Domain.Persistence;
using OpenKSeF.Invoices.Infrastructure.Mapping;
using OpenKSeF.Invoices.Infrastructure.Persistence;
using OpenKSeF.Invoices.Infrastructure.Validation;

namespace OpenKSeF.Invoices.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInvoiceInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IssuedInvoiceAggregateMapper>();
        services.AddScoped<IInvoiceRepository, EfInvoiceRepository>();
        services.AddScoped<IInvoiceToKsefPayloadMapper, InvoiceToKsefPayloadMapper>();
        services.AddScoped<IKsefXmlSchemaValidator>(_ => new KsefXmlSchemaValidator());

        return services;
    }
}
