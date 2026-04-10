# ADR-001: Naming — `Invoice` Aggregate vs `SyncedInvoice` Read Model

**Status:** Accepted  
**Date:** 2026-04-10  
**Authors:** OpenKSeF team

---

## Context

The legacy OpenKSeF codebase used `InvoiceHeader` and `InvoiceLine` as EF entity names for invoices
downloaded and cached from KSeF (Krajowy System e-Faktur). These names were chosen historically and
reflect the read/sync nature of that data — they are not domain aggregates owned by this system.

When the Invoice Issuance domain was designed, two naming concerns collided:

1. The business concept "Invoice" — the document this system issues, validates, and submits.
2. The infrastructure concept "InvoiceHeader" — the EF entity that stores synced-from-KSeF invoice data.

Using `Invoice` for both would make the codebase ambiguous and error-prone.

---

## Decision

Reserve the name **`Invoice`** exclusively for the domain aggregate root in `OpenKSeF.Invoices.Domain`.

Rename the legacy sync/read-side entities:

| Legacy name     | New name              | Layer           |
|-----------------|-----------------------|-----------------|
| `InvoiceHeader` | `SyncedInvoice`       | EF entity       |
| `InvoiceLine`   | `SyncedInvoiceLine`   | EF entity       |

Align PostgreSQL table names accordingly:

| Legacy table      | New table               |
|-------------------|-------------------------|
| `InvoiceHeaders`  | `SyncedInvoices`        |
| `InvoiceLines`    | `SyncedInvoiceLines`    |

The new `Invoice` aggregate (write side) is persisted via `IssuedInvoices` / `IssuedInvoiceLines` tables
to maintain clear separation from the sync/read side.

---

## Rationale

- **Ubiquitous language**: The word "invoice" in the business domain refers to a document issued by the
  seller. Having the system's primary domain concept named `Invoice` aligns code with business language.
- **Eliminate ambiguity**: `SyncedInvoice` is self-describing — it is a snapshot synchronized from KSeF,
  not a domain-owned aggregate.
- **Clean layering**: The read model (`SyncedInvoice`) lives in `OpenKSeF.Domain` (legacy EF layer).
  The aggregate (`Invoice`) lives in `OpenKSeF.Invoices.Domain` (pure domain, no EF). Names reinforce
  the architectural boundary.

---

## Consequences

- EF migration `RenameInvoiceHeaderToSyncedInvoiceAndAddIssuedInvoices` performs the PostgreSQL rename.
- All consumers of `InvoiceHeader` must be updated to `SyncedInvoice` (tracked in X1).
- `[Obsolete]` attributes guide remaining callers toward the renamed type.
- New code must only refer to `SyncedInvoice` / `SyncedInvoiceLine` for the sync model; `Invoice` is reserved for the domain aggregate.

---

## References

- `01-domain-specification.md` §1.0 — Refactor context and naming rationale
- `04-refactor-backlog.md` §4.4 X1 — Naming cleanup task
- `OpenKSeF.Domain/Entities/InvoiceHeader.cs` — legacy entity (marked `[Obsolete]`)
- `OpenKSeF.Domain/Entities/SyncedInvoice.cs` — renamed EF entity
- `OpenKSeF.Invoices.Domain/Aggregates/Invoice.cs` — domain aggregate root
