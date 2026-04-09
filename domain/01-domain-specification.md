# 1. Domain Specification — Invoice Issuance & Validation

## 1.0 Refactor context
This specification is written for a refactor, not for a greenfield module.

Current OpenKSeF naming is legacy/read-model-oriented:
- `InvoiceHeader` / `InvoiceLine` represent synchronized invoice storage,
- they are not the intended business aggregate for invoice issuance,
- their naming should not force the new domain model to avoid the word `Invoice`.

Target naming after refactor:
- domain aggregate root: `Invoice`
- domain line entity: `InvoiceLine`
- synchronized/read-side model: `SyncedInvoice`
- synchronized/read-side line model: `SyncedInvoiceLine`

PostgreSQL implication:
- this refactor may require staged table/column renames,
- if existing tables are named around `InvoiceHeader` / `InvoiceLine`, migration should move them toward `SyncedInvoice` / `SyncedInvoiceLine` semantics,
- application rollout may temporarily need compatibility mappings, views, or phased EF migrations so the rename does not break running environments.

## 1.1 Scope
This bounded domain covers:
- creation of sales-side fiscal and non-fiscal invoice documents,
- validation of correctness before approval and/or KSeF submission,
- preparation of structured payload for KSeF integration,
- lifecycle management from draft to immutable post-KSeF state.

Out of scope in v1 unless required by invoice correctness:
- full product catalog management,
- full customer master management,
- accounting/tax ledgers,
- warehouse flows,
- payment reconciliation beyond invoice-level facts required by document type.

## 1.2 ASSUMPTIONs
- `ASSUMPTION-001`: v1 concerns issuer-side documents, not purchase invoice sync.
- `ASSUMPTION-002`: legal defaults are Polish invoice rules as generally expected by KSeF-enabled invoicing, but implementation must isolate jurisdiction-specific policy.
- `ASSUMPTION-003`: document issue/edit workflow is user-driven; approval is an explicit business action.
- `ASSUMPTION-004`: KSeF submission requirement is derived from buyer classification and issuer policy, not from a UI checkbox alone.
- `ASSUMPTION-005`: duplicate is not a separate fiscal aggregate; it is a print/reissue operation over an existing approved/sent document with duplicate metadata.

## 1.3 Ubiquitous language
- **Invoice** — aggregate root representing a business invoice document in the issuing domain.
- **Fiscal Document** — document with fiscal/legal effect, e.g. VAT invoice, advance invoice, final invoice, correction.
- **Non-Fiscal Document** — document without fiscal effect, e.g. proforma.
- **Draft** — editable working version with soft validations.
- **Approved** — business-approved version ready for issuance / dispatch according to rules.
- **Issued** — business moment when the document becomes the effective issued document. In KSeF-required flows this is bound to successful KSeF submission.
- **Submitted to KSeF** — technical act of sending structured payload.
- **Accepted by KSeF** — successful submission with KSeF identifiers assigned.
- **Immutable** — no content edits allowed; only derived operations such as print/duplicate/correction.
- **Correction** — fiscal adjustment document referencing an original document.
- **Advance Invoice** — document recording received advance/prepayment.
- **Final Invoice** — settlement document consuming earlier advance invoices.
- **Buyer Classification** — B2B / B2C / unknown, plus NIP presence and legal consequences.
- **Validation Stage** — `Draft`, `Approve`, `SendToKsef`.
- **Hard Rule** — compliance/legal or system consistency rule that blocks progression.
- **Soft Rule** — warning/suggestion that does not block draft editing.
- **Policy** — configurable business rule, e.g. numbering pattern, editable-approved behavior, optional warnings.
- **KSeF Payload** — technical structured representation mapped from domain document.
- **Print View** — presentation-only rendering, e.g. English invoice print.
- **Synced Invoice** — synchronized/read-side invoice record imported from integration flow and not treated as the issuing aggregate root.

## 1.4 Bounded contexts

### A. Invoice Domain
Owns:
- business meaning of document types,
- document lifecycle,
- line totals, VAT summaries, correction/advance/final rules,
- decision whether KSeF is required,
- domain validation.

### B. Invoice Application
Owns:
- commands/use cases,
- orchestration of validation,
- approval flow,
- submission requests,
- repositories and transaction boundaries,
- authorization and UI/API-facing DTOs.

### C. KSeF Integration
Owns:
- mapping domain document to `open-ksef` library payload/schema,
- technical validation against transport/schema requirements,
- credential/session handling,
- sending and receiving KSeF identifiers/status.

### D. Presentation
Owns:
- print layouts,
- Polish/English rendering,
- duplicate print watermark/metadata,
- document visualization.

## 1.5 Core architectural split

### Domain
Contains:
- aggregates,
- entities,
- value objects,
- domain services,
- policies abstractions,
- rule primitives,
- domain events.

Must not contain:
- EF, API clients, KSeF SDK calls, HTTP, persistence concerns.

### Application
Contains:
- command handlers,
- workflow orchestration,
- validation pipeline composition,
- transaction management,
- permissions.

### Infrastructure
Contains:
- EF repositories,
- policy provider implementations,
- numbering implementations,
- open-ksef adapter,
- clock/id providers,
- configuration readers.

### Contracts
Contains:
- request/response DTOs,
- integration contracts,
- public API schemas.

## 1.6 Aggregate and entity model

## 1.6.1 Aggregate root: `Invoice`
Represents one business document.

### Identity
- `InvoiceId`
- `TenantId`
- `DocumentNumber` (nullable before assignment when numbering policy allows delayed numbering)
- `ExternalReference` (optional)

### Core classification
- `DocumentKind`
  - `VatInvoice`
  - `AdvanceInvoice`
  - `FinalInvoice`
  - `Proforma`
  - `CorrectionInvoice`
- `DocumentStatus`
  - `Draft`
  - `Approved`
  - `SubmittedToKsef`
  - `AcceptedByKsef`
  - `RejectedByKsef`
  - `Cancelled` only if business decides to support internal cancellation before issue; otherwise omit in implementation
- `BuyerKind`
  - `Business`
  - `Consumer`
  - `Unknown`
- `IssuanceMode`
  - `LocalOnly`
  - `RequiresKsef`
  - `OptionalKsef`

### Parties
- `SellerSnapshot`
- `BuyerSnapshot`
- `AdditionalPartySnapshot[]` only when required by invoice correctness or legal annotations

### Dates
- `IssueDate`
- `SaleDate` or `SalePeriod`
- `DueDate`
- `ApprovedAt`
- `SubmittedToKsefAt`
- `AcceptedByKsefAt`
- `DuplicateIssuedAt[]` as print metadata trail

### Money and currency
- `DocumentCurrency` (`PLN` in v1 default, future-ready VO)
- `ExchangeRate` (optional, not used in PLN-only flows, reserved for future)
- `Totals`
  - `NetTotal`
  - `VatTotal`
  - `GrossTotal`
- `VatBreakdown[]`

### Commercial data
- `PaymentMethod`
- `BankAccount` (seller-side if printed/required)
- `PublicNotes`
- `InternalNotes` (non-fiscal, not mapped to KSeF unless explicitly supported)
- `LineItems`
- `Flags`
  - `SplitPaymentRequired`
  - `TpMarker`
  - `WdtMarker`
  - `WntMarker`
  - `VatExemptionApplied`

### KSeF state
- `KsefSubmissionRequirement`
- `KsefSubmissionState`
- `KsefReferenceNumber` (technical submission reference)
- `KsefDocumentNumber` (assigned by KSeF after success)
- `KsefCanonicalHash` or payload fingerprint

### Relations
- `OriginalDocumentId` — for correction and duplicate derivation/view
- `AdvanceDocumentIds[]` — for final invoice settlement
- `SettledAdvanceAllocations[]`

## 1.6.2 Entity: `InvoiceLine`
Fields:
- `LineId`
- `LineNumber`
- `Description`
- `Quantity`
- `UnitOfMeasure`
- `UnitPrice`
- `PricingMode` (`Net` | `Gross`)
- `Discount`
- `VatRate`
- `VatExemptionReason` (optional)
- `VatClassificationMarker` (GTU or equivalent marker if required)
- `NetAmount`
- `VatAmount`
- `GrossAmount`
- `CorrectionRole` (`BeforeCorrection`, `AfterCorrection`, `Normal`) for correction representation

Behavior:
- recalculate monetary values based on pricing mode and VAT,
- expose normalized monetary totals,
- reject negative/invalid combinations except when correction semantics explicitly allow deltas.

## 1.6.3 Value objects
- `DocumentNumber`
- `Money`
- `CurrencyCode`
- `Percentage`
- `VatRate`
- `VatClassification`
- `TaxExemptionReason`
- `Nip`
- `PartyName`
- `PostalAddress`
- `BankAccountNumber`
- `IssueDates`
- `KsefIdentifiers`
- `ApprovalState`
- `DocumentTotals`
- `VatSummary`
- `AdvanceAllocation`
- `CorrectionReference`
- `DuplicateMetadata`

## 1.6.4 Enums / classifications
- `DocumentKind`
- `DocumentStatus`
- `BuyerKind`
- `ValidationSeverity`
- `ValidationStage`
- `PricingMode`
- `KsefSubmissionRequirement`
  - `Required`
  - `Optional`
  - `Forbidden`
  - `NotApplicable`
- `KsefSubmissionState`
  - `NotPlanned`
  - `Ready`
  - `Submitted`
  - `Accepted`
  - `Rejected`
- `CorrectionReasonKind`
  - `Formal`
  - `ValueChange`
  - `QuantityChange`
  - `VatChange`
  - `BuyerDataChange`
  - `Other`

## 1.6.5 Policies / domain services
- `INumberingPolicy`
- `IDocumentUniquenessPolicy`
- `IBuyerClassificationPolicy`
- `IKsefRequirementPolicy`
- `IVatPolicy`
- `ICorrectionPolicy`
- `IAdvanceSettlementPolicy`
- `IApprovedEditPolicy`
- `IClock`

## 1.7 Naming strategy for OpenKSeF integration
The word `Invoice` should belong to the new domain model:
- domain aggregate = `Invoice`
- synchronized/read-side persistence projection = `SyncedInvoice` / `SyncedInvoiceLine`
- mapping layer object = `KsefInvoicePayload`
- translation service = `InvoiceToKsefPayloadMapper`

Mapping examples:
- `Invoice.DocumentNumber` -> `SyncedInvoice.InvoiceNumber` or target KSeF field
- `Invoice.KsefDocumentNumber` -> `SyncedInvoice.KSeFInvoiceNumber`
- `Invoice.KsefReferenceNumber` -> `SyncedInvoice.KSeFReferenceNumber`
- `Invoice.LineItems` -> `SyncedInvoiceLine[]`

Migration advice:
- keep the mapping boundary explicit during the rename,
- if PostgreSQL table names currently mirror `InvoiceHeader` / `InvoiceLine`, rename them toward `synced_invoices` / `synced_invoice_lines` or equivalent,
- do not leave the sync/read schema with names that imply it is the source of truth for invoice issuing behavior.

## 1.8 Lifecycle and state machine

## 1.8.1 States
- `Draft`
- `Approved`
- `SubmittedToKsef`
- `AcceptedByKsef`
- `RejectedByKsef`

## 1.8.2 State transitions

### Draft -> Approved
Allowed when:
- hard validation at `Approve` passes,
- required numbering resolved if numbering-on-approval policy is enabled,
- document type-specific invariants pass.

### Approved -> Draft
Allowed only if configurable `ApprovedEditableBeforeKsef` policy permits and document was not sent to KSeF.
This supports the stated requirement that approved but unsent B2C/private-person document may remain editable by policy.

### Approved -> SubmittedToKsef
Allowed when:
- `KsefSubmissionRequirement` is `Required` or `Optional` and caller requested send,
- hard validation at `SendToKsef` passes,
- technical integration prechecks pass.

### SubmittedToKsef -> AcceptedByKsef
Allowed on successful KSeF response.
Effects:
- assign KSeF identifiers,
- mark aggregate immutable,
- emit domain event `InvoiceAcceptedByKsef`.

### SubmittedToKsef -> RejectedByKsef
Allowed on negative KSeF response.
Effects:
- store rejection reason/result,
- remain non-immutable,
- allow correction/edit according to policy.

### RejectedByKsef -> Approved
Allowed after fixes and re-approval if needed by policy.

## 1.8.3 Immutability rules
### Hard law / compliance
- After successful KSeF acceptance, fiscal content is immutable.
- Changes require a correction document, not editing in place.

### Configurable policy
- Whether `Approved` but unsent documents are editable.
- Whether returning to `Draft` requires explicit unlock action.
- Whether document number remains fixed after unlock.

## 1.9 Invariants

## 1.9.1 Cross-document invariants
- Correction must reference exactly one original fiscal document.
- Final invoice must reference at least one advance invoice.
- Sum of settled advances on final invoice cannot exceed final gross amount unless explicit correction semantics allow it.
- Duplicate refers to an already issued/approved original and does not create a new fiscal document identity.

## 1.9.2 Aggregate invariants
- A fiscal document must have seller identity.
- A fiscal document must have at least one line unless document kind has explicit zero-line support; v1: no zero-line support.
- Totals equal sum of lines and VAT summaries.
- Gross = net + VAT in document currency.
- Currency code must be set.
- For PLN-first v1, default currency is `PLN`; non-PLN allowed only if feature/policy permits.
- `Proforma` is never KSeF-submittable.
- `AcceptedByKsef` implies presence of KSeF identifiers and immutability.
- `CorrectionInvoice` must contain correction reason and original reference.
- `FinalInvoice` must contain advance settlement section.

## 1.9.3 Party invariants
- Seller NIP required for fiscal documents in Polish default policy.
- Buyer NIP required for B2B where KSeF submission is mandatory.
- Buyer without NIP defaults toward B2C semantics unless policy overrides with explicit classification.

## 1.10 Document types

## 1.10.1 VAT Invoice
Fiscal sales document.
Required domain features:
- seller and buyer snapshot,
- line items,
- VAT breakdown,
- issue date,
- document number,
- totals.

KSeF:
- required for B2B with NIP under stated assumption/policy.
- optional or not applicable for B2C depending issuer rules.

## 1.10.2 Advance Invoice
Represents receipt/requested recognition of prepayment.
Rules:
- must identify advance amount,
- may cover full or partial future sale,
- can be sent to KSeF if fiscal and required by policy,
- later final invoice must account for prior advance invoices.

## 1.10.3 Final Invoice
Settlement document following advances.
Rules:
- references one or more prior advance invoices,
- shows settled advances,
- remaining amount due after deduction must be consistent,
- cannot settle unrelated seller/buyer/currency combinations.

## 1.10.4 Proforma
Non-fiscal document.
Rules:
- cannot be approved as fiscal issuance if business distinguishes approval from commercial acceptance; in this design it may be `Approved` for commercial use but never becomes fiscal issued document,
- cannot be sent to KSeF,
- can share presentation fields with VAT invoice,
- numbering may be separate and configurable.

## 1.10.5 Correction Invoice
Fiscal correcting document.
Rules:
- must reference original document,
- must store correction reason,
- may represent delta or before/after values; recommend storing explicit before/after semantics for traceability,
- cannot correct proforma because proforma is non-fiscal; instead issue new proforma.

Reference from ProFak:
- ProFak duplicates line set into "before correction" and editable "after correction" representation. That is a strong practical pattern and worth retaining in domain semantics, even if internal model differs.

## 1.10.6 Duplicate
Not a separate aggregate in v1.
Decision:
- treat duplicate as a presentation/reissue action over an existing issued fiscal document,
- maintain `DuplicateMetadata` trail,
- render duplicate print with duplicate date and source document reference.

Trade-off:
- simpler domain, less legal confusion,
- if future regulations/process require duplicate persistence as first-class event, model it as `DocumentPrintEvent`, not a new fiscal document aggregate.

## 1.10.7 English print
Decision:
- presentation-only print variant,
- no new document kind,
- no change to fiscal payload or domain identity.

## 1.11 VAT rules

## 1.11.1 Hard law / compliance baseline
- Each line must resolve to a VAT treatment: explicit rate or exemption basis.
- VAT summary must reconcile with line-level amounts.
- Exemption cannot coexist with positive VAT amount on the same line.
- Correction must preserve auditable relation to original VAT result.

## 1.11.2 Configurable policy
- allowed rate set beyond default Polish list,
- which markers/annotations are enforced as warning vs error,
- rounding strategy within legally acceptable boundaries if multiple approaches exist.

## 1.11.3 Default Polish VAT capability set
Support as domain vocabulary, not hardcoded magic strings:
- standard percentage rates (e.g. 23, 8, 5, 0),
- exempt/non-taxable categories via `TaxExemptionReason`,
- markers/annotations such as split payment, TP, WDT/WNT, GTU if required by broader policy.

Implementation advice:
- represent VAT rate as VO with either `Percentage` or `ExemptionCode`, not as free string.
- current OpenKSeF `InvoiceLine.VatRate : string?` is insufficient for domain correctness.

## 1.11.4 VAT summaries
For each distinct VAT treatment produce:
- taxable base,
- VAT amount,
- gross subtotal,
- optional legal marker / exemption reason.

Invariant:
- document totals are derived from summaries, not independently editable in fiscal mode.

## 1.12 Advance and final invoice rules

### Advance invoice
- must record advance value,
- must not exceed received/declared advance according to application policy,
- if multiple advances exist, each remains individually referenceable.

### Final invoice
- must reference settled advance invoices,
- must expose settlement lines/summary,
- must not leave ambiguity which advances were consumed,
- if previous advances cover full value, final invoice gross due may be zero but settlement section remains mandatory.

## 1.13 Correction rules
### Hard law / compliance
- original document reference required,
- correction reason required,
- corrected values must be auditable.

### Recommended design choice
Represent correction content as:
- `CorrectionReference` to original,
- `CorrectionReason`,
- `CorrectedSections[]`,
- line deltas or before/after values.

Trade-off:
- delta-only model is compact but worse for audit and rendering,
- before/after model is clearer and maps well to print/test expectations.

Recommendation: use before/after semantics at domain/API boundary, optionally store normalized delta internally.

## 1.14 Rules dependent on buyer type

## 1.14.1 B2B
Indicators:
- buyer classified as business,
- buyer has NIP.

Default consequence:
- KSeF submission required.

## 1.14.2 B2C
Indicators:
- buyer is consumer/private person,
- NIP absent.

Default consequence:
- KSeF submission not required.
- approved-but-unsent editability may be allowed by policy.

## 1.14.3 Unknown buyer kind
If buyer kind is unknown:
- draft stage may emit warning,
- approve/send stages must resolve ambiguity when it impacts KSeF obligation.

## 1.15 Soft vs hard validation split

### Soft validation (`Draft`)
Examples:
- missing buyer email,
- unusual due date,
- missing public notes for preferred template,
- missing optional bank account,
- line description too generic.

### Hard validation (`Approve`, `SendToKsef`)
Examples:
- missing seller identity,
- inconsistent totals,
- correction without original reference,
- final invoice without advances,
- proforma attempted for KSeF send,
- B2B required KSeF send without buyer NIP resolution.

## 1.16 Domain events
- `InvoiceDrafted`
- `InvoiceApproved`
- `InvoiceApprovalReverted`
- `InvoiceSubmissionRequested`
- `InvoiceSubmittedToKsef`
- `InvoiceAcceptedByKsef`
- `InvoiceRejectedByKsef`
- `InvoiceDuplicateIssued`
- `InvoiceCorrectionIssued`

## 1.17 Proposed project structure
- `OpenKSeF.Invoices.Domain`
- `OpenKSeF.Invoices.Application`
- `OpenKSeF.Invoices.Infrastructure`
- `OpenKSeF.Invoices.Contracts`

Current `OpenKSeF.Domain.Entities.InvoiceHeader/InvoiceLine` should be treated as legacy persistence/read models and gradually isolated from new domain behavior.

Recommended rename target:
- `InvoiceHeader` -> `SyncedInvoice`
- `InvoiceLine` -> `SyncedInvoiceLine`

Recommended PostgreSQL outcome:
- rename legacy invoice sync tables so they clearly represent synchronized read-side storage,
- avoid keeping table names that suggest the sync schema is the issuing domain model,
- execute the rename with explicit EF/database migration steps rather than hiding the distinction behind code-only aliases.
