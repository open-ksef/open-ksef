# 6. UI Test Scenarios

Format: Given / When / Then. Scenario IDs are stable and referenced from `todo_ui.md`. Each scenario traces to at least one:

- domain rule in `02-validation-specification.md` (format `INV-VAL-###`),
- domain scenario in `03-test-scenarios.md` (format `VAT-###`, `COR-###`, `ST-###`, etc.),
- section in `05-ui-specification.md` (format `05:§5.x.y`).

Scenario ID prefixes:

| Prefix | Surface |
|---|---|
| `API-###` | new `InvoicesAggregateController` contract tests |
| `UIL-###` | invoice list screen |
| `UID-###` | invoice detail screen |
| `UIC-###` | create draft form |
| `UIE-###` | edit draft form |
| `UIA-###` | approve / reopen flow |
| `UIX-###` | correction flow |
| `UIF-###` | final-from-advances flow |
| `UIK-###` | KSeF submission & status polling |
| `UIP-###` | print variant viewer |
| `UIS-###` | state badge rendering |
| `UIV-###` | validation surfacing |
| `UIR-###` | legacy rename regression |
| `MOB-###` | mobile minimal slice |

Tooling conventions:

- `API-###` run as `dotnet test` against `OpenKSeF.Api.IntegrationTests` with Testcontainers Postgres.
- `UI*-###` run as `vitest` under `src/OpenKSeF.Portal.Web`, reusing the existing JSDOM + `waitFor()` helper pattern (no Testing Library migration).
- `UIK-###` and any multi-screen flow run additionally through Playwright MCP against the running dev stack.
- `MOB-###` run through existing Appium Android smoke tier.

---

## 6.1 API controller contract

### API-001 List aggregate invoices by status filter
**Given** a tenant with one `Draft` and one `AcceptedByKsef` aggregate invoice
**When** client calls `GET .../invoices/aggregate?status=Draft`
**Then** the response contains exactly the draft invoice
**And** every row has `status`, `kind`, `ksefSubmissionState` populated
**Traces**: `05:§5.4.2#1`, `05:§5.4.3`

### API-002 List rejects unknown status filter
**Given** any tenant
**When** client calls `GET .../invoices/aggregate?status=NonExisting`
**Then** response is `400 Bad Request`
**And** error envelope has `code = INV-VAL-003` or equivalent structure rule
**Traces**: `05:§5.4.2#1`, `02` structure rules

### API-003 Get detail returns `InvoiceReadDto`
**Given** an approved aggregate invoice
**When** client calls `GET .../invoices/aggregate/{id}`
**Then** response body deserializes into `InvoiceReadDto`
**And** all enum values use canonical names from `05:§5.3`
**Traces**: `05:§5.4.2#2`

### API-004 Create draft happy path
**Given** a valid `CreateInvoiceRequest` for a B2B VAT invoice
**When** client calls `POST .../invoices/aggregate`
**Then** response is `201 Created` with `Location` header pointing to detail
**And** returned `InvoiceReadDto.status` is `Draft`
**Traces**: `05:§5.4.2#3`, `VAT-001`

### API-005 Create draft returns validation envelope on missing seller
**Given** `CreateInvoiceRequest` with no seller NIP
**When** client calls `POST .../invoices/aggregate`
**Then** response is `422 Unprocessable Entity`
**And** envelope contains `INV-VAL-010` (seller required) as `Error`
**Traces**: `05:§5.4.4`, `INV-VAL-010`

### API-006 Update draft is idempotent for no-op
**Given** an existing draft
**When** client calls `PATCH .../draft` with the same body twice
**Then** both responses are `200 OK`
**And** no domain event duplication
**Traces**: `05:§5.4.2#4`

### API-007 Approve returns envelope grouped by stage
**Given** a draft that fails `INV-VAL-060` and `INV-VAL-013`
**When** client calls `POST .../approve`
**Then** response is `422` with `stage = "Approve"`
**And** `messages[]` contains both codes with severity `Error`
**Traces**: `05:§5.4.4`, `VAT-002`, `BUY-004`

### API-008 Reopen honors `IApprovedEditPolicy`
**Given** an approved unsent invoice and policy `ApprovedEditableBeforeKsef = false`
**When** client calls `POST .../reopen`
**Then** response is `409 Conflict`
**And** envelope contains `INV-VAL-102`
**Traces**: `05:§5.8`, `ST-003`

### API-009 Submit-to-KSeF requires `Approved` status
**Given** a draft invoice
**When** client calls `POST .../submit-to-ksef`
**Then** response is `409 Conflict`
**And** envelope contains `INV-VAL-100`
**Traces**: `05:§5.4.2#7`, `ST-004`

### API-010 Create correction from original produces draft
**Given** an `AcceptedByKsef` original VAT invoice
**When** client calls `POST .../corrections` with valid reason and deltas
**Then** response is `201 Created`
**And** new invoice has `kind = CorrectionInvoice` and `status = Draft`
**And** `correctionReference.originalId` equals the original's id
**Traces**: `05:§5.4.2#10`, `COR-001`, `COR-006`

### API-011 Final-from-advances rejects mixed buyers
**Given** two advance invoices with different `BuyerSnapshot.Nip`
**When** client calls `POST .../final-from-advances` referencing both
**Then** response is `422` with `INV-VAL-073`
**Traces**: `FIN-004`

### API-012 Print endpoint returns English labels on variant=English
**Given** an accepted invoice
**When** client calls `GET .../print?variant=English`
**Then** `InvoicePrintModel.variant` is `English`
**And** `labels` payload is the English `PrintLabels` instance
**And** fiscal content is identical to `variant=Standard` output
**Traces**: `REG-003`, `05:§5.6.8`, `01:L459-L463`

### API-013 Print endpoint refuses Duplicate before acceptance
**Given** a draft invoice
**When** client calls `GET .../print?variant=Duplicate`
**Then** response is `409 Conflict` referencing `IMM-003` precondition
**Traces**: `IMM-003`, `05:§5.6.8`

### API-014 Tenant isolation
**Given** tenant A owns invoice X and tenant B is authenticated
**When** B calls `GET .../tenants/{A}/invoices/aggregate/{X}`
**Then** response is `404 Not Found` (never `403`)
**Traces**: `05:§5.4.5`

---

## 6.2 Invoice list (`UIL`)

### UIL-001 Renders merged aggregate and synced rows
**Given** one aggregate draft and two synced invoices for the current tenant
**When** user navigates to `/invoices`
**Then** the table shows three rows
**And** each row has a `SourceChip` labelled `Aggregate` or `Sync`
**Traces**: `05:§5.6.1`

### UIL-002 Filter chip narrows to Draft
**Given** mixed statuses in the list
**When** user clicks the `Draft` status chip
**Then** only draft rows remain
**And** the React Query key includes `status: ['Draft']`
**Traces**: `05:§5.5.5`, `05:§5.6.1`

### UIL-003 Kind filter supports multiple kinds
**Given** invoices of kinds `VatInvoice`, `AdvanceInvoice`, `Proforma`
**When** user selects `VatInvoice` and `Proforma`
**Then** advance rows disappear and the other two remain
**Traces**: `05:§5.6.1`

### UIL-004 Row link routes by source
**Given** one aggregate draft and one synced invoice with `ksefInvoiceNumber = FA/1/2026`
**When** user clicks the aggregate row
**Then** navigation target is `/invoices/aggregate/{id}`
**And** clicking the synced row navigates to `/invoices/FA/1/2026`
**Traces**: `05:§5.5.2`, `05:§5.6.1`

### UIL-005 Header "Nowa faktura" navigates to create draft
**Given** the list screen
**When** user clicks "Nowa faktura"
**Then** navigation target is `/invoices/new`
**Traces**: `05:§5.6.1`, `05:§5.6.3`

### UIL-006 Empty state
**Given** no invoices in the tenant
**When** user navigates to `/invoices`
**Then** the empty-state illustration and CTA "Nowa faktura" are rendered
**And** no table is rendered
**Traces**: `05:§5.6.1`

### UIL-007 List paginates at 25 rows
**Given** 60 aggregate invoices
**When** user navigates to `/invoices`
**Then** page 1 shows 25 rows
**And** pagination control shows 3 pages
**Traces**: `05:§5.4.3`

### UIL-008 Date range filter applies to issue date
**Given** invoices dated 2026-01-01 and 2026-03-01
**When** user sets range `2026-02-01 .. 2026-04-01`
**Then** only the March invoice is shown
**Traces**: `05:§5.4.3`, `05:§5.6.1`

---

## 6.3 Aggregate invoice detail (`UID`)

### UID-001 Renders all sections for an approved invoice
**Given** an approved VAT invoice with seller, buyer, lines, totals
**When** user navigates to `/invoices/aggregate/{id}`
**Then** header, parties, dates, commercial, lines, totals, KSeF banner are all rendered
**Traces**: `05:§5.6.2`

### UID-002 Draft status shows Edit and Approve buttons
**Given** a draft invoice
**When** user views detail
**Then** "Edytuj" and "Zatwierdź" buttons are present
**And** "Wyślij do KSeF" is not rendered
**Traces**: `05:§5.6.2`, `05:§5.8`

### UID-003 Approved status shows Submit and Reopen
**Given** an approved invoice
**When** user views detail
**Then** "Wyślij do KSeF" and "Odblokuj do edycji" buttons are present
**And** "Edytuj" is not rendered
**Traces**: `05:§5.6.2`, `05:§5.8`

### UID-004 Reopen button disabled when policy forbids
**Given** an approved invoice and server response carrying `reopenAllowed: false`
**When** user views detail
**Then** "Odblokuj do edycji" is rendered disabled with a tooltip showing `INV-VAL-102`
**Traces**: `05:§5.8`, `ST-003`

### UID-005 Submitted status polls every 3 seconds
**Given** an invoice with `status = SubmittedToKsef`
**When** user views detail
**Then** `getAggregateInvoice` is refetched every 3 seconds until state leaves `SubmittedToKsef`
**Traces**: `05:§5.6.9`, `05:§5.5.5`

### UID-006 Accepted status shows print button and KSeF identifiers
**Given** an accepted invoice with KSeF identifiers
**When** user views detail
**Then** `KsefIdentifiersCard` renders both numbers
**And** "Drukuj" button is present
**Traces**: `05:§5.6.2`, `ST-005`

### UID-007 Rejected status exposes "Popraw i zatwierdź ponownie"
**Given** a rejected invoice with rejection reason
**When** user views detail
**Then** rejection reason is rendered
**And** "Popraw i zatwierdź ponownie" button is present
**Traces**: `ST-006`, `05:§5.6.2`

### UID-008 Correction invoice shows correction reference
**Given** a correction invoice whose original is `FA/1/2026`
**When** user views detail
**Then** `CorrectionReferenceCard` is rendered with a link to the original
**Traces**: `05:§5.6.2`, `COR-001`

### UID-009 Final invoice shows advance allocations
**Given** a final invoice settling two advances
**When** user views detail
**Then** `AdvanceAllocationList` shows both advances with their settled amounts
**Traces**: `05:§5.6.2`, `FIN-001`

### UID-010 Duplicate banner rendered when duplicates exist
**Given** an accepted invoice that has been duplicate-printed twice
**When** user views detail
**Then** `DuplicateIssuanceBanner` shows both issuances
**And** the original content is unchanged
**Traces**: `IMM-003`, `REG-004`, `05:§5.6.2`

---

## 6.4 Create draft (`UIC`)

### UIC-001 Valid B2B VAT draft submits and navigates to detail
**Given** a filled form for a VAT invoice with B2B buyer, one line at 23% VAT, PLN
**When** user clicks "Zapisz"
**Then** `createInvoiceDraft` is called with a valid body
**And** navigation is to `/invoices/aggregate/{newId}`
**Traces**: `05:§5.6.3`, `VAT-001`

### UIC-002 Missing seller NIP is blocked by client zod
**Given** seller NIP field is empty
**When** user attempts to submit
**Then** inline error is shown under the NIP field
**And** no API call is made
**Traces**: `05:§5.9.2`, `INV-VAL-010`

### UIC-003 Invalid NIP checksum is blocked by client zod
**Given** seller NIP `1234567890`
**When** user tabs out of the field
**Then** inline error "Nieprawidłowy numer NIP" is shown
**And** submit button is disabled
**Traces**: `05:§5.5.4`, `INV-VAL-011`

### UIC-004 Buyer-kind change updates KSeF requirement banner
**Given** form is filled with buyer kind `Business` and NIP present
**When** user switches buyer kind to `Consumer`
**Then** `KsefRequirementBanner` changes from `Required` to `Optional`
**Traces**: `05:§5.6.3`, `BUY-001`, `BUY-002`

### UIC-005 Adding a line updates totals preview
**Given** a draft with one line net 100 PLN at 23% VAT
**When** user adds a second line net 50 PLN at 23% VAT
**Then** `TotalsSummaryCard` shows net 150, VAT 34.50, gross 184.50
**Traces**: `05:§5.6.3`, `05:§5.7`

### UIC-006 Server returns `INV-VAL-060` on unknown VAT rate
**Given** a line with VAT rate left empty and no exemption
**When** user submits
**Then** server returns envelope with `INV-VAL-060`
**And** `ValidationMessageList` renders the Polish message and rule code
**Traces**: `VAT-002`, `05:§5.9.3`

### UIC-007 Draft warnings render non-blocking banner
**Given** a draft missing buyer classification
**When** user submits
**Then** the draft is created
**And** a warning banner with `INV-VAL-012` is shown on the next screen
**Traces**: `BUY-003`, `05:§5.9.1`

### UIC-008 Proforma kind hides KSeF submission requirement
**Given** user selects `Proforma` as kind
**When** form rerenders
**Then** `KsefRequirementBanner` shows `NotApplicable`
**And** the "Wyślij do KSeF" future action is not offered on the success screen
**Traces**: `PRO-002`, `05:§5.6.3`

### UIC-009 Line editor requires at least one line
**Given** form with zero lines
**When** user attempts to submit
**Then** inline error "Dodaj przynajmniej jedną pozycję" is shown
**Traces**: `05:§5.6.3`, `INV-VAL-050`

---

## 6.5 Edit draft (`UIE`)

### UIE-001 Edit screen prefills from server
**Given** a draft invoice with seller, buyer, two lines
**When** user opens `/invoices/aggregate/{id}/edit`
**Then** every field is prefilled with current values
**Traces**: `05:§5.6.4`

### UIE-002 Non-draft invoice redirects to detail
**Given** an `Approved` invoice
**When** user opens the edit URL directly
**Then** the app redirects to `/invoices/aggregate/{id}`
**And** shows a toast "Faktura nie jest w stanie roboczym"
**Traces**: `05:§5.6.4`, `INV-VAL-101`

### UIE-003 Patch request contains only changed fields
**Given** user edits only `publicNotes`
**When** user saves
**Then** the PATCH body contains `publicNotes`
**And** does not resend seller/buyer snapshots unchanged
**Traces**: `05:§5.4.2#4`

### UIE-004 Server rejects edit with state transition error
**Given** concurrent approval happened between prefill and save
**When** user saves
**Then** server responds `409` with `INV-VAL-101`
**And** the UI shows a modal "Faktura została w międzyczasie zatwierdzona"
**Traces**: `IMM-001`, `05:§5.9.1`

### UIE-005 Cancel navigates back to detail without API call
**Given** user has modified two fields
**When** user clicks "Anuluj"
**Then** no PATCH is issued
**And** navigation is to detail
**Traces**: `05:§5.6.4`

### UIE-006 Line reorder persists `lineNumber`
**Given** three lines in order A, B, C
**When** user moves C before A
**Then** saved payload has `lineNumber` 1=C, 2=A, 3=B
**Traces**: `05:§5.7`

---

## 6.6 Approve & reopen (`UIA`)

### UIA-001 Approve happy path
**Given** a valid draft
**When** user clicks "Zatwierdź"
**Then** `approveInvoice` is called
**And** navigation is to detail
**And** status badge shows `Approved`
**Traces**: `ST-001`, `05:§5.6.5`

### UIA-002 Approve blocked by `INV-VAL-063`
**Given** a draft with VAT summary mismatch
**When** user clicks "Zatwierdź"
**Then** the approve review screen shows `ValidationMessageList` with `INV-VAL-063`
**And** the status remains `Draft`
**Traces**: `VAT-005`, `05:§5.6.5`

### UIA-003 Reopen happy path
**Given** an approved unsent invoice with policy allowing reopen
**When** user clicks "Odblokuj do edycji"
**Then** status returns to `Draft`
**Traces**: `ST-002`, `05:§5.8`

### UIA-004 Reopen blocked by `INV-VAL-102`
**Given** an approved unsent invoice with `ApprovedEditableBeforeKsef = false`
**When** user clicks "Odblokuj do edycji"
**Then** a modal shows message text and rule code `INV-VAL-102`
**Traces**: `ST-003`, `05:§5.9.1`

### UIA-005 Grouping of validation errors by family
**Given** a draft failing `INV-VAL-013`, `INV-VAL-063`, `INV-VAL-102`
**When** user attempts to approve
**Then** `ValidationMessageList` renders three groups in order: `Strony`, `VAT`, `Stan`
**Traces**: `05:§5.9.1`

### UIA-006 Retry after fix
**Given** the user received `INV-VAL-013` blocking approve
**And** they corrected the buyer NIP and resubmitted
**When** `approveInvoice` succeeds
**Then** navigation is to detail and status badge is `Approved`
**Traces**: `ST-001`, `05:§5.6.5`

---

## 6.7 Correction flow (`UIX`)

### UIX-001 Correction screen loads original read-only
**Given** an accepted VAT invoice
**When** user navigates to `/invoices/aggregate/{id}/corrections/new`
**Then** a read-only card shows the original's document number, buyer, totals
**Traces**: `05:§5.6.6`, `COR-006`

### UIX-002 Correction without reason blocked by `INV-VAL-081`
**Given** form is submitted with empty reason description
**When** user submits
**Then** server envelope contains `INV-VAL-081`
**Traces**: `COR-003`

### UIX-003 Correction without original is impossible from this screen
**Given** any entry point
**When** component mounts
**Then** the original id is always passed in the route and the form cannot be submitted without it
**Traces**: `COR-002`, `INV-VAL-080`

### UIX-004 Before/after line editor variant
**Given** correction with two lines
**When** user edits the `AfterCorrection` column for line 1
**Then** `BeforeCorrection` snapshot is untouched
**And** saved payload includes both snapshots per line
**Traces**: `01:L234`, `01:L444-L445`, `05:§5.7`

### UIX-005 Correction with identical values blocked by `INV-VAL-082`
**Given** before and after values are identical
**When** user submits
**Then** server envelope contains `INV-VAL-082`
**Traces**: `COR-004`

### UIX-006 Correction of proforma blocked by `INV-VAL-083`
**Given** the original is a proforma
**When** UI computes allowed actions on the proforma's detail
**Then** "Utwórz korektę" button is not rendered
**And** direct navigation to the correction route results in server `422` with `INV-VAL-083`
**Traces**: `COR-005`

### UIX-007 Correction reason kind select exposes all kinds
**Given** the reason select
**When** user opens it
**Then** options match `CorrectionReasonKind`: Formal, ValueChange, QuantityChange, VatChange, BuyerDataChange, Other
**Traces**: `01:L280-L286`

### UIX-008 Successful correction navigates to new draft detail
**Given** a valid correction form
**When** user submits
**Then** navigation is to `/invoices/aggregate/{newCorrectionId}`
**And** the new detail shows `CorrectionReferenceCard` pointing to the original
**Traces**: `05:§5.6.2`, `COR-001`

---

## 6.8 Final-from-advances (`UIF`)

### UIF-001 Buyer picker lists only buyers with approved advances
**Given** buyer A has two advance invoices, buyer B has none
**When** user opens `/invoices/final-from-advances`
**Then** only buyer A is selectable
**Traces**: `05:§5.6.7`

### UIF-002 Advance picker shows running total
**Given** buyer A with advances 300, 500, 200 PLN
**When** user selects the first two
**Then** running total shows 800 PLN
**Traces**: `05:§5.6.7`

### UIF-003 Final without advances blocked by `INV-VAL-071`
**Given** user skips the picker and attempts to submit
**When** user clicks "Utwórz finalną"
**Then** zod blocks submission with an inline error
**And** no API call is made
**Traces**: `FIN-002`, `05:§5.9.2`

### UIF-004 Overflow blocked by `INV-VAL-072`
**Given** final invoice net total 1000 and selected advances totalling 1200
**When** user submits
**Then** server envelope contains `INV-VAL-072`
**Traces**: `FIN-003`

### UIF-005 Mixed buyers blocked at picker
**Given** the UI already filters by buyer
**When** user tries to cross-select via URL injection
**Then** server envelope contains `INV-VAL-073`
**Traces**: `FIN-004`

### UIF-006 Success navigates to final draft detail
**Given** a valid picker state
**When** user submits
**Then** navigation is to the new final invoice's detail
**And** `AdvanceAllocationList` shows both source advances
**Traces**: `FIN-001`, `05:§5.6.2`

---

## 6.9 KSeF submit and status polling (`UIK`)

### UIK-001 Submit triggers transition to Submitted
**Given** an approved invoice
**When** user clicks "Wyślij do KSeF"
**Then** `submitInvoiceToKsef` is called
**And** status transitions to `SubmittedToKsef`
**Traces**: `05:§5.6.9`, `01:L338-L342`

### UIK-002 Polling terminates on Accepted
**Given** a submitted invoice
**When** the server eventually reports `KsefSubmissionState = Accepted`
**Then** polling stops
**And** screen renders KSeF identifiers and the "Drukuj" button
**Traces**: `ST-005`

### UIK-003 Polling terminates on Rejected
**Given** a submitted invoice
**When** the server reports `KsefSubmissionState = Rejected`
**Then** polling stops
**And** rejection reason is shown
**And** "Utwórz korektę" button is offered
**Traces**: `ST-006`

### UIK-004 Submit blocked by missing credentials
**Given** tenant has no KSeF credentials
**When** user clicks "Wyślij do KSeF"
**Then** envelope contains `INV-VAL-092`
**Traces**: `KTF-003`

### UIK-005 Schema validation failure shown with rule code
**Given** payload mapping rejected by technical validator
**When** user clicks "Wyślij do KSeF"
**Then** envelope contains `INV-VAL-111`
**And** `ValidationMessageList` renders the message with its code
**Traces**: `KTF-002`, `05:§5.9.3`

### UIK-006 Re-send accepted document blocked
**Given** an already accepted invoice
**When** user attempts to submit again as the same original
**Then** envelope contains `INV-VAL-093`
**Traces**: `IMM-002`

---

## 6.10 Print variants (`UIP`)

### UIP-001 Standard variant renders by default
**Given** user navigates to `/invoices/aggregate/{id}/print`
**When** page loads
**Then** `getInvoicePrint` is called with `variant=Standard`
**Traces**: `05:§5.6.8`

### UIP-002 Switching to English triggers refetch
**Given** standard print rendered
**When** user clicks the `English` segment
**Then** `getInvoicePrint` is called with `variant=English`
**And** labels switch to English
**And** amounts and fiscal fields are unchanged
**Traces**: `REG-003`, `01:L459-L463`

### UIP-003 English labels come from backend
**Given** the English variant
**When** the portal renders labels
**Then** the label strings match the `PrintLabels` English instance returned by the server
**And** no English literals exist in portal source code
**Traces**: `UI-ASSUMPTION-004`, `05:§5.6.8`

### UIP-004 Duplicate variant disabled before acceptance
**Given** a draft invoice
**When** user visits print
**Then** the `Duplicate` segment is rendered disabled
**And** tooltip references `IMM-003` precondition
**Traces**: `IMM-003`

### UIP-005 Duplicate variant records metadata
**Given** an accepted invoice
**When** user selects the `Duplicate` variant and prints
**Then** `DuplicateMetadata` count increases by one
**And** the detail page's `DuplicateIssuanceBanner` now shows the new entry
**Traces**: `REG-004`, `IMM-003`

### UIP-006 Duplicate does not create a new aggregate
**Given** an accepted invoice with id X
**When** user prints two duplicates
**Then** the list page still shows only one row for id X
**And** no new aggregate id is created
**Traces**: `REG-004`, `01:L449-L457`

### UIP-007 Print layout has print-only media
**Given** the print view
**When** user triggers `window.print()`
**Then** only the print layout is visible (header nav, sidebar hidden)
**Traces**: `05:§5.6.8`

### UIP-008 Print view fiscal content matches detail
**Given** the same accepted invoice
**When** user views detail and standard print side by side
**Then** document number, totals, VAT summary, buyer, seller are identical
**Traces**: `REG-003`

---

## 6.11 State badges (`UIS`)

### UIS-001 Draft badge
**Given** `status = Draft`
**When** `DocumentStatusBadge` renders
**Then** label is "Robocza", color class `--status-draft`, icon present
**Traces**: `05:§5.7`, `05:§5.13`

### UIS-002 Approved badge
**Given** `status = Approved`
**Then** label is "Zatwierdzona"
**Traces**: `05:§5.7`

### UIS-003 Submitted badge
**Given** `status = SubmittedToKsef`
**Then** label is "Wysłana do KSeF"
**Traces**: `05:§5.7`

### UIS-004 Accepted badge
**Given** `status = AcceptedByKsef`
**Then** label is "Zaakceptowana przez KSeF"
**Traces**: `05:§5.7`

### UIS-005 Rejected badge
**Given** `status = RejectedByKsef`
**Then** label is "Odrzucona przez KSeF"
**Traces**: `05:§5.7`

### UIS-006 Kind chip renders all five kinds
**Given** `DocumentKindChip` is rendered for each of `VatInvoice`, `AdvanceInvoice`, `FinalInvoice`, `Proforma`, `CorrectionInvoice`
**Then** each has a distinct color and Polish label
**Traces**: `01:L148-L153`, `05:§5.7`

### UIS-007 KSeF submission state badge covers all states
**Given** `KsefSubmissionStatus` is rendered for each of `NotPlanned`, `Ready`, `Submitted`, `Accepted`, `Rejected`
**Then** each renders a distinct visual
**Traces**: `01:L274-L279`

---

## 6.12 Validation surfacing (`UIV`)

### UIV-001 Error messages group by rule family
**Given** envelope carrying codes `INV-VAL-001`, `INV-VAL-010`, `INV-VAL-020`, `INV-VAL-060`
**When** `ValidationMessageList` renders
**Then** four groups are shown in order: Struktura, Strony, Daty, VAT
**Traces**: `05:§5.9.1`, `rule-code-mapping.md`

### UIV-002 Warnings appear below errors
**Given** envelope with one error and two warnings
**When** list renders
**Then** error group appears first, warnings below
**And** aria-role is `alert` (errors present)
**Traces**: `05:§5.9.1`, `05:§5.13`

### UIV-003 Warnings-only list uses status role
**Given** envelope with two warnings and zero errors
**When** list renders
**Then** aria-role is `status`
**Traces**: `05:§5.13`

### UIV-004 Polish message sourced from envelope
**Given** server returns Polish message for `INV-VAL-060`
**When** list renders
**Then** the visible text equals the server-provided Polish string verbatim
**Traces**: `05:§5.9.3`

### UIV-005 Rule code always visible
**Given** any rendered message
**When** user inspects it
**Then** the rule code is visible in monospace to the right of the text
**Traces**: `05:§5.9.3`

### UIV-006 Unknown rule code shows dev-mode warning
**Given** backend returns `INV-VAL-999` not present in the client registry
**When** list renders
**Then** the message still renders its Polish text
**And** a console warning is logged in development mode
**Traces**: `05:§5.9.4`

### UIV-007 Draft warnings do not block navigation
**Given** an invoice saved with `INV-VAL-064` warning
**When** user navigates away
**Then** navigation succeeds without confirmation prompt
**Traces**: `VAT-006`, `05:§5.9.1`

### UIV-008 Approve modal traps focus
**Given** approval attempt blocked by errors
**When** modal opens
**Then** focus moves into the modal and cannot leave until closed
**Traces**: `05:§5.13`

---

## 6.13 Legacy rename regression (`UIR`)

### UIR-001 Existing list page still renders synced invoices
**Given** a synced invoice in the legacy controller
**When** user views `/invoices` after the rename
**Then** the synced row still renders with the same fields as before
**Traces**: `05:§5.11`

### UIR-002 Synced detail page reached via unchanged URL
**Given** a synced invoice with `ksefInvoiceNumber = FA/1/2026`
**When** user navigates to `/invoices/FA/1/2026`
**Then** detail page renders
**Traces**: `05:§5.11`, `05:§5.5.2`

### UIR-003 No references to `InvoiceResponse` remain in portal source
**Given** the rename is complete
**When** a lint/grep test scans `src/OpenKSeF.Portal.Web/src`
**Then** zero references to the legacy type name `InvoiceResponse` remain
**Traces**: `05:§5.11.2`

### UIR-004 Query keys disambiguate synced and aggregate lists
**Given** a user views `/invoices`
**When** both queries are issued
**Then** `['invoices', 'synced', 'list', ...]` and `['invoices', 'aggregate', 'list', ...]` are cached independently
**Traces**: `05:§5.5.5`

### UIR-005 Payment toggle endpoint still works
**Given** a synced invoice
**When** user marks it as paid
**Then** `PATCH /tenants/{t}/invoices/{id}/paid` is called and the row updates
**Traces**: `05:§5.11.3`

---

## 6.14 Mobile minimal slice (`MOB`)

### MOB-001 List page deserializes renamed DTO
**Given** backend returns `SyncedInvoiceResponse`-shaped JSON
**When** mobile app opens the invoice list
**Then** `SyncedInvoiceDto` deserializes successfully
**And** rows render as before
**Traces**: `05:§5.10.1`

### MOB-002 Detail page deserializes renamed DTO
**Given** backend returns detail JSON
**When** mobile app opens detail
**Then** `SyncedInvoiceDto` detail fields render
**And** transfer copy action still works
**Traces**: `05:§5.10.1`

### MOB-003 SQLite cache migrated on first run
**Given** a user upgrading from an older build with the legacy cache table name
**When** the app starts after upgrade
**Then** the SQLite schema version is bumped
**And** the legacy cache is cleared
**And** the next list fetch populates `CachedSyncedInvoice`
**Traces**: `05:§5.10.1`

### MOB-004 `DocumentStatusBadge` renders on list rows
**Given** a row whose DTO carries `status`
**When** the list renders
**Then** `DocumentStatusBadge` is shown with Polish label
**Traces**: `05:§5.10.2`

### MOB-005 `DocumentKindBadge` renders on list rows
**Given** a row whose DTO carries `kind`
**Then** `DocumentKindBadge` is shown with Polish label
**Traces**: `05:§5.10.2`

### MOB-006 Rows without status show neutral sync pill
**Given** a legacy row without `status`
**When** list renders
**Then** a neutral "Sync" pill is rendered in place of the status badge
**Traces**: `05:§5.10.2`

### MOB-007 Unit tests updated for new type names
**Given** the rename landed
**When** `dotnet test OpenKSeF.Mobile.Tests` runs
**Then** all tests pass
**And** no references to `InvoiceDto` remain in `OpenKSeF.Mobile.Tests`
**Traces**: `05:§5.10.1`
