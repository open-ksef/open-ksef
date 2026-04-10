# ADR-002: Duplicate Invoice — Print Action, Not a New Aggregate

**Status:** Accepted  
**Date:** 2026-04-10  
**Authors:** OpenKSeF team

---

## Context

Under Polish fiscal law, a seller may issue a **duplicate** of an already-issued invoice when the buyer
claims the original was lost or destroyed. The duplicate must be clearly marked "DUPLIKAT" (Polish for
"Duplicate") and carry the date of re-issuance alongside the original invoice data.

Two implementation options were considered:

1. **Duplicate as a new fiscal aggregate** — create a second `Invoice` document of kind `Duplicate`,
   linked to the original, with its own lifecycle and KSeF submission.
2. **Duplicate as a presentation/print action** — record the re-issuance event as metadata on the
   existing aggregate; render a special print variant that displays the duplicate stamp and date.

---

## Decision

Implement duplicate issuance as a **presentation action on the existing `Invoice` aggregate**.

- `Invoice.RecordDuplicateIssue(issuedAt, issuedBy)` appends a `DuplicateMetadata` entry to the
  aggregate's `DuplicateIssuances` collection. The aggregate's fiscal data is not mutated.
- A dedicated `PrintVariant.Duplicate` renders the duplicate stamp, re-issuance date, and issuer
  alongside the original fiscal data unchanged.
- `InvoicePrintModelProjector` handles the variant; `InvoicePrintModel` carries `DuplicatePrintInfo`.
- The duplicate print is only available for invoices in `Accepted` (KSeF-accepted) status.

---

## Rationale

- **Simpler domain model**: A duplicate is legally the same document re-printed, not a new fiscal
  document. No new KSeF submission is required; no new aggregate identity is needed.
- **Auditability without complexity**: Recording `DuplicateMetadata` on the aggregate preserves a
  full trail of who printed what and when, without introducing a parallel document lifecycle.
- **Lower regulatory risk**: Creating a duplicate as a new KSeF submission could be misinterpreted
  as a second invoice for the same sale. Keeping it purely presentational avoids that risk.
- **Forward compatibility**: If future regulations require a duplicate to become a first-class event
  (e.g. for auditing systems), the metadata trail can be promoted to a `DocumentPrintEvent` domain
  event without restructuring the aggregate hierarchy.

---

## Consequences

- Controllers must call `RecordDuplicateIssue` on the existing `Invoice` (not create a new document).
- The print infrastructure must select `PrintVariant.Duplicate` and supply the `DuplicatePrintInfo`
  (including the latest `DuplicateMetadata` entry).
- Polish label set includes `DuplicateLabel = "DUPLIKAT"` rendered prominently on duplicate prints.
- English print variant is a label-swap only and is independent of the duplicate mechanism.
- Immutability test `IMM-003` validates that `RecordDuplicateIssue` does not change fiscal totals.

---

## References

- `01-domain-specification.md` §1.10.6 — Duplicate-as-metadata decision and trade-off
- `01-domain-specification.md` §1.10.7 — English print variant decision
- `OpenKSeF.Invoices.Domain/Aggregates/Invoice.cs` — `RecordDuplicateIssue`, `DuplicateIssuances`
- `OpenKSeF.Invoices.Domain/ValueObjects/DuplicateMetadata.cs` — the metadata value object
- `OpenKSeF.Invoices.Contracts/Dtos/InvoicePrintModel.cs` — `PrintVariant`, `PrintLabels`, `DuplicatePrintInfo`
- `OpenKSeF.Invoices.Application/Projection/InvoicePrintModelProjector.cs` — projector implementation
- `03-test-scenarios.md` `IMM-003`, `REG-003`, `REG-004` — test coverage for print variants
