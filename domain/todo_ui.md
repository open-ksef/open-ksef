# Invoice UI Module — Task List

Reference docs (all paths relative to `domain/`):
- `01` = `01-domain-specification.md`
- `02` = `02-validation-specification.md`
- `03` = `03-test-scenarios.md`
- `04` = `04-refactor-backlog.md`
- `05` = `05-ui-specification.md`
- `06` = `06-ui-test-scenarios.md`

Line references use format `05:§5.4.2` = file 05, section 5.4.2. Scenario IDs (`API-###`, `UI*-###`, `MOB-###`) come from `06`.

This list assumes the domain layer in `todo.md` is fully complete (all iterations 0–6 and cross-cutting X1–X4). The UI layer implementation is tracked independently here.

---

## Iteration 0 — API boundary

### E0-S1: Controller skeleton
> Backlog: `05:§5.4`

- [x] Create `src/OpenKSeF.Api/Controllers/InvoicesAggregateController.cs` at route `api/tenants/{tenantId:guid}/invoices/aggregate`
- [x] Apply `[Authorize]` and tenant ownership check mirroring `InvoicesController`
- [x] Wire DI for every command handler (`ICreateInvoiceHandler`, `IUpdateInvoiceDraftHandler`, `IApproveInvoiceHandler`, `IReopenInvoiceHandler`, `ICreateCorrectionFromOriginalHandler`, `ICreateFinalInvoiceFromAdvancesHandler`, `IRecordKsefAcceptanceHandler`, `IRecordKsefRejectionHandler`)
- [x] Wire DI for projectors (`InvoiceReadDtoProjector`, `InvoicePrintModelProjector`)
- [x] Verify: no logic in controller beyond HTTP translation — `04:L294-L296` architectural rule

### E0-S2: Request/response DTOs
> Backlog: `05:§5.4.2`

- [x] Define request DTOs in `OpenKSeF.Invoices.Contracts/Dtos/Requests/` — `CreateInvoiceRequest`, `UpdateInvoiceDraftRequest`, `ApproveInvoiceRequest`, `ReopenInvoiceRequest`, `CreateCorrectionFromOriginalRequest`, `CreateFinalInvoiceFromAdvancesRequest`, `RecordKsefAcceptanceRequest`, `RecordKsefRejectionRequest`
- [x] Decide mapping strategy command-contract → request-DTO (reuse existing command records directly or wrap)
- [x] Reuse existing `InvoiceReadDto`, `InvoicePrintModel`, `DuplicatePrintInfo` without modification
- [x] Document enum JSON format (PascalCase strings) in a single JsonConverter

### E0-S3: Error envelope
> Backlog: `05:§5.4.4`

- [x] Implement `ValidationEnvelope` response type (`stage`, `messages[]`)
- [x] Implement middleware / exception filter mapping `ValidationResult` → 422 with envelope
- [x] Implement mapping for state-transition errors → 409 with envelope
- [x] Ensure every `INV-VAL-###` code is present in the central X2 registry before shipping
- [x] Localized Polish messages sourced from `02-validation-specification.md` (single source of truth)

### E0-S4: Endpoint implementations
> Backlog: `05:§5.4.2`

- [x] Endpoint 1 — `GET /` list with filters `status`, `kind`, `buyerKind`, `from`, `to`, `page`, `pageSize`
- [x] Endpoint 2 — `GET /{id}` detail via `InvoiceReadDtoProjector`
- [x] Endpoint 3 — `POST /` create draft
- [x] Endpoint 4 — `PATCH /{id}/draft` update draft
- [x] Endpoint 5 — `POST /{id}/approve`
- [x] Endpoint 6 — `POST /{id}/reopen`
- [x] Endpoint 7 — `POST /{id}/submit-to-ksef`
- [x] Endpoint 8 — `POST /{id}/ksef-acceptance`
- [x] Endpoint 9 — `POST /{id}/ksef-rejection`
- [x] Endpoint 10 — `POST /{id}/corrections`
- [x] Endpoint 11 — `POST /final-from-advances`
- [x] Endpoint 12 — `GET /{id}/print?variant=`
- [x] Endpoint 13 — `GET /{id}/duplicates`

### E0-S5: Contract tests (`API-###`)
> Backlog: `06:§6.1`

- [x] `API-001..003` list and detail
- [x] `API-004..005` create draft happy + validation path
- [x] `API-006` update draft idempotency
- [x] `API-007` approve envelope grouping
- [x] `API-008` reopen policy
- [x] `API-009` submit-to-ksef state guard
- [x] `API-010` correction creation
- [x] `API-011` final-from-advances mixed buyers
- [x] `API-012..013` print variants
- [x] `API-014` tenant isolation

### E0-S6: Legacy rename cleanup on API side
> Backlog: `05:§5.11`

- [x] Rename `src/OpenKSeF.Api/Models/InvoiceResponse.cs` → `SyncedInvoiceResponse.cs` in place
- [x] Rename `InvoiceLineResponse` → `SyncedInvoiceLineResponse`
- [x] Verify legacy controllers return the renamed type and JSON field names are unchanged
- [x] Verify: zero references to old type name `InvoiceResponse` remain in `src/OpenKSeF.Api`

---

## Iteration 1 — Portal foundation

### E1-S1: Tech stack additions
> Backlog: `05:§5.5.1`

- [x] `npm install react-hook-form zod @hookform/resolvers` in `src/OpenKSeF.Portal.Web`
- [x] Verify `npm run build` and `npm run lint` still pass
- [x] Update `package.json` engines/deps list

### E1-S2: Shared schemas
> Backlog: `05:§5.5.4`

- [x] Create `src/api/schemas/invoice.ts` with enum schemas (`DocumentKind`, `DocumentStatus`, `BuyerKind`, `PricingMode`, `KsefSubmissionRequirement`, `KsefSubmissionState`, `CorrectionReasonKind`) matching `05:§5.3`
- [x] Request schemas: `createInvoiceRequestSchema`, `updateInvoiceDraftRequestSchema`, `createCorrectionFromOriginalRequestSchema`, `createFinalInvoiceFromAdvancesRequestSchema`
- [x] Response schemas: `invoiceReadDtoSchema`, `invoicePrintModelSchema`, `duplicatePrintInfoSchema`, `validationEnvelopeSchema`
- [x] Refinements: NIP mod-11 checksum, IBAN format, ISO-4217 currency
- [x] Create `src/api/schemas/ruleCodes.ts` with the `INV-VAL-###` registry for family grouping (no Polish strings)

### E1-S3: API client module
> Backlog: `05:§5.5.3`

- [x] Create `src/api/invoicesAggregateApi.ts` with typed functions per endpoint
- [x] Implement `InvoiceValidationError` class carrying `stage` and `messages[]`
- [x] Throw `InvoiceValidationError` from non-2xx responses so callers can catch it uniformly
- [x] Parse every response through zod so shape drift is caught at call-site

### E1-S4: CSS variables
> Backlog: `05:§5.5.6`

- [x] Add `--status-draft`, `--status-approved`, `--status-submitted`, `--status-accepted`, `--status-rejected` to `src/components/ui.css`
- [x] Add `--severity-error`, `--severity-warning`
- [x] Define `@media print` stylesheet scaffold for print-only layout

### E1-S5: Shared component scaffolds
> Backlog: `05:§5.7`

- [x] `src/components/invoices/DocumentStatusBadge.tsx` (+ test scenarios `UIS-001..005`)
- [x] `src/components/invoices/DocumentKindChip.tsx` (+ `UIS-006`)
- [x] `src/components/invoices/SourceChip.tsx`
- [x] `src/components/invoices/KsefSubmissionStatus.tsx` (+ `UIS-007`)
- [x] `src/components/invoices/KsefRequirementBanner.tsx`
- [x] `src/components/invoices/KsefIdentifiersCard.tsx`
- [x] `src/components/invoices/ValidationMessageList.tsx` (+ `UIV-001..008`)
- [x] `src/components/invoices/MoneyInput.tsx` / `MoneyDisplay.tsx`
- [x] `src/components/invoices/NipInput.tsx` (mod-11 check)
- [x] `src/components/invoices/CurrencySelect.tsx`
- [x] `src/components/invoices/VatRateSelect.tsx`
- [x] `src/components/invoices/PartyCard.tsx` / `PartyFormSection.tsx`
- [x] `src/components/invoices/IssueDatesFieldset.tsx`
- [x] `src/components/invoices/DocumentNumberPreview.tsx`
- [x] `src/components/invoices/TotalsSummaryCard.tsx`
- [x] `src/components/invoices/VatSummaryTable.tsx`
- [x] `src/components/invoices/InvoiceLineEditor.tsx` (both standard and correction variants)
- [x] `src/components/invoices/InvoiceLineTable.tsx`
- [x] `src/components/invoices/CorrectionReferenceCard.tsx`
- [x] `src/components/invoices/AdvanceAllocationPicker.tsx` / `AdvanceAllocationList.tsx`
- [x] `src/components/invoices/DuplicateIssuanceBanner.tsx`
- [x] `src/components/invoices/PrintVariantSwitcher.tsx`

---

## Iteration 2 — Read flows

### E2-S1: Refactor invoice list
> Backlog: `05:§5.6.1`

- [x] Refactor `src/pages/InvoiceList.tsx` in place — no parallel file
- [x] Merge aggregate list (`listAggregateInvoices`) and synced list (`listInvoices`) into one table with `SourceChip`
- [x] Filter chips: status (multi), kind (multi), buyer kind (single), source, date range
- [x] Row links route to `/invoices/aggregate/:id` or `/invoices/:ksefInvoiceNumber` based on source
- [x] Header actions "Nowa faktura" and "Finalna z zaliczek"
- [x] Empty state
- [x] Tests: `UIL-001..008`

### E2-S2: Aggregate invoice detail page
> Backlog: `05:§5.6.2`

- [x] Create `src/pages/InvoiceAggregateDetail.tsx`
- [x] Wire `getAggregateInvoice` query
- [x] Render all sections from `05:§5.6.2`
- [x] Render status-aware action buttons (per `05:§5.8`)
- [x] Start polling when status is `SubmittedToKsef`
- [x] Tests: `UID-001..010`

### E2-S3: Rename synced detail page
> Backlog: `05:§5.11.2`

- [x] Rename `src/pages/InvoiceDetails.tsx` → `src/pages/SyncedInvoiceDetail.tsx`
- [x] Update `src/api/types.ts` rename (`InvoiceResponse` → `SyncedInvoiceResponse`, `InvoiceLineResponse` → `SyncedInvoiceLineResponse`)
- [x] Update React Query keys from `['invoices', ...]` to `['invoices', 'synced', ...]`
- [x] Update router entries
- [x] Tests: `UIR-001..005`

---

## Iteration 3 — Draft CRUD

### E3-S1: Create draft form
> Backlog: `05:§5.6.3`

- [x] Create `src/pages/InvoiceDraftCreate.tsx`
- [x] Wire react-hook-form with `createInvoiceRequestSchema` resolver
- [x] Seller prefill from current tenant
- [x] Live `KsefRequirementBanner` reacting to buyer kind + NIP
- [x] Live `TotalsSummaryCard` preview from line data
- [x] Submit → `createInvoiceDraft` → navigate to new detail
- [x] Tests: `UIC-001..009`

### E3-S2: Edit draft form
> Backlog: `05:§5.6.4`

- [x] Create `src/pages/InvoiceDraftEdit.tsx`
- [x] Prefill from `getAggregateInvoice`
- [x] Redirect to detail if status is not `Draft`
- [x] Patch payload contains only changed fields
- [x] Submit → `updateInvoiceDraft`
- [x] Tests: `UIE-001..006`

### E3-S3: Client-side rule mirroring
> Backlog: `05:§5.9.2`

- [x] Mirror in zod: `INV-VAL-001..003`, `INV-VAL-010..013`, `INV-VAL-020..022`, `INV-VAL-040`, `INV-VAL-050..053`
- [x] Never replicate: numbering, VAT reconciliation, advance/final settlement, correction chain, KSeF requirement, state machine, technical KSeF
- [x] Document in each schema which rule it mirrors

---

## Iteration 4 — Approve & reopen

### E4-S1: Approve review screen
> Backlog: `05:§5.6.5`

- [x] Create `src/pages/InvoiceApproveReview.tsx`
- [x] Render read-only invoice with client-side preview of errors
- [x] "Zatwierdź" button dispatches `approveInvoice`
- [x] On failure, render `ValidationMessageList` grouped by family
- [x] Navigate to detail on success
- [x] Tests: `UIA-001..002`, `UIA-005..006`

### E4-S2: Reopen flow
> Backlog: `05:§5.8`

- [ ] "Odblokuj do edycji" button on detail for eligible statuses
- [ ] Gated by server response (`reopenAllowed` flag on `InvoiceReadDto`); backend must expose this
- [ ] Disabled button with tooltip when not allowed, referencing `INV-VAL-102`
- [ ] Tests: `UIA-003..004`

---

## Iteration 5 — Correction & final-from-advances

### E5-S1: Correction flow
> Backlog: `05:§5.6.6`

- [ ] Create `src/pages/InvoiceCorrectionCreate.tsx`
- [ ] Load original read-only
- [ ] `InvoiceLineEditor` in correction variant (before/after)
- [ ] `CorrectionReasonSelect` covering all `CorrectionReasonKind`
- [ ] Submit → `createCorrectionFromOriginal`
- [ ] Hide "Utwórz korektę" on proforma detail (`COR-005`)
- [ ] Tests: `UIX-001..008`

### E5-S2: Final-from-advances flow
> Backlog: `05:§5.6.7`

- [ ] Create `src/pages/InvoiceFinalFromAdvances.tsx`
- [ ] Buyer picker sourced from advances list
- [ ] `AdvanceAllocationPicker` with running total
- [ ] Final header (issue date, due date) confirmation
- [ ] Submit → `createFinalInvoiceFromAdvances`
- [ ] Tests: `UIF-001..006`

---

## Iteration 6 — KSeF submission

### E6-S1: Submit screen
> Backlog: `05:§5.6.9`

- [ ] Create `src/pages/InvoiceKsefSubmit.tsx`
- [ ] `submitInvoiceToKsef` mutation
- [ ] Polling with 3s interval on `getAggregateInvoice` until `Accepted` or `Rejected`, max 2 minutes
- [ ] Fallback manual "Odśwież" button after timeout
- [ ] Tests: `UIK-001..006`

### E6-S2: Post-transition UX
> Backlog: `05:§5.8`

- [ ] Accepted: show identifiers, "Drukuj" button, navigate-to-detail
- [ ] Rejected: show rejection reason, "Popraw i zatwierdź ponownie", "Utwórz korektę"
- [ ] Tests: `UIK-002..003`

---

## Iteration 7 — Print variants

### E7-S1: Print viewer
> Backlog: `05:§5.6.8`

- [ ] Create `src/pages/InvoicePrintView.tsx`
- [ ] `PrintVariantSwitcher` with three segments
- [ ] `getInvoicePrint` query with variant parameter
- [ ] `@media print` layout hiding navigation
- [ ] Duplicate disabled before acceptance with tooltip referencing `IMM-003`
- [ ] Tests: `UIP-001..008`

### E7-S2: English labels from backend
> Backlog: `UI-ASSUMPTION-004`, `05:§5.6.8`

- [ ] Verify no English literals exist in portal source for invoice labels
- [ ] All English strings sourced from `PrintLabels` in response
- [ ] Tests: `UIP-002..003`

---

## Iteration 8 — Mobile minimal slice

### E8-S1: DTO rename
> Backlog: `05:§5.10.1`

- [ ] `Models/InvoiceDto.cs` → `SyncedInvoiceDto.cs` (rename in place)
- [ ] `Models/InvoiceLineDto.cs` → `SyncedInvoiceLineDto.cs`
- [ ] `Models/CachedInvoice.cs` → `CachedSyncedInvoice.cs`
- [ ] Update `IApiService` method signatures
- [ ] Update XAML `x:DataType` bindings and code-behind
- [ ] Bump SQLite schema version and clear cache on upgrade
- [ ] Tests: `MOB-001..003`, `MOB-007`

### E8-S2: Status/kind badges
> Backlog: `05:§5.10.2`

- [ ] Create `Controls/DocumentStatusBadge.xaml`
- [ ] Create `Controls/DocumentKindBadge.xaml`
- [ ] Create `Controls/KsefStatusBadge.xaml`
- [ ] Bind on `InvoiceListPage.xaml` and `InvoiceDetailsPage.xaml`
- [ ] Neutral "Sync" pill fallback for rows without status
- [ ] Polish strings only
- [ ] Tests: `MOB-004..006`

### E8-S3: Smoke verification
> Backlog: `05:§5.10.3`

- [ ] Appium Android smoke tier passes against renamed DTOs
- [ ] No regressions in existing invoice list / detail / transfer copy flows

### E8-S4: Optional — backend legacy controller status projection
> Backlog: `05:§5.10.2`

- [ ] Decide whether legacy `InvoicesController` should additionally project aggregate status for synced rows that have a matching aggregate
- [ ] If yes: extend `SyncedInvoiceResponse` with optional `status`, `kind`, `ksefSubmissionState`
- [ ] If no: document that mobile badges remain neutral "Sync" until mobile adopts the aggregate controller

---

## Cross-cutting tasks

- [ ] **X1 Rename cleanup (portal)** — zero references to `InvoiceResponse` / `InvoiceLineResponse` remain in `src/OpenKSeF.Portal.Web`. Scenario `UIR-003`. (`05:§5.11.2`)
- [ ] **X2 Rename cleanup (mobile)** — zero references to `InvoiceDto` / `InvoiceLineDto` / `CachedInvoice` remain in `src/OpenKSeF.Mobile`. Scenario `MOB-007`. (`05:§5.10.1`)
- [ ] **X3 Rule-code governance** — CI test enforces that every code surfaced by `InvoicesAggregateController` exists in the registry driving the existing domain X2 uniqueness test. (`05:§5.9.4`)
- [ ] **X4 Architectural guard** — portal test verifies that draft/approve/reopen/submit/correction/final flows never call the legacy `InvoicesController` mutation endpoints (only `/paid` stays legacy). (`04:L294-L296`, `05:§5.11.3`)
- [ ] **X5 Accessibility baseline** — automated check that every status badge has non-color differentiators (icon + text) and every modal traps focus. (`05:§5.13`, `UIV-008`)
- [ ] **X6 Docs index** — update `domain/README.md` with the three new files (`05-ui-specification.md`, `06-ui-test-scenarios.md`, `todo_ui.md`) and mention ADR-002 tie-in for print variants
- [ ] **X7 Shape-drift detection** — zod response parsing throws in dev mode when the backend sends unknown enum values; CI treats this as a failure during integration tests

