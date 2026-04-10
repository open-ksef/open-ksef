using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Application.Commands.CreateFinalInvoiceFromAdvances;

public interface ICreateFinalInvoiceFromAdvancesHandler
{
    /// <summary>
    /// Creates a draft final invoice that references and settles the given advance invoices.
    /// </summary>
    /// <param name="advances">The approved advance invoices to settle. Must all share the same seller, buyer and currency.</param>
    /// <param name="command">Command with tenant, issue date and per-advance settlement amounts.</param>
    Invoice Handle(IReadOnlyList<Invoice> advances, CreateFinalInvoiceFromAdvancesCommand command);
}
