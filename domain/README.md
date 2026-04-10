# Invoice Issuance & Validation Domain Documentation

Artifacts prepared from analysis of:
- `open-ksef` current codebase
- `profak` invoicing model and KSeF-related behaviors

## Files
- `01-domain-specification.md` — domain model, lifecycle, invariants, policies
- `02-validation-specification.md` — validation catalog, rule codes, severity, execution stages
- `03-test-scenarios.md` — Given/When/Then scenarios for positive, negative, edge, and regression cases
- `04-refactor-backlog.md` — backlog and rollout plan for refactoring OpenKSeF toward clean architecture

## Design intent
This documentation targets a refactor of OpenKSeF from a sync/browse-oriented read model toward a domain-capable invoicing module that:
- can author and validate invoices,
- can decide whether KSeF submission is required,
- can prepare data for KSeF integration,
- stays explicit about what is legal/compliance vs configurable business policy.

This documentation is intentionally refactor-oriented. It does not describe only the target domain model, but also the migration path from the current PostgreSQL-backed synchronization schema to a naming model where:
- the new business aggregate is `Invoice`,
- the current synchronized/read-side model is renamed to `SyncedInvoice`,
- PostgreSQL table and column names may require staged rename migrations or compatibility views during rollout.

## Current repository observations
### open-ksef
Current `OpenKSeF.Domain` is not yet a business domain for invoice issuing. It mostly persists synchronized invoice headers/lines and KSeF credentials.
Notable observations:
- `InvoiceHeader` is a persistence/data model for synced invoices, not a behavioral aggregate.
- uniqueness exists for `(TenantId, KSeFInvoiceNumber)`.
- `InvoiceHeader` contains current fields such as vendor/buyer data, amounts, currency, payment status, line collection.
- the current naming makes the read model look like the business model, which blocks introducing `Invoice` as the real aggregate root cleanly.

### profak
ProFak provides several useful reference behaviors:
- configurable numbering (`Numerator`, grouping, per-purpose counters),
- document kinds including `Sprzedaż`, `Proforma`, `KorektaSprzedaży`, `Zaliczka`, `Rozliczenie`, `Duplikat` as print concern, etc.,
- correction mechanics with base/original/corrected invoice references,
- KSeF send flow and generated structured XML,
- print-layer customization, which supports the decision that English print is presentation-only.

## Naming decision
This documentation uses:
- aggregate root: `Invoice`
- line entity: `InvoiceLine`
- synchronized/read-side model: `SyncedInvoice`
- synchronized/read-side line model: `SyncedInvoiceLine`
- read/export DTO for KSeF mapping: `KsefInvoicePayload`
- presentation model: `InvoicePrintModel`

Recommended refactor direction:
- rename current persistence/read models from `InvoiceHeader` / `InvoiceLine` to `SyncedInvoice` / `SyncedInvoiceLine`,
- reserve `Invoice` / `InvoiceLine` for the new issuing domain,
- plan PostgreSQL rename migrations explicitly instead of keeping domain naming distorted by legacy table names.

Where needed, mapping to current persistence models and staged migration concerns is documented explicitly.
