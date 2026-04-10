namespace OpenKSeF.Invoices.Domain.Events;

public interface IDomainEvent
{
    DateTime OccurredAt { get; }
}
