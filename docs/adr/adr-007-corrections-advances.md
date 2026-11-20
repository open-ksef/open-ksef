# ADR-007: Corrections, advances and final invoices in the issuing model

Status: proposed (2026-11-20). Extends ADR-001. Follows ADR-004.

## Context

- Issuing writes only `RodzajFaktury = VAT` (`src/OpenKSeF.Sync/Issuing/Fa3InvoiceWriter.cs:82`). REQ-0027 (KOR) and REQ-0028 (ZAL, ROZ, KOR_ZAL, KOR_ROZ) need corrections of accepted invoices and order chains.
- `IssuedInvoice` is the aggregate and the send outbox (`src/OpenKSeF.Domain/Invoicing/IssuedInvoice.cs:8`, ADR-001 §3). XML is built once at issue and stored (`IssuedInvoiceService.cs:288-295`). The Worker sends the stored bytes and never calls the writer (`IFa3InvoiceWriter` has one consumer, `IssuedInvoiceService.cs:15`).
- One counter per tenant and period: PK `(TenantId, Period)`, `Period` is `varchar(7)` (`ApplicationDbContext.cs:250-253`). One pattern per profile (`SellerProfile.cs:528`).
- `MigrationPolicyTests` rejects a unique index or a foreign key on an existing table (`MigrationPolicyTests.cs:104-110`).
- Released mobile builds edit a draft with `PUT /issued-invoices/{id}` (`open-ksef-mobile: ApiService.cs:179-181`) and issue from the details page without saving first (`IssuedInvoiceDetailsViewModel.cs:233`). Only buyer kind or currency makes a draft portal-only (`IssuingFormat.cs:42`).
- Synced invoices already carry `invoiceType` with KSeF codes (`Domain/Entities/InvoiceHeader.cs:26`, `Api/Models/InvoiceResponse.cs:26`).

## Decision

1. **One table, one entity.** All six kinds are `IssuedInvoice` rows. A new column `InvoiceType` holds the KSeF code (`VAT`, `KOR`, `ZAL`, `ROZ`, `KOR_ZAL`, `KOR_ROZ`), default `VAT`. On the wire it is a string with the same values, named `invoiceType` as on synced invoices.
2. **Every row stores its full state.** Buyer, lines, currency, rate and (ZAL) amount received are the document's state after the change. `TotalNet`, `TotalVat` and `TotalGross` store the document's own `P_13`, `P_14` and `P_15` sums. They are signed on corrections and equal the full state on VAT, so VAT rows keep their meaning.
3. **Correction lines are linked to the state they correct.** `IssuedInvoiceLines.BaseLineNo` points to the line in the corrected state. Null means an added line. A corrected-state line that has no after line is a removed line. The `StanPrzed` before and after rows are computed from (state, after), never stored. One pure function `Corrections` in `Domain/Invoicing` produces the rows and the bucket deltas. The writer, the view and the totals all call it.
4. **Current state is computed.** For an original O: the latest Accepted document among O and the rows with `CorrectedInvoiceId = O.Id`, ordered by `IssuedAt`, then `CreatedAt`. Nothing is materialised on O, so the Worker stays the only writer of KSeF results and never updates a second row.
5. **One open document.** For a correction it is one per original, for ZAL/ROZ one per chain. Open means Draft, Queued, Sending or Rejected. A Rejected row whose number is already taken in KSeF counts as closed. Creation locks the tenant (`FOR KEY SHARE`, as in `IssuedInvoiceService.cs:276`), then the original row (or the chain's first ZAL) with `FOR UPDATE`, then checks. This invariant keeps corrections linear, which item 4 depends on.
6. **References without foreign keys.** `CorrectedInvoiceId` is the original that `DaneFaKorygowanej` names. `OrderChainId` is the first ZAL's Id. Both are plain `uuid` columns with non-unique indexes and are set only from tenant-scoped loads.
7. **Numbering by series.** FV is `NumberingPattern` with the existing `InvoiceNumberCounters` and covers VAT and ROZ. KOR is a new `CorrectionNumberingPattern` and covers KOR, KOR_ZAL and KOR_ROZ. ZAL is a new `AdvanceNumberingPattern`. KOR and ZAL counters go to a new table `NumberSeriesCounters (TenantId, Series, Period, Next)`. The FV counter rows are not touched. Each series has an optional next-number override, using the same lock-and-check as `SellerProfileService.cs:69-88`.
8. **Separate write endpoints per kind.** `PUT /{id}` stays VAT-only. On any other kind it returns 409 `INV-KIND-MISMATCH` and changes nothing. `POST /{id}/issue` and `POST /{id}/copy` on a non-VAT row need `?invoiceType=<code>`, otherwise they return the same 409. Released mobile builds therefore cannot overwrite or issue a correction they render as VAT.
9. **Writer extends, VAT bytes fixed.** `Write(invoice, generatedAtUtc, Fa3Refs? refs = null)`. `Fa3Refs` carries the original, the corrected state and the advance KSeF numbers. Elements that only other kinds have are null for VAT, and `XElement` drops nulls. Golden byte tests for every VAT variant are committed from the unchanged writer before the first writer change.
10. **Two additive migrations.** KOR ships in one release and ZAL/ROZ in the next. There is no data rewrite, no removal and no allow-list entry.

## Consequences

- Existing VAT rows read `InvoiceType = 'VAT'` from the column default. Their stored XML is never regenerated, because only editable drafts reach the writer (`IssuedInvoiceService.cs:257`).
- Rollback by image (ADR-004 D2) works on the schema. But previous images read every row as VAT. A KOR draft opened in a previous API could be issued as a VAT invoice with the full after-state lines, which is a duplicate sale. The rollback runbook deletes non-VAT drafts first and lists non-VAT Rejected rows.
- Released mobile builds show a correction as an invoice: number `KOR/...`, signed `totalGross`, after-state lines (ADR-004 C4). A save or an issue returns 409 with a message that points to the portal.
- The view of an original loads its corrections through the new index. Chains are short. This is acceptable until a profile shows otherwise.
- The current state is only as good as the one-open invariant. Both the create path and the issue path re-check it under the row lock.

## Rejected

- Separate `IssuedCorrection` table or aggregate: it would duplicate the send outbox, the escalation and the Worker for the same lifecycle.
- Materialised current state on the original: the Worker would write two rows per acceptance. It is a cache with no reader that needs it.
- Stored before rows (`IsBefore` lines): duplicate data. The corrected state already holds them.
- Storing deltas per line (difference method): the PO chose `StanPrzed`, and a state replay is needed for every read.
- A `Series` column in the PK of `InvoiceNumberCounters`: it rebuilds the PK on a live table (M3, D3 rehearsal).
- Series encoded in `Period` (`KOR:2026-11`): it needs widening `varchar(7)` (M3) and hides the key in a string.
- Self-FK on `CorrectedInvoiceId`/`OrderChainId`: M3 allow-list. `DeleteTenantAsync` bulk-deletes all rows of a tenant in one statement (`IssuedInvoiceService.cs:358`).
- Partial unique index for "one open correction": a unique index on an existing table (M3). The row lock is enough.
- New fields on the VAT `PUT` instead of new endpoints: a released build's body cannot be told apart from a new client's.
- `IsPortalOnly` alone as the guard: the server must protect drafts from builds already installed.
