# Invoice Domain Module — Task List

Reference docs (all paths relative to `domain/`):
- `01` = `01-domain-specification.md`
- `02` = `02-validation-specification.md`
- `03` = `03-test-scenarios.md`
- `04` = `04-refactor-backlog.md`

Line references use format `01:L44-L64` = file 01, lines 44–64.

---

## Iteration 0 — Baseline & Anti-Corruption Boundary

### E0-S1: Inventory current invoice-related code
> Backlog: `04:L46-L60`

- [x] Scan `OpenKSeF.Domain`, API, Worker, Portal for all usages of `InvoiceHeader`, `InvoiceLine`
- [x] List all PostgreSQL tables, indexes, FK constraints, EF mappings using `InvoiceHeader`/`InvoiceLine` naming
- [x] Identify naming collisions with target name `Invoice`
- [x] Document all write/read paths touching invoice data
- [x] Produce dependency inventory document (output: `domain/inventory.md` or similar)

### E0-S2: Anti-corruption layer for legacy models
> Backlog: `04:L62-L81`

- [x] Mark `InvoiceHeader` / `InvoiceLine` as legacy read models (code comments, `[Obsolete]` or doc)
- [x] Create mapping interfaces between legacy read models and new invoice contracts
- [x] Forbid new domain behavior in legacy EF entities (add architectural rule/test)
- [x] Document rename strategy: `InvoiceHeader` -> `SyncedInvoice`, `InvoiceLine` -> `SyncedInvoiceLine` (code + PostgreSQL). Naming rationale: `01:L299-L316`
- [x] Decide migration approach: direct rename vs phased vs compatibility views
- [x] Switch at least one consumer to use mapper abstraction

---

## Iteration 1 — Domain Kernel

### E1-S1: Create `Invoice` aggregate
> Backlog: `04:L85-L99` | Full model: `01:L136-L260`

- [x] Create project `OpenKSeF.Invoices.Domain`
- [x] Create project `OpenKSeF.Invoices.Domain.Tests`
- [x] Implement enums: `DocumentKind`, `DocumentStatus`, `BuyerKind`, `PricingMode`, `KsefSubmissionRequirement`, `KsefSubmissionState`, `CorrectionReasonKind` — `01:L262-L287`
- [x] Implement value objects: `Money`, `CurrencyCode`, `Percentage`, `VatRate`, `VatClassification`, `TaxExemptionReason`, `Nip`, `PartyName`, `PostalAddress`, `BankAccountNumber`, `DocumentNumber`, `IssueDates`, `KsefIdentifiers`, `ApprovalState`, `DocumentTotals`, `VatSummary`, `AdvanceAllocation`, `CorrectionReference`, `DuplicateMetadata` — `01:L241-L261`
- [x] Implement `SellerSnapshot`, `BuyerSnapshot` party snapshots — `01:L171-L173`
- [x] Implement `InvoiceLine` entity with monetary calculation — `01:L218-L239`
- [x] Implement `Invoice` aggregate root: identity `01:L142-L146`, classification `01:L148-L169`, dates `01:L176-L183`, money/currency `01:L185-L192`, commercial data `01:L194-L205`, KSeF state `01:L207-L212`, relations `01:L214-L216`
- [x] Implement state machine: Draft -> Approved -> Submitted -> Accepted/Rejected — `01:L318-L370`
- [x] Implement aggregate invariants — `01:L379-L395`
- [x] Implement cross-document invariants — `01:L376-L378`
- [x] Implement document kind rules: VAT `01:L398-L411`, Advance `01:L413-L419`, Final `01:L421-L427`, Proforma `01:L429-L435`, Correction `01:L437-L447`, Duplicate-as-metadata `01:L449-L457`
- [x] Implement domain events — `01:L573-L582`
- [x] Unit tests: totals calculation, document kind invariants, state transitions
- [x] Verify: no EF/infrastructure references in domain project

### E1-S2: Policy abstractions
> Backlog: `04:L101-L118` | Interface list: `01:L289-L298`

- [x] Define interfaces: `INumberingPolicy`, `IDocumentUniquenessPolicy`, `IBuyerClassificationPolicy`, `IKsefRequirementPolicy`, `IVatPolicy`, `ICorrectionPolicy`, `IAdvanceSettlementPolicy`, `IApprovedEditPolicy`, `IClock`
- [x] Create in-memory/default test implementations for each policy
- [x] Unit test: at least one policy-swappable test per policy family

---

## Iteration 2 — Validation Engine

### E2-S1: Validation primitives and pipeline
> Backlog: `04:L123-L137` | Interfaces: `02:L38-L81`

- [x] Implement `ValidationStage`, `ValidationSeverity` enums — `02:L38-L49`
- [x] Implement `ValidationMessage` record — `02:L52-L59`
- [x] Implement `ValidationContext` record — `02:L61-L68`
- [x] Implement `ValidationResult` record — `02:L70-L73`
- [x] Implement `IValidationRule<T>` interface — `02:L75-L80`
- [x] Implement pipeline split interfaces: `IDomainValidationRule<Invoice>`, `IDomainValidationRule<InvoiceLine>`, `IKsefTechnicalValidationRule<KsefInvoicePayload>`, `IStateTransitionRule<Invoice>` — `02:L89-L91`
- [x] Implement orchestrators: `DraftValidationService`, `ApprovalValidationService`, `KsefSubmissionValidationService` — `02:L93-L97`
- [x] Implement `IPolicyProvider` / `IPolicySnapshot` — `02:L103-L118`

### E2-S2: Domain validation rules v1
> Backlog: `04:L139-L153` | Full rule catalog: `02:L124-L448`

- [x] Rules: Structure/identity — `INV-VAL-001..003` — `02:L127-L146`
- [x] Rules: Parties — `INV-VAL-010..013` — `02:L149-L176`
- [x] Rules: Dates — `INV-VAL-020..022` — `02:L179-L199`
- [x] Rules: Numbering — `INV-VAL-030..032` — `02:L202-L222`
- [x] Rules: Currency — `INV-VAL-040..042` — `02:L225-L245`
- [x] Rules: Lines/totals — `INV-VAL-050..053` — `02:L248-L275`
- [x] Rules: VAT — `INV-VAL-060..064` — `02:L278-L312`
- [x] Rules: Advance/final — `INV-VAL-070..073` — `02:L315-L341`
- [x] Rules: Correction — `INV-VAL-080..083` — `02:L344-L370`
- [x] Rules: KSeF requirement — `INV-VAL-090..093` — `02:L373-L401`
- [x] Rules: State transition/immutability — `INV-VAL-100..102` — `02:L405-L425`
- [x] Rules: Technical KSeF payload — `INV-VAL-110..112` — `02:L428-L448`
- [x] Unit tests per rule: positive + negative path
- [x] Create rule code -> class mapping table

### Validation tests
> Each scenario references a validation rule. Implement alongside or after E2-S2.

- [x] VAT tests: `VAT-001..006` — `03:L7-L39`
- [x] Advance tests: `ADV-001..002` — `03:L42-L52`
- [x] Final invoice tests: `FIN-001..004` — `03:L54-L74`
- [x] Correction tests: `COR-001..006` — `03:L77-L112`
- [x] Proforma tests: `PRO-001..003` — `03:L115-L130`
- [x] Buyer classification tests: `BUY-001..005` — `03:L133-L161`
- [x] State transition tests: `ST-001..006` — `03:L163-L199`
- [x] Immutability tests: `IMM-001..003` — `03:L201-L218`
- [x] Config/policy tests: `CFG-001..005` — `03:L220-L249`
- [x] Regression tests: `REG-001..004` — `03:L251-L278`
- [x] KSeF technical failure tests: `KTF-001..003` — `03:L280-L297`

---

## Iteration 3 — Application Workflows

### E3-S1: Draft / approve / unlock flows
> Backlog: `04:L158-L170`

- [x] Create project `OpenKSeF.Invoices.Application`
- [x] Create project `OpenKSeF.Invoices.Contracts` (request/response DTOs)
- [x] Implement command: `CreateInvoice`
- [x] Implement command: `UpdateInvoiceDraft`
- [x] Implement command: `ApproveInvoice` (runs hard validation pipeline)
- [x] Implement command: `ReopenInvoice` (uses `IApprovedEditPolicy`). Policy context: `01:L331-L337`
- [x] Ensure no controller mutates aggregate state directly
- [x] Tests: state transition flows — `03:L163-L199` (ST-001..ST-004)

### E3-S2: Correction / advance / final flows
> Backlog: `04:L172-L186`

- [ ] Implement command: `CreateCorrectionFromOriginal`. Rules: `01:L437-L447`
- [ ] Implement command: `CreateFinalInvoiceFromAdvances`. Rules: `01:L421-L427`, settlement: `01:L498-L509`
- [ ] Normalize references and settlement allocations
- [ ] Tests: correction — `03:L77-L112`, advance/final — `03:L42-L74`

---

## Iteration 4 — KSeF Integration Boundary

### E4-S1: Domain to KSeF payload mapping
> Backlog: `04:L191-L202` | Naming: `01:L299-L316`

- [ ] Create project `OpenKSeF.Invoices.Infrastructure`
- [ ] Define `KsefInvoicePayload` in contracts/integration boundary
- [ ] Implement `InvoiceToKsefPayloadMapper` in infrastructure layer
- [ ] Ensure mapper depends on interface, not concrete open-ksef library DTOs

### E4-S2: Technical validation before submission
> Backlog: `04:L204-L215`

- [ ] Implement `IKsefTechnicalValidationRule` rules: `INV-VAL-110..112` — `02:L428-L448`
- [ ] Integrate open-ksef schema/library validation
- [ ] Send pipeline blocks schema-invalid payloads before transport
- [ ] Negative integration tests — `03:L280-L297` (KTF-001..003)

### E4-S3: Submission result handling
> Backlog: `04:L217-L232`

- [ ] Implement workflow: Submitted -> Accepted (persist KSeF identifiers, lock aggregate). Transitions: `01:L345-L350`
- [ ] Implement workflow: Submitted -> Rejected (store rejection, allow recovery). Transitions: `01:L352-L359`
- [ ] Tests: `ST-005..006` — `03:L186-L199`, `IMM-001..002` — `03:L201-L212`

---

## Iteration 5 — Persistence & Legacy Migration

### E5-S1: Invoice persistence model
> Backlog: `04:L237-L253`

- [ ] Create EF persistence model / repository for new `Invoice` aggregate
- [ ] Keep sync/browse model (`SyncedInvoice`) operational for existing read paths
- [ ] Execute PostgreSQL rename: `InvoiceHeader` -> `synced_invoices`, `InvoiceLine` -> `synced_invoice_lines` (tables, PK/FK, indexes). Migration notes: `01:L17-L20`
- [ ] Document migration rollback path
- [ ] Define coexistence strategy for old + new persistence

### E5-S2: Read model / projection strategy
> Backlog: `04:L255-L265`

- [ ] Project aggregate into API-friendly read DTOs
- [ ] Decide if `SyncedInvoice` becomes read projection only
- [ ] Create print projection model supporting English + duplicate views
- [ ] Ensure no API endpoint reads aggregate internals without DTO/projection

---

## Iteration 6 — Presentation & Print

### E6-S1: Print model and rendering
> Backlog: `04:L269-L280` | Design decisions: `01:L449-L463`

- [ ] Create `InvoicePrintModel`
- [ ] Implement standard print variant
- [ ] Implement duplicate print variant (no new aggregate, metadata only) — `01:L449-L457`
- [ ] Implement English print variant (labels only, same data) — `01:L459-L463`
- [ ] Tests: `REG-003..004` — `03:L266-L278`, `IMM-003` — `03:L214-L218`

---

## Cross-Cutting Tasks
> `04:L283-L300`

- [ ] **X1 Naming cleanup** `04:L285-L289`: reserve `Invoice` for domain; rename legacy to `SyncedInvoice`/`SyncedInvoiceLine`; align PostgreSQL
- [ ] **X2 Error code governance** `04:L291-L292`: central `INV-VAL-###` registry + CI test for uniqueness
- [ ] **X3 Architectural guards** `04:L294-L296`: test preventing infra refs from Domain; test preventing controllers from mutating aggregates
- [ ] **X4 Documentation as code** `04:L298-L300`: keep domain docs in `docs/domain/`; add ADR for naming + duplicate-as-print decisions
