using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Application.Commands.CreateInvoice;

public sealed class CreateInvoiceHandler : ICreateInvoiceHandler
{
    public Invoice Handle(CreateInvoiceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var seller = new SellerSnapshot(
            new PartyName(command.SellerName),
            new Nip(command.SellerNip));

        var buyer = new BuyerSnapshot(
            new PartyName(command.BuyerName),
            command.BuyerKind,
            string.IsNullOrWhiteSpace(command.BuyerNip) ? null : new Nip(command.BuyerNip));

        return Invoice.Draft(
            InvoiceId.New(),
            new TenantId(command.TenantId),
            command.Kind,
            seller,
            buyer,
            new CurrencyCode(command.Currency),
            command.IssueDate,
            command.KsefSubmissionRequirement,
            documentNumber: string.IsNullOrWhiteSpace(command.DocumentNumber)
                ? null
                : new DocumentNumber(command.DocumentNumber),
            externalReference: command.ExternalReference);
    }
}
