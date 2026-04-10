using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Application.Commands.RecordKsefRejection;

public sealed class RecordKsefRejectionHandler : IRecordKsefRejectionHandler
{
    public void Handle(Invoice invoice, RecordKsefRejectionCommand command)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(command);

        invoice.RejectByKsef(command.RejectionReason, command.RejectedAt);
    }
}
