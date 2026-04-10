using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Events;

public sealed record InvoiceDrafted(InvoiceId InvoiceId, DateTime OccurredAt) : IDomainEvent;

public sealed record InvoiceApproved(InvoiceId InvoiceId, DateTime OccurredAt) : IDomainEvent;

public sealed record InvoiceApprovalReverted(InvoiceId InvoiceId, DateTime OccurredAt) : IDomainEvent;

public sealed record InvoiceSubmittedToKsef(InvoiceId InvoiceId, DateTime OccurredAt) : IDomainEvent;

public sealed record InvoiceAcceptedByKsef(
    InvoiceId InvoiceId,
    KsefIdentifiers Identifiers,
    DateTime OccurredAt) : IDomainEvent;

public sealed record InvoiceRejectedByKsef(
    InvoiceId InvoiceId,
    string RejectionReason,
    DateTime OccurredAt) : IDomainEvent;

public sealed record InvoiceDuplicateIssued(InvoiceId InvoiceId, DateTime OccurredAt) : IDomainEvent;

public sealed record InvoiceCorrectionIssued(
    InvoiceId CorrectionInvoiceId,
    InvoiceId OriginalInvoiceId,
    DateTime OccurredAt) : IDomainEvent;
