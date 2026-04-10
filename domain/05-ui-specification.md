# 5. UI Specification — Invoice Portal & API Surface

## 5.0 Refactor context
This specification is written for a refactor, not a greenfield UI module. It is the presentation-layer companion to `01-domain-specification.md`.

At the time of writing:
- the domain layer in `OpenKSeF.Invoices.*` is complete (see `todo.md` iterations 0–6 and cross-cutting X1–X4, all checked),
- the legacy EF entities have been renamed `InvoiceHeader` → `SyncedInvoice` and `InvoiceLine` → `SyncedInvoiceLine` per `01:L11-L20` and `adr-001`,
- the **UI layer has not yet caught up**:
  - `OpenKSeF.Api` exposes only `InvoicesController` / `InvoicesSummaryController` over the `SyncedInvoice` browse model. No HTTP endpoints expose the `Invoice` aggregate, its command handlers, or `InvoiceReadDtoProjector` / `InvoicePrintModelProjector`.
  - `src/OpenKSeF.Portal.Web` has `InvoiceList.tsx` and `InvoiceDetails.tsx` only, both read-only. No create/edit/approve/correction/print flows. Forms use hand-rolled `useState`, no schema validation library, Polish-only strings, no i18n framework.
  - `open-ksef-mobile` has `InvoiceListPage` / `InvoiceDetailsPage` backed by `InvoiceDto` / `CachedInvoice` which still use the legacy shape and will break once the API response is renamed.

This document is the source of truth for adding the missing presentation layer. Downstream AI agents implement against this doc and `06-ui-test-scenarios.md`; progress is tracked in `todo_ui.md`.

## 5.1 Scope
In scope:
- a new API controller exposing the `Invoice` aggregate command/query surface so portal and mobile can drive it over REST,
- Portal Web screens, flows, routing, shared components, state-machine UX, validation surfacing, print variant viewer,
- minimal mobile (MAUI) alignment: DTO rename matching the backend rename and new read-only status/kind badges on existing list and detail pages.

Out of scope in this iteration:
- full mobile parity (create/edit/approve/submit/print) — see `05.10` for the minimal mobile slice,
- introducing an i18n framework — portal UI stays Polish-only; English is used **only** for print-label strings sourced from the backend,
- adopting a new design system / component library — existing `components/ui.css` remains the styling baseline,
- multi-tenant bulk operations, attachment management, ledger/accounting views.

## 5.2 ASSUMPTIONs
- `UI-ASSUMPTION-001`: Portal and mobile interact with the domain exclusively through REST endpoints — no UI code references `OpenKSeF.Invoices.Domain` or `OpenKSeF.Invoices.Application` types directly. See `01:L75-L82`.
- `UI-ASSUMPTION-002`: Portal keeps two noun spaces side by side: **`Invoice`** for anything sourced from the new aggregate (`InvoiceReadDto`, create/edit/approve flows), and **`SyncedInvoice`** for the existing KSeF browse cache. Both surfaces are visible in `/invoices` but rendered from distinct endpoints.
- `UI-ASSUMPTION-003`: Portal forms for the new aggregate use `react-hook-form` + `zod` + `@hookform/resolvers`. Existing `TenantList` manual state pattern is left alone.
- `UI-ASSUMPTION-004`: All user-facing strings in the portal are Polish. English appears only on the English print variant and is sourced from the backend `PrintLabels` record. No i18next.
- `UI-ASSUMPTION-005`: Client-side zod schemas mirror a subset of domain validation for fast feedback, but the server remains the authority. Every blocking error surfaced in the UI carries its `INV-VAL-###` code so support can cross-reference `02-validation-specification.md`.
- `UI-ASSUMPTION-006`: Mobile stays read-only in this round and pins to a stable DTO contract (`05.10`). Future mobile CRUD is a separate iteration.
- `UI-ASSUMPTION-007`: React Query remains the server-state cache. No Redux, no Zustand, no RxJS introduction.
- `UI-ASSUMPTION-008`: The legacy `InvoicesController` / `InvoicesSummaryController` are **not** deleted or rewritten. They continue to serve the `SyncedInvoice` browse model. A new controller is added for the aggregate.

## 5.3 Ubiquitous language — domain to UI mapping

Every enum / value object / state the UI surfaces maps to a domain concept from `01:L136-L287`. This table is the contract; any new UI symbol that is not listed here must be added here first.

| Domain concept | Source in `01` | API DTO field (`InvoiceReadDto`) | Portal component | Mobile binding |
|---|---|---|---|---|
| `Invoice` aggregate | `01:L138-L146` | `InvoiceReadDto` (whole) | `InvoiceDetailPage` (aggregate variant) | read-only detail (planned for later iteration) |
| `InvoiceLine` entity | `01:L218-L239` | `InvoiceReadDto.Lines[]` | `InvoiceLineEditor`, `InvoiceLineTable` | read-only list cell |
| `DocumentKind` | `01:L148-L153` | `Kind` | `DocumentKindChip`, `DocumentKindSelect` | `DocumentKindBadge` (new) |
| `DocumentStatus` | `01:L154-L160`, `01:L318-L370` | `Status` | `DocumentStatusBadge`, state-transition buttons | `DocumentStatusBadge` (new) |
| `BuyerKind` | `01:L161-L164` | `BuyerKind` | `BuyerKindSelect`, buyer card | display only |
| `PricingMode` | `01:L226`, `01:L268` | `Lines[].PricingMode` | `PricingModeToggle` on line editor | not surfaced |
| `KsefSubmissionRequirement` | `01:L207`, `01:L269-L273` | `KsefSubmissionRequirement` | `KsefRequirementBanner` | display only |
| `KsefSubmissionState` | `01:L208`, `01:L274-L279` | `KsefSubmissionState` | `KsefSubmissionStatus` | `KsefStatusBadge` (new) |
| `CorrectionReasonKind` | `01:L280-L286` | `CorrectionReference.ReasonKind` | `CorrectionReasonSelect` | not surfaced |
| `Money`, `CurrencyCode` | `01:L185-L192`, `01:L243-L244` | `MoneyReadDto` | `MoneyInput`, `MoneyDisplay`, `CurrencySelect` | text display |
| `Percentage`, `VatRate` | `01:L245-L246` | `Lines[].VatRate` | `VatRateSelect`, `PercentageInput` | text display |
| `VatClassification` / `TaxExemptionReason` | `01:L247-L248` | `Lines[].VatClassification`, `Lines[].ExemptionReason` | `VatClassificationPicker` | not surfaced |
| `Nip` | `01:L249` | `PartyReadDto.Nip` | `NipInput` with mod-11 check | text display |
| `PartyName` / `PostalAddress` | `01:L250-L251` | `PartyReadDto.Name/Address` | `PartyCard`, `PartyFormSection` | text display |
| `BankAccountNumber` | `01:L252` | `BankAccount` | `BankAccountInput` (IBAN format) | transfer copy action |
| `DocumentNumber` | `01:L242` | `DocumentNumber` | `DocumentNumberPreview` (shows policy-resolved next number) | text display |
| `IssueDates` | `01:L176-L183`, `01:L253` | `IssueDate`, `SaleDate`, `DueDate` | `IssueDatesFieldset` | text display |
| `KsefIdentifiers` | `01:L207-L212`, `01:L254` | `KsefDocumentNumber`, `KsefReferenceNumber` | `KsefIdentifiersCard` | display only |
| `DocumentTotals` / `VatSummary` | `01:L186-L192`, `01:L256-L257` | `TotalNet/Vat/Gross`, `VatBreakdown[]` | `TotalsSummaryCard`, `VatSummaryTable` | text display |
| `AdvanceAllocation` | `01:L216`, `01:L258` | `SettledAdvanceAllocations[]` | `AdvanceAllocationPicker` | not surfaced |
| `CorrectionReference` | `01:L214`, `01:L259` | `CorrectionReference` | `CorrectionReferenceCard` | not surfaced |
| `DuplicateMetadata` | `01:L182`, `01:L260`, `01:L449-L457` | `DuplicateIssuances[]` | `DuplicateIssuanceBanner`, `PrintVariantSwitcher` | not surfaced |
| `ValidationSeverity` / `ValidationStage` | `01:L266-L267`, `02:L38-L49` | error envelope `messages[]` | `ValidationMessageList` | not surfaced (v1) |

The enums above are the only canonical names the portal must know. Any discovery screen, filter chip, or form dropdown that lists "types", "statuses", or "kinds" must use these names — not free strings.

## 5.4 API surface — new controller

### 5.4.1 Placement
A new controller `InvoicesAggregateController` is added under the same tenant-scoped route family already used by the legacy controller:

- base route: `api/tenants/{tenantId:guid}/invoices/aggregate`
- placed in `src/OpenKSeF.Api/Controllers/InvoicesAggregateController.cs`
- request/response DTOs live in `src/OpenKSeF.Invoices.Contracts/Dtos/` (new DTOs) and `src/OpenKSeF.Api/Models/` (HTTP-specific envelopes only)
- authorization mirrors `InvoicesController`: `[Authorize]`, tenant ownership verified via `ICurrentUserService.UserId` and `Tenants.AnyAsync(...)`
- DI wiring registers every command handler (`ICreateInvoiceHandler`, `IUpdateInvoiceDraftHandler`, `IApproveInvoiceHandler`, `IReopenInvoiceHandler`, `ICreateCorrectionFromOriginalHandler`, `ICreateFinalInvoiceFromAdvancesHandler`, `IRecordKsefAcceptanceHandler`, `IRecordKsefRejectionHandler`) and both projectors (`IInvoiceReadModelProjector<InvoiceReadDto>`, `InvoicePrintModelProjector`)

The legacy `InvoicesController` and `InvoicesSummaryController` are **not modified** in this iteration except for renaming the response type as described in `5.11`.

### 5.4.2 Endpoint catalog

Every endpoint below is required for the Portal flows defined in `5.6`. Error envelope is described in `5.9`.

| # | HTTP | Route (suffix of base) | Request DTO | Response DTO | Backing command/projector | Runs stage | Rule families mirrored client-side |
|---|---|---|---|---|---|---|---|
| 1 | `GET` | `/` | query: `status`, `kind`, `buyerKind`, `from`, `to`, `page`, `pageSize` | `PagedResult<InvoiceReadDto>` | `IInvoiceReadModelProjector` over repository list | — | — |
| 2 | `GET` | `/{id:guid}` | — | `InvoiceReadDto` | `InvoiceReadDtoProjector.Project` | — | — |
| 3 | `POST` | `/` | `CreateInvoiceRequest` | `InvoiceReadDto` | `ICreateInvoiceHandler` + `DraftValidationService` | `Draft` | structure, parties, dates, currency (soft) |
| 4 | `PATCH` | `/{id:guid}/draft` | `UpdateInvoiceDraftRequest` | `InvoiceReadDto` | `IUpdateInvoiceDraftHandler` + `DraftValidationService` | `Draft` | same as 3 + lines, VAT (soft) |
| 5 | `POST` | `/{id:guid}/approve` | `ApproveInvoiceRequest` | `InvoiceReadDto` + `ValidationResult` | `IApproveInvoiceHandler` + `ApprovalValidationService` | `Approve` | all `INV-VAL-001..102` blocking |
| 6 | `POST` | `/{id:guid}/reopen` | `ReopenInvoiceRequest` | `InvoiceReadDto` | `IReopenInvoiceHandler` (`IApprovedEditPolicy`) | state rule | `INV-VAL-102` |
| 7 | `POST` | `/{id:guid}/submit-to-ksef` | — | `InvoiceReadDto` | submission workflow + `KsefSubmissionValidationService` | `SendToKsef` | `INV-VAL-090..112` |
| 8 | `POST` | `/{id:guid}/ksef-acceptance` | `RecordKsefAcceptanceRequest` | `InvoiceReadDto` | `IRecordKsefAcceptanceHandler` | state rule | `ST-005` equivalent |
| 9 | `POST` | `/{id:guid}/ksef-rejection` | `RecordKsefRejectionRequest` | `InvoiceReadDto` | `IRecordKsefRejectionHandler` | state rule | `ST-006` equivalent |
| 10 | `POST` | `/{id:guid}/corrections` | `CreateCorrectionFromOriginalRequest` | `InvoiceReadDto` (correction) | `ICreateCorrectionFromOriginalHandler` | `Draft` | `INV-VAL-080..083` |
| 11 | `POST` | `/final-from-advances` | `CreateFinalInvoiceFromAdvancesRequest` | `InvoiceReadDto` (final) | `ICreateFinalInvoiceFromAdvancesHandler` | `Draft` | `INV-VAL-070..073` |
| 12 | `GET` | `/{id:guid}/print?variant=Standard\|Duplicate\|English` | — | `InvoicePrintModel` | `InvoicePrintModelProjector.Project(variant)` | — | — |
| 13 | `GET` | `/{id:guid}/duplicates` | — | `DuplicatePrintInfo[]` | aggregate `DuplicateIssuances` read | — | — |

Endpoint 8 and 9 are explicit rather than implicit so that a future KSeF callback worker can POST them and so portal tests can drive state transitions deterministically. Endpoint 7 is the user-initiated send; the worker-side flow is orthogonal.

### 5.4.3 List filter semantics (endpoint 1)
- `status` is multi-valued: `Draft`, `Approved`, `SubmittedToKsef`, `AcceptedByKsef`, `RejectedByKsef` (domain names from `01:L154-L160`, transported as PascalCase strings).
- `kind` is multi-valued over `DocumentKind` (`01:L148-L153`).
- `buyerKind` is single-valued over `BuyerKind` (`01:L161-L164`).
- `from` / `to` filter on `IssueDate` inclusive.
- Default sort: `IssueDate desc, DocumentNumber desc`.
- `page` is 1-based, default 1. `pageSize` defaults to 25, max 100.

### 5.4.4 Error envelope

All blocking validation responses share a single shape, carried by 422 Unprocessable Entity for domain/validation errors and 409 Conflict for state-transition errors:

```
{
  "stage": "Approve" | "Draft" | "SendToKsef",
  "messages": [
    {
      "code": "INV-VAL-060",
      "severity": "Error" | "Warning",
      "field": "lines[2].vatRate" | null,
      "messagePl": "Pozycja wymaga określenia stawki VAT lub podstawy zwolnienia.",
      "messageTechnical": "Line VAT treatment unresolved: no rate and no exemption."
    }
  ]
}
```

- `code` must appear in the central `INV-VAL-###` registry enforced by the existing X2 governance test (`todo.md` cross-cutting).
- `messagePl` is the user-facing string; the portal must never fabricate its own Polish text for a known rule code.
- `field` is a JSON-path-like pointer into the request DTO; when null the message is document-level.
- The portal surfaces these messages through the shared `ValidationMessageList` component (`5.7`).

### 5.4.5 Auth and tenant scoping
- `[Authorize]` with `sub`/`email` claims read via `ICurrentUserService`.
- Every endpoint runs `VerifyTenantOwnership(tenantId)` before handler dispatch, returning 404 on mismatch (never 403, to avoid leaking tenant existence).
- No role split in v1; all authenticated owners of a tenant can issue any command. Role-based split is out of scope.

## 5.5 Portal Web architecture

### 5.5.1 Tech stack deltas
Add the following NPM dependencies to `src/OpenKSeF.Portal.Web/package.json`:

- `react-hook-form`
- `zod`
- `@hookform/resolvers`

Justification: tenant forms are small (NIP + name + a handful of fields), so the current manual `useState` pattern is acceptable there. Invoice forms carry ~30 fields, nested line arrays, dependent validation (pricing mode changes line math, buyer kind changes KSeF banner, correction role drives line editor variants). Hand-rolling this in `useState` will not scale and will not produce the same error-surfacing UX as the backend `ValidationMessageList`.

No other library additions. No Redux, no Zustand, no component library, no i18n framework, no date library swap (continue with native `toLocaleDateString('pl-PL')`).

### 5.5.2 Routes
File: `src/OpenKSeF.Portal.Web/src/router.tsx`.

Preserve existing routes. Add the following:

| Route | Page component | Purpose |
|---|---|---|
| `/invoices` | `InvoiceList` (refactored) | merged list over aggregate + synced sources |
| `/invoices/new` | `InvoiceDraftCreate` | create a new draft |
| `/invoices/aggregate/:id` | `InvoiceAggregateDetail` | detail view for an aggregate-backed invoice |
| `/invoices/aggregate/:id/edit` | `InvoiceDraftEdit` | edit a draft |
| `/invoices/aggregate/:id/approve` | `InvoiceApproveReview` | approval review with validation preview |
| `/invoices/aggregate/:id/submit` | `InvoiceKsefSubmit` | submit to KSeF and poll result |
| `/invoices/aggregate/:id/corrections/new` | `InvoiceCorrectionCreate` | create correction from this original |
| `/invoices/aggregate/:id/print` | `InvoicePrintView` | print variant viewer |
| `/invoices/final-from-advances` | `InvoiceFinalFromAdvances` | pick advances, generate final draft |
| `/invoices/:ksefInvoiceNumber` | `SyncedInvoiceDetail` (renamed from `InvoiceDetailsPage`) | detail view for a synced KSeF-imported invoice |

The `aggregate/` path segment keeps the two identity spaces disjoint: `/:ksefInvoiceNumber` resolves a synced row, `/aggregate/:id` resolves an aggregate by GUID. The list page links to whichever matches.

### 5.5.3 API client layer
New module: `src/OpenKSeF.Portal.Web/src/api/invoicesAggregateApi.ts`.

- imports and reuses `ApiClient` from `src/api/client.ts` (unchanged),
- exports one named function per endpoint in `5.4.2` (e.g. `listAggregateInvoices`, `getAggregateInvoice`, `createInvoiceDraft`, `updateInvoiceDraft`, `approveInvoice`, `reopenInvoice`, `submitInvoiceToKsef`, `recordKsefAcceptance`, `recordKsefRejection`, `createCorrectionFromOriginal`, `createFinalInvoiceFromAdvances`, `getInvoicePrint`, `listInvoiceDuplicates`),
- response parsing goes through zod schemas in `src/api/schemas/invoice.ts` so runtime shape drift is caught immediately and every call-site gets inferred types,
- errors from the envelope in `5.4.4` are thrown as a typed `InvoiceValidationError` carrying `stage` and `messages[]` so `ValidationMessageList` can render them without re-parsing.

### 5.5.4 Shared zod schemas
File: `src/OpenKSeF.Portal.Web/src/api/schemas/invoice.ts`.

- one schema per request DTO (mirrors `CreateInvoiceRequest`, `UpdateInvoiceDraftRequest`, `CreateCorrectionFromOriginalRequest`, `CreateFinalInvoiceFromAdvancesRequest`),
- one schema per response DTO (`InvoiceReadDto`, `InvoicePrintModel`, `DuplicatePrintInfo`, `ValidationResult`),
- enum schemas for `DocumentKind`, `DocumentStatus`, `BuyerKind`, `PricingMode`, `KsefSubmissionRequirement`, `KsefSubmissionState`, `CorrectionReasonKind` defined exactly as the table in `5.3`,
- client-side-only refinements (NIP mod-11 checksum, IBAN length, currency code ISO-4217 presence) are added to the request schemas so users see fast feedback; these duplicate server rules but never replace them.

### 5.5.5 State management
- `@tanstack/react-query` (already present) handles fetching, caching, mutation.
- Query key conventions:
  - `['invoices', 'aggregate', 'list', { tenantId, filters }]`
  - `['invoices', 'aggregate', 'detail', { tenantId, id }]`
  - `['invoices', 'aggregate', 'print', { tenantId, id, variant }]`
  - `['invoices', 'aggregate', 'duplicates', { tenantId, id }]`
  - `['invoices', 'synced', 'list', ...]` (existing, unchanged)
- Invalidation rules:
  - `createInvoiceDraft` / `updateInvoiceDraft` / `approveInvoice` / `reopenInvoice` / `recordKsefAcceptance` / `recordKsefRejection` → invalidate `list` and the specific `detail`,
  - `submitInvoiceToKsef` → invalidate `detail`, start polling (`refetchInterval`) on `detail` until `KsefSubmissionState` is `Accepted` or `Rejected`, then stop,
  - `createCorrectionFromOriginal` / `createFinalInvoiceFromAdvances` → invalidate `list` and fetch the new draft's detail.

### 5.5.6 Styling
- Extend `src/components/ui.css` with CSS variables for state and severity colors. Naming convention: `--status-draft`, `--status-approved`, `--status-submitted`, `--status-accepted`, `--status-rejected`, `--severity-error`, `--severity-warning`.
- Badges use a filled pill with color + icon + text so meaning does not depend on color alone (`5.13`).
- No Tailwind, no CSS-in-JS, no MUI. Existing class-based CSS continues.

## 5.6 Screens

Each subsection describes one screen: purpose, route, data queries, form schema, interactions, allowed state transitions, visible validation surfaces, test scenario IDs.

### 5.6.1 Invoice list (merged)
- **Route**: `/invoices`
- **Page component**: `src/pages/InvoiceList.tsx` (refactored in place per CLAUDE.md — no parallel file)
- **Purpose**: single entry point showing both aggregate-backed and KSeF-synced invoices, filterable by state, kind, buyer kind, date range.
- **Queries**:
  - `listAggregateInvoices({ tenantId, ...filters })`
  - existing `listInvoices({ tenantId, ...filters })` (synced)
- **Sources are visually merged** in one table; each row shows a `SourceChip` (`Aggregate` / `KSeF sync`) so users can tell them apart.
- **Columns**: document number, kind chip, status badge, buyer name, issue date, gross amount, KSeF state.
- **Filter chips**: status (multi), kind (multi), buyer kind (single), source (single), date range.
- **Actions on each row**: link to the appropriate detail route (`/invoices/aggregate/:id` or `/invoices/:ksefInvoiceNumber`).
- **Header actions**: "Nowa faktura" (→ `/invoices/new`), "Finalna z zaliczek" (→ `/invoices/final-from-advances`).
- **Validation surfaces**: none.
- **Scenarios**: `UIL-001..UIL-008` in `06`.

### 5.6.2 Aggregate invoice detail
- **Route**: `/invoices/aggregate/:id`
- **Page component**: new `src/pages/InvoiceAggregateDetail.tsx`
- **Purpose**: read-only view of an aggregate invoice (any status), with state-machine action buttons.
- **Query**: `getAggregateInvoice({ tenantId, id })` (`5.4.2` endpoint 2).
- **Sections**:
  - header: document number, kind chip, status badge, KSeF requirement banner, KSeF state banner,
  - parties: `PartyCard` for seller and buyer,
  - dates: issue, sale, due, approved-at, submitted-at, accepted-at,
  - commercial: payment method, bank account, public notes,
  - lines: `InvoiceLineTable` (read-only variant),
  - totals: `TotalsSummaryCard`, `VatSummaryTable`,
  - correction reference (when kind is `CorrectionInvoice`): `CorrectionReferenceCard`,
  - advance allocation (when kind is `FinalInvoice`): `AdvanceAllocationList`,
  - duplicates: `DuplicateIssuanceBanner` if any,
  - KSeF identifiers: `KsefIdentifiersCard` if present.
- **Action buttons** (shown per status):
  - `Draft`: "Edytuj", "Zatwierdź" (→ approve review), "Usuń" (out of scope v1, shown disabled),
  - `Approved`: "Wyślij do KSeF", "Odblokuj do edycji" (gated by `IApprovedEditPolicy`, surfaces `INV-VAL-102` when forbidden),
  - `SubmittedToKsef`: no actions, polling active,
  - `AcceptedByKsef`: "Drukuj" (with variant picker), "Utwórz korektę",
  - `RejectedByKsef`: "Popraw i zatwierdź ponownie", "Utwórz korektę".
- **Validation surfaces**: none inline; approve/reopen/submit push to the relevant screen.
- **Scenarios**: `UID-001..UID-010` in `06`.

### 5.6.3 Create draft
- **Route**: `/invoices/new`
- **Page component**: `src/pages/InvoiceDraftCreate.tsx`
- **Form schema**: `createInvoiceRequestSchema` (zod).
- **Sections**:
  - header: kind (`DocumentKindSelect`, default `VatInvoice`), document number (`DocumentNumberPreview` — shown but not editable if numbering policy resolves automatically), external reference,
  - seller: `PartyFormSection` with prefill from tenant,
  - buyer: `PartyFormSection`, `BuyerKindSelect`, `NipInput` with mod-11,
  - dates: `IssueDatesFieldset`,
  - currency: `CurrencySelect`, default PLN,
  - commercial: `PaymentMethodSelect`, `BankAccountInput`,
  - lines: `InvoiceLineEditor` (array field, at least one line required),
  - notes: public/internal.
- **Live derived UI**: `KsefRequirementBanner` updates as buyer kind / NIP change, showing the requirement that the server will compute (`Required`, `Optional`, `Forbidden`, `NotApplicable`).
- **Totals preview**: `TotalsSummaryCard` updates live from line data.
- **Submit**: `createInvoiceDraft` → navigate to detail of the returned draft.
- **Validation surfaces**:
  - inline: zod schema errors (required fields, format),
  - banner: `ValidationMessageList` rendering server response messages; `Draft` stage warnings shown non-blocking.
- **Scenarios**: `UIC-001..UIC-009` in `06`.

### 5.6.4 Edit draft
- **Route**: `/invoices/aggregate/:id/edit`
- **Page component**: `src/pages/InvoiceDraftEdit.tsx`
- **Purpose**: same shape as `5.6.3` but prefilled from `getAggregateInvoice` and dispatching `updateInvoiceDraft`.
- **Guard**: if status is not `Draft`, redirect to detail with a toast (`INV-VAL-101` family is server-enforced but UI should not even render the form).
- **Form schema**: `updateInvoiceDraftRequestSchema`.
- **Submit**: `updateInvoiceDraft` → navigate back to detail.
- **Scenarios**: `UIE-001..UIE-006` in `06`.

### 5.6.5 Approve review
- **Route**: `/invoices/aggregate/:id/approve`
- **Page component**: `src/pages/InvoiceApproveReview.tsx`
- **Purpose**: render the draft read-only with a pre-flight validation preview, then allow final approve.
- **Queries**: `getAggregateInvoice({ tenantId, id })`.
- **Validation preview**: optional server call that runs `ApprovalValidationService` without committing (if the backend exposes a dry-run; otherwise the preview only shows what can be inferred client-side from zod and the user clicks "Zatwierdź" to get the authoritative list). For v1, preview is client-side only; server-side authoritative check happens on actual submit.
- **Actions**: "Zatwierdź" (dispatches `approveInvoice`). On validation failure, render `ValidationMessageList` grouped by rule family (`Struktura`, `Strony`, `VAT`, `Daty`, `KSeF`, `Stan`).
- **Reopen button**: only on `Approved`/`RejectedByKsef` statuses (navigates here from detail when the document is already approved and policy allows reopen).
- **Scenarios**: `UIA-001..UIA-006` in `06`.

### 5.6.6 Create correction from original
- **Route**: `/invoices/aggregate/:id/corrections/new`
- **Page component**: `src/pages/InvoiceCorrectionCreate.tsx`
- **Purpose**: produce a new draft correction invoice referencing the original shown as `:id`.
- **Original** is loaded read-only via `getAggregateInvoice`.
- **Form schema**: `createCorrectionFromOriginalRequestSchema` — collects `CorrectionReasonKind`, reason description, before/after deltas.
- **Line editor**: `InvoiceLineEditor` in correction variant — every line has both `BeforeCorrection` and `AfterCorrection` snapshots (`01:L234`, ADR rationale in `01:L444-L445`).
- **Submit**: `createCorrectionFromOriginal` → navigate to the new draft's detail.
- **Validation surfaces**: standard `ValidationMessageList` showing `INV-VAL-080..083` plus any line/VAT rules that fire.
- **Scenarios**: `UIX-001..UIX-008` in `06`.

### 5.6.7 Final invoice from advances
- **Route**: `/invoices/final-from-advances`
- **Page component**: `src/pages/InvoiceFinalFromAdvances.tsx`
- **Purpose**: pick one or more approved advance invoices for the same seller/buyer/currency and generate a final draft.
- **Query**: `listAggregateInvoices` filtered to `kind=AdvanceInvoice, status=Approved/AcceptedByKsef`.
- **Step 1**: pick buyer (autocomplete) → list matching advances.
- **Step 2**: `AdvanceAllocationPicker` — tick advances, show running settled total.
- **Step 3**: confirm final invoice header (issue date, due date).
- **Submit**: `createFinalInvoiceFromAdvances` → navigate to new draft's detail.
- **Validation surfaces**: `INV-VAL-070..073` shown via `ValidationMessageList` on server rejection.
- **Scenarios**: `UIF-001..UIF-006` in `06`.

### 5.6.8 Print variant viewer
- **Route**: `/invoices/aggregate/:id/print`
- **Page component**: `src/pages/InvoicePrintView.tsx`
- **Purpose**: render the invoice in a printable layout using the server-projected `InvoicePrintModel` with a variant picker (Standard / Duplicate / English).
- **Query**: `getInvoicePrint({ tenantId, id, variant })` — variant changes trigger a refetch; labels come from the response so English strings never live in the portal code.
- **Variant logic**:
  - `Standard` — always available,
  - `Duplicate` — available only if status ≥ `AcceptedByKsef` (per ADR-002 duplicate is a reissue of an issued document), server enforces; client disables the button with tooltip referencing `IMM-003`,
  - `English` — available in any status, labels from `PrintLabels` English instance (`01:L458-L463`).
- **Print trigger**: native `window.print()` on a print-only CSS layout (`@media print`) — no PDF library in v1.
- **Duplicate issuance**: when the user renders a duplicate, the server records `DuplicateMetadata`; the viewer shows the current issuance count and date.
- **Scenarios**: `UIP-001..UIP-008` in `06`.

### 5.6.9 KSeF submission & status polling
- **Route**: `/invoices/aggregate/:id/submit`
- **Page component**: `src/pages/InvoiceKsefSubmit.tsx`
- **Purpose**: trigger submission, display progress, surface the Accepted/Rejected outcome.
- **Mutation**: `submitInvoiceToKsef` (endpoint 7).
- **Polling**: after success of the submit mutation, the screen polls `getAggregateInvoice` with `refetchInterval: 3000ms` until `KsefSubmissionState ∈ { Accepted, Rejected }`, max 2 minutes, then falls back to manual "Odśwież".
- **On Accepted**: show KSeF identifiers, offer "Drukuj" and navigate-to-detail.
- **On Rejected**: show rejection reason (from `KsefRejectionReason`), offer "Utwórz korektę" and "Popraw i zatwierdź ponownie".
- **Validation surfaces**: `INV-VAL-090..112` via `ValidationMessageList`.
- **Scenarios**: `UIK-001..UIK-006` in `06`.

## 5.7 Shared component catalog

All components live under `src/OpenKSeF.Portal.Web/src/components/invoices/`. Each must have co-located tests (see `06`).

| Component | Props (shape) | States / variants | Accessibility |
|---|---|---|---|
| `DocumentStatusBadge` | `{ status: DocumentStatus }` | one per status, each with color + icon + Polish label from a local map | `role="status"`, `aria-label` with Polish text |
| `DocumentKindChip` | `{ kind: DocumentKind }` | one per kind, color-coded | `aria-label` Polish text |
| `SourceChip` | `{ source: 'Aggregate' \| 'Synced' }` | two variants | `aria-label` |
| `BuyerKindSelect` | `{ value, onChange }` | dropdown: `Business`, `Consumer`, `Unknown` | native `<select>`, labelled |
| `KsefRequirementBanner` | `{ requirement: KsefSubmissionRequirement }` | four variants (`Required`, `Optional`, `Forbidden`, `NotApplicable`) | `role="note"` |
| `KsefSubmissionStatus` | `{ state, identifiers?, rejectionReason? }` | five states from `01:L274-L279` | `role="status"`, live region for polling updates |
| `KsefIdentifiersCard` | `{ ksefDocumentNumber, ksefReferenceNumber }` | — | — |
| `ValidationMessageList` | `{ stage, messages }` | grouped by rule family prefix (`INV-VAL-0xx`), Errors above Warnings | `role="alert"` when Errors present, `role="status"` for Warnings only |
| `InvoiceLineEditor` | `{ value, onChange, mode: 'create' \| 'correction', pricingMode }` | standard / correction (before+after) | array add/remove buttons with keyboard shortcuts |
| `InvoiceLineTable` | `{ lines, showCorrectionColumns? }` | read-only | semantic `<table>` with caption |
| `MoneyInput` | `{ value: Money, onChange, currency, pricingMode? }` | PLN default, other currencies allowed | labelled, `inputmode="decimal"` |
| `MoneyDisplay` | `{ value: Money }` | — | screen reader reads amount + currency |
| `PercentageInput` | `{ value, onChange }` | — | `inputmode="decimal"` |
| `NipInput` | `{ value, onChange }` | runs mod-11 checksum, shows inline error | `aria-invalid`, `aria-describedby` |
| `CurrencySelect` | `{ value, onChange }` | ISO-4217 list, PLN default | — |
| `VatRateSelect` | `{ value, onChange }` | policy-aware: 23/8/5/0/zw/np | — |
| `PartyCard` | `{ party: PartyReadDto }` | read-only | — |
| `PartyFormSection` | `{ value, onChange, showNip }` | seller (prefillable) / buyer | — |
| `IssueDatesFieldset` | `{ value, onChange }` | — | — |
| `DocumentNumberPreview` | `{ policyResolved, externalReference? }` | read-only preview of next number | — |
| `TotalsSummaryCard` | `{ net, vat, gross, currency }` | — | — |
| `VatSummaryTable` | `{ breakdown }` | rows per rate/exemption | — |
| `CorrectionReferenceCard` | `{ reference: CorrectionReference }` | — | link to original |
| `AdvanceAllocationPicker` | `{ advances, selected, onChange }` | checklist with running total | — |
| `AdvanceAllocationList` | `{ allocations }` | read-only | — |
| `DuplicateIssuanceBanner` | `{ issuances: DuplicatePrintInfo[] }` | hidden when empty | — |
| `PrintVariantSwitcher` | `{ variant, onChange, disabledVariants? }` | three segmented options | `role="radiogroup"` |

## 5.8 State-machine UI mapping

Direct one-to-one mapping of `01:L318-L370`. Each transition maps to exactly one button in the UI and exactly one endpoint in `5.4.2`.

| Transition | UI entry point | Button label (PL) | Endpoint | Guard rules surfaced |
|---|---|---|---|---|
| `Draft → Approved` | Detail page, Approve review screen | "Zatwierdź" | `POST .../approve` (endpoint 5) | all `Approve` stage rules |
| `Approved → Draft` | Detail page | "Odblokuj do edycji" | `POST .../reopen` (endpoint 6) | `IApprovedEditPolicy`, `INV-VAL-102` |
| `Approved → SubmittedToKsef` | Detail page → Submit screen | "Wyślij do KSeF" | `POST .../submit-to-ksef` (endpoint 7) | `SendToKsef` stage rules |
| `SubmittedToKsef → AcceptedByKsef` | automatic via polling or callback | — (passive) | internal / callback endpoint 8 | — |
| `SubmittedToKsef → RejectedByKsef` | automatic via polling or callback | — (passive) | internal / callback endpoint 9 | — |
| `RejectedByKsef → Approved` | Detail page after fixes | "Zatwierdź ponownie" | `POST .../approve` (endpoint 5) | same as Draft → Approved |

Buttons that are not valid from the current status are not rendered (not just disabled) — except `Reopen`, which is rendered disabled with a tooltip showing `INV-VAL-102` when policy forbids it, so the user learns about the policy.

## 5.9 Validation surfacing

### 5.9.1 Mapping stages to surfaces
- `Draft` stage — soft warnings, rendered inline on form fields and as a passive banner above the form. Never blocks navigation. Examples: `INV-VAL-012`, `INV-VAL-042`, `INV-VAL-064`.
- `Approve` stage — blocking errors, rendered as a modal on approve attempt, grouped by rule family. Blocks the transition. Examples: `INV-VAL-001..083` error variants.
- `SendToKsef` stage — blocking errors, rendered on the submit screen. Examples: `INV-VAL-090..112`.
- State-transition errors (`INV-VAL-100..102`) — toast + detail-page banner.

### 5.9.2 Client vs server authority
Client mirrors a subset of rules in zod for fast feedback:
- structure/identity (`INV-VAL-001..003`),
- party presence and NIP format (`INV-VAL-010..013`),
- date presence/order (`INV-VAL-020..022`),
- line presence and total consistency (`INV-VAL-050..053`),
- currency code ISO-4217 (`INV-VAL-040`).

Server is authoritative for everything, and always for:
- numbering uniqueness (`INV-VAL-030..032`),
- VAT reconciliation (`INV-VAL-060..064`),
- advance/final settlement (`INV-VAL-070..073`),
- correction chain (`INV-VAL-080..083`),
- KSeF submission requirement (`INV-VAL-090..093`),
- state machine and immutability (`INV-VAL-100..102`),
- technical KSeF payload (`INV-VAL-110..112`).

### 5.9.3 Rendering rule codes
Every message renders its Polish text as the primary label and its `INV-VAL-###` code as a small monospace suffix. Support can cross-reference the code to `02-validation-specification.md`. The code is never the only signal shown to the user.

### 5.9.4 Rule-code registry consumption
The portal ships a small JSON lookup in `src/api/schemas/ruleCodes.ts` derived from `rule-code-mapping.md`. Its only purpose is:
- to group messages by family for `ValidationMessageList`,
- to provide dev-mode warnings if the backend returns a code the portal does not know (surfaces integration drift).

No Polish text comes from this file — text always comes from the server response.

## 5.10 Mobile (MAUI) minimal slice

Scope constraint (`5.1`): read-only + DTO rename + status/kind badges. No create/edit/approve/correction/print on device.

### 5.10.1 DTO rename
- `InvoiceDto` → `SyncedInvoiceDto` in `src/OpenKSeF.Mobile/Models/InvoiceDto.cs` (file renamed in place per CLAUDE.md — no parallel file).
- `InvoiceLineDto` → `SyncedInvoiceLineDto`.
- `CachedInvoice` → `CachedSyncedInvoice`; SQLite schema version bumped and local cache cleared on upgrade.
- `IApiService.GetInvoicesAsync` / `GetInvoiceDetailsAsync` return types updated to the new names.
- All XAML `x:DataType` bindings in `InvoiceListPage.xaml`, `InvoiceDetailsPage.xaml`, and their `.cs` code-behind updated.
- Unit tests in `OpenKSeF.Mobile.Tests` renamed accordingly.

The mobile app continues to talk to the **legacy** `InvoicesController` / `InvoicesSummaryController` for the v1 mobile slice. It does not call the new aggregate controller yet. This preserves mobile functionality while the rename lands.

### 5.10.2 New badges on list and detail
- Add `DocumentStatusBadge` control (MAUI `ContentView`) under `src/OpenKSeF.Mobile/Controls/`.
- Add `DocumentKindBadge` and `KsefStatusBadge`.
- Bind status/kind/KSeF state to the corresponding fields from `SyncedInvoiceDto` when present. For rows fetched from the legacy controller — which does not currently include these fields — the badge renders a neutral "Sync" pill. Filling the badges with real values requires the legacy controller to additionally project the aggregate status when the sync row has a matching aggregate; that is tracked in `todo_ui.md` as an optional enhancement (`E8-S4`) and is out of scope if the backend team prefers to defer it.
- Polish strings only. No English.

### 5.10.3 Non-goals for mobile in this round
- no create/edit/approve/submit,
- no print variants,
- no correction/final-from-advances flows,
- no validation-message list rendering,
- no change to Appium E2E tiers beyond smoke verification of the rename.

## 5.11 Legacy rename effects on portal

### 5.11.1 API DTO rename
- `src/OpenKSeF.Api/Models/InvoiceResponse.cs` → `SyncedInvoiceResponse.cs` (class renamed in place).
- `InvoiceLineResponse` → `SyncedInvoiceLineResponse`.
- Controllers return `SyncedInvoiceResponse` for the existing browse endpoints. Route paths are **not** changed to avoid breaking clients; only the response class name changes and JSON field names stay identical.

### 5.11.2 Portal type rename
- `src/OpenKSeF.Portal.Web/src/api/types.ts`:
  - `InvoiceResponse` → `SyncedInvoiceResponse`,
  - `InvoiceLineResponse` → `SyncedInvoiceLineResponse`.
- Every usage in `InvoiceList.tsx`, `InvoiceDetails.tsx`, `useInvoices.ts`, `pages/*.test.tsx` updated.
- React Query keys: `['invoices', ...]` becomes `['invoices', 'synced', ...]` to disambiguate from the new aggregate keys.
- No runtime behavior change is expected; tests in `06` under the `UIR-###` group (regression) assert the old and new UX are equivalent on the read surfaces.

### 5.11.3 Things **not** deleted in this iteration
- Legacy controllers,
- `SyncedInvoice` browse entity,
- existing payment toggle endpoint `PATCH /{id}/paid`,
- existing transfer details endpoint `GET /{id}/transfer`,
- existing list/detail React pages (refactored, not replaced).

## 5.12 Naming rationale for portal
Two noun spaces coexist:

- **Invoice** — anything sourced from the new aggregate. Used for noun-phrase English identifiers in code (`InvoiceDraftCreate`, `InvoiceLineEditor`, `InvoiceReadDto`) and for Polish labels ("Nowa faktura", "Zatwierdź fakturę").
- **SyncedInvoice** — anything sourced from the KSeF browse cache. Polish label stays "Faktury" from KSeF; internal code uses `Synced` prefix (`SyncedInvoiceResponse`, `SyncedInvoiceDetail`).

Polish users see "Faktury" for both — the `SourceChip` on list rows is the visual tell. Developers never see ambiguous identifiers because English code always disambiguates. Anchored in `adr-001` and `01:L299-L316`.

## 5.13 Accessibility baseline
- All status/kind badges use color **and** an icon **and** Polish text — never color alone.
- Form fields have `<label>` associations and `aria-describedby` for inline errors.
- Modals (approval validation list) trap focus and restore on close.
- `ValidationMessageList` uses `role="alert"` when any `Error` is present and `role="status"` when only warnings are shown.
- Every state-transition button has a textual label, not icon-only.
- Keyboard shortcuts for `InvoiceLineEditor`: `Enter` to add, `Delete` to remove focused line, `Tab` moves through cells.
- Print view uses high-contrast black-on-white and a dedicated `@media print` stylesheet. No color reliance.

## 5.14 Out of scope
- Internationalisation framework (i18next etc.).
- Mobile CRUD parity.
- Design system / component library adoption.
- Attachment management.
- Accounting / ledger integration.
- Bulk actions across invoices.
- Real-time multi-user editing.
- Role-based access control beyond current tenant ownership.
- Exchange-rate lookups for non-PLN invoices.
- PDF generation in-browser (v1 uses `window.print()`).
