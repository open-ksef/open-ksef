# 4. Refactor Backlog — OpenKSeF toward Clean Architecture

## 4.1 Goal
Refactor OpenKSeF so invoice issuing and validation become a real domain module rather than behavior attached to persistence models.

Naming target for the refactor:
- new domain aggregate: `Invoice`
- synchronized/read-side legacy model: `SyncedInvoice`
- synchronized/read-side legacy line model: `SyncedInvoiceLine`

Database implication:
- the refactor should explicitly consider PostgreSQL rename migrations for tables, indexes, foreign keys, and EF mappings that still use `InvoiceHeader` / `InvoiceLine` semantics,
- do not keep misleading legacy table names just to avoid migration work.

Traceability notation:
- Domain spec = `DS-x`
- Validation spec = `VS-x`
- Test scenarios = `TS-x`

Reference mapping:
- `DS-1` Scope / bounded contexts / architecture split → sections 1.1, 1.4, 1.5
- `DS-2` Aggregate model → sections 1.6–1.7
- `DS-3` Lifecycle / invariants → sections 1.8–1.15
- `VS-1` Validation pipeline → sections 2.1–2.11
- `TS-1` Test coverage → sections 3.1–3.10

## 4.2 Target project structure
Recommended new projects:
- `OpenKSeF.Invoices.Domain`
- `OpenKSeF.Invoices.Application`
- `OpenKSeF.Invoices.Infrastructure`
- `OpenKSeF.Invoices.Contracts`

Optional supporting projects:
- `OpenKSeF.Invoices.Domain.Tests`
- `OpenKSeF.Invoices.Application.Tests`
- `OpenKSeF.Invoices.IntegrationTests`

Current projects remain but should depend on the new invoice module instead of embedding invoice behavior directly.

## 4.3 Iteration plan

## Iteration 0 — Baseline and anti-corruption boundary
### Epic E0: Prepare refactor surface

#### Story E0-S1: Inventory current invoice-related code
Technical tasks:
- identify all current invoice-related entities/usages in `OpenKSeF.Domain`, API, worker, and portal
- identify naming collisions with `Invoice`, `InvoiceHeader`, `InvoiceLine`
- identify current migration/data coupling risks
- inventory PostgreSQL tables, indexes, constraints, and EF mappings affected by the rename toward `SyncedInvoice`

DoD:
- dependency inventory document exists
- all current write/read paths touching invoice data are listed
- naming collision list accepted

Traceability:
- `DS-1`, `DS-2`

#### Story E0-S2: Define anti-corruption layer for legacy models
Technical tasks:
- mark `InvoiceHeader` and `InvoiceLine` as legacy persistence/read models
- create mapping interfaces between legacy read models and new invoice contracts
- forbid new domain behavior additions in legacy EF entities
- define rename path from `InvoiceHeader` / `InvoiceLine` to `SyncedInvoice` / `SyncedInvoiceLine`
- decide whether PostgreSQL rename uses direct table rename, phased migrations, or compatibility views

DoD:
- clear code comments / architectural rule added
- mapper abstraction introduced
- at least one consumer switched to mapper abstraction
- rename strategy for code and PostgreSQL schema is documented

Traceability:
- `DS-2`

Risks:
- team keeps adding behavior into legacy entities
- migration pressure mixes read model and domain model again

## Iteration 1 — New domain kernel
### Epic E1: Introduce invoice domain model

#### Story E1-S1: Create `Invoice` aggregate
Technical tasks:
- introduce aggregate root, line entity, value objects, enums
- implement monetary calculation rules
- implement document kinds for VAT, advance, final, proforma, correction
- model duplicate as presentation event/metadata, not aggregate kind

DoD:
- aggregate compiles without infrastructure dependencies
- unit tests cover totals and document kind invariants
- no EF attributes in domain classes

Traceability:
- `DS-2`, `DS-3`, `TS-1`

#### Story E1-S2: Introduce policy abstractions
Technical tasks:
- add interfaces for numbering, VAT, KSeF requirement, correction, editability
- create in-memory/default implementations for tests

DoD:
- domain can execute decisions using abstractions only
- at least one policy-swappable unit test per policy family exists

Traceability:
- `DS-2`, `DS-3`, `VS-1`, `TS-1`

Risks:
- over-engineering policy surface
- leaking configuration primitives into aggregate methods

Trade-off:
- more interfaces now, less hard-coded legal/business coupling later. Worth it.

## Iteration 2 — Validation engine
### Epic E2: Build validation as first-class subsystem

#### Story E2-S1: Validation primitives and pipeline
Technical tasks:
- implement `ValidationContext`, `ValidationResult`, `ValidationMessage`, `IValidationRule<T>`
- stage-aware validation orchestrators for Draft/Approve/SendToKsef
- stable rule code registry

DoD:
- validation pipeline exists with stage filtering
- messages carry code + severity + user/technical text
- rule codes are unique and documented

Traceability:
- `VS-1`

#### Story E2-S2: Domain validation rules v1
Technical tasks:
- implement rules for structure, parties, dates, numbering, currency, lines, totals, VAT
- implement correction and advance/final rules
- implement state transition and immutability rules

DoD:
- all blocking v1 rules implemented
- unit tests cover positive and negative paths
- mapping table from documentation codes to classes exists

Traceability:
- `DS-3`, `VS-1`, `TS-1`

Risks:
- validation duplicates domain invariants incorrectly
- handlers bypass validation pipeline

## Iteration 3 — Application workflows
### Epic E3: Use cases and lifecycle orchestration

#### Story E3-S1: Draft / approve / unlock flows
Technical tasks:
- create commands: `CreateInvoice`, `UpdateInvoiceDraft`, `ApproveInvoice`, `ReopenInvoice`
- ensure approval path runs hard validation
- ensure reopen path uses policy

DoD:
- application handlers exist
- transition tests pass
- no controller writes aggregate state directly

Traceability:
- `DS-3`, `VS-1`, `TS-1`

#### Story E3-S2: Correction / advance / final flows
Technical tasks:
- create `CreateCorrectionFromOriginal`
- create `CreateFinalInvoiceFromAdvances`
- normalize references and settlement allocations

DoD:
- flows create correctly linked aggregates
- correction/final unit tests pass

Traceability:
- `DS-3`, `TS-1`

Risks:
- trying to reuse one generic "clone document" flow for all kinds. Bad idea.

## Iteration 4 — KSeF integration boundary
### Epic E4: Clean KSeF adapter

#### Story E4-S1: Domain to KSeF payload mapping
Technical tasks:
- create `InvoiceToKsefPayloadMapper`
- define `KsefInvoicePayload` in integration/contracts boundary
- keep open-ksef library specifics out of domain

DoD:
- mapper is isolated in infrastructure/integration layer
- send handler depends on interface, not concrete library DTOs

Traceability:
- `DS-1`, `DS-2`, `VS-1`, `TS-1`

#### Story E4-S2: Technical validation before submission
Technical tasks:
- add technical KSeF validation rules
- integrate open-ksef schema/library checks
- return domain-friendly validation messages with technical details attached

DoD:
- send pipeline blocks schema-invalid payloads before transport call
- negative integration tests exist

Traceability:
- `VS-1`, `TS-1`

#### Story E4-S3: Submission result handling
Technical tasks:
- create workflow for `Submitted`, `Accepted`, `Rejected`
- persist KSeF identifiers and result metadata
- lock aggregate on acceptance

DoD:
- accepted document becomes immutable
- rejected document remains recoverable
- event/log trail exists

Traceability:
- `DS-3`, `TS-1`

Risks:
- mixing transport retries with domain transition logic
- storing KSeF SDK objects directly in aggregate/persistence schema

## Iteration 5 — Persistence and legacy migration
### Epic E5: Persistence model alignment

#### Story E5-S1: Introduce invoice persistence model
Technical tasks:
- create EF persistence model or repository mappings for new aggregate
- keep current sync/browse model for sync/read use if still needed
- define migration strategy for coexistence
- rename legacy persistence types and PostgreSQL objects from `InvoiceHeader` / `InvoiceLine` toward `SyncedInvoice` / `SyncedInvoiceLine`

DoD:
- repositories persist new aggregate independently
- legacy read models remain operational
- migration plan documented
- PostgreSQL rename plan covers tables, PK/FK names, indexes, and rollback path

Traceability:
- `DS-2`

#### Story E5-S2: Read model / projection strategy
Technical tasks:
- project aggregate into API-friendly read DTOs
- decide if current `SyncedInvoice` becomes read projection only
- create dedicated print projection model

DoD:
- no API endpoint reads directly from aggregate internals without DTO/projection
- print projection supports English view and duplicate view

Traceability:
- `DS-1`, `DS-2`, `TS-1`

## Iteration 6 — Presentation and print
### Epic E6: Presentation-only concerns isolated

#### Story E6-S1: Print model and rendering
Technical tasks:
- create `InvoicePrintModel`
- implement standard, duplicate, and English print variants
- ensure no fiscal identity change from print selection

DoD:
- English print uses same aggregate data
- duplicate does not create new aggregate
- regression tests exist

Traceability:
- `DS-3`, `TS-1`

## 4.4 Cross-cutting technical tasks

### X1 Naming cleanup
- reserve `Invoice` for the new domain aggregate
- rename legacy/read-side models to `SyncedInvoice` / `SyncedInvoiceLine`
- remove `InvoiceHeader` / `InvoiceLine` from places where they imply domain ownership
- align PostgreSQL names with the sync/read-side meaning

### X2 Error code governance
- central registry for `INV-VAL-###`
- CI test to prevent duplicates

### X3 Architectural guards
- add test or analyzer preventing infrastructure references from Domain
- add test preventing controllers from mutating aggregates directly

### X4 Documentation as code
- keep docs under `docs/domain/`
- add ADR referencing naming choice and duplicate-as-print decision

## 4.5 Risks and dependencies

### External dependencies
- open-ksef library capabilities and payload surface
- KSeF schema/rule changes
- current OpenKSeF DB schema and migration tolerance

### Main risks
1. **Naming confusion**
   - Current system already uses invoice terminology loosely.
   - Mitigation: strict naming boundary, ADR, and explicit rename from legacy sync models to `SyncedInvoice`.

2. **Legacy persistence gravity**
   - Team may keep using EF entities as domain.
   - Mitigation: anti-corruption layer + code review rule.

6. **PostgreSQL rename risk**
   - Table/index/constraint renames can be noisy and easy to under-plan.
   - Mitigation: explicit migration design, staged rollout, and rollback path.

3. **Policy vs compliance mixing**
   - Dangerous because legal rules become optional accidentally.
   - Mitigation: explicit classification per rule.

4. **KSeF adapter leakage**
   - SDK types creeping into application/domain.
   - Mitigation: dedicated mapper/contracts.

5. **Overgeneralization for future currencies**
   - Can bloat v1.
   - Mitigation: future-ready VO, PLN-first enforcement.

## 4.6 Definition of Done by stage

### Stage DoD — Domain kernel
- aggregate and VOs implemented
- invariants unit-tested
- no infrastructure references

### Stage DoD — Validation
- all v1 rule codes implemented
- stage-aware pipeline works
- docs and tests aligned

### Stage DoD — Workflows
- draft/approve/reopen/send flows implemented
- state transitions tested
- immutable-after-KSeF enforced

### Stage DoD — KSeF integration
- mapper isolated
- technical validation present
- success/reject handling implemented

### Stage DoD — Presentation
- standard, duplicate, English print supported
- no print mode alters fiscal domain identity

## 4.7 Recommended implementation order summary
1. Inventory + anti-corruption boundary
2. New domain aggregate and value objects
3. Validation engine and rules
4. Application workflows
5. KSeF mapping + technical validation
6. Persistence integration
7. Presentation/print isolation

That order is boring. Good. Boring survives production.
