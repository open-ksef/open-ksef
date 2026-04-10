using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Application.Commands.RecordKsefAcceptance;

public sealed class RecordKsefAcceptanceHandler : IRecordKsefAcceptanceHandler
{
    public void Handle(Invoice invoice, RecordKsefAcceptanceCommand command)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(command);

        var identifiers = new KsefIdentifiers(command.KsefDocumentNumber, command.KsefReferenceNumber);
        invoice.AcceptByKsef(identifiers, command.AcceptedAt);
    }
}
