# ADR-001: Invoice issuing inside the existing layers

Status: accepted (2026-10-30).

## Context

- `origin/invocing` (PR #17) adds 5 projects `OpenKSeF.Invoices.{Domain,Application,Contracts,Infrastructure,Domain.Tests}`, renames `InvoiceHeader` -> `SyncedInvoice` in a prod migration, and stubs the KSeF send (T-015 report: `InvoicesAggregateController.cs:310`, migration `20260410063716_...cs:14`).
- Current layering: Domain <- Sync <- Api/Worker (`CLAUDE.md:28-36`). KSeF code lives in Sync next to `KSeFGateway` (`CLAUDE.md:47`, `:64`). Direct `ApplicationDbContext`, no repositories (`CLAUDE.md:75-80`).
- Working FA(3) send exists: `tools/ksef-test-invoices/Program.cs:104-145` (32/32 accepted on TEST).
- CEO 2026-10-30: one release (B2B + B2C + PDF with KOD I + copy), on for everyone, NIP lookup from the MF white list, mobile issuing form.

## Decision

1. **No new projects.** Domain code goes to `OpenKSeF.Domain/Invoicing/`. The FA(3) writer, XSD validator and KSeF submitter go to `OpenKSeF.Sync/Issuing/`. The send loop goes to `OpenKSeF.Worker/Services/`. Controllers and DTOs go to `OpenKSeF.Api`. The aggregate is the EF entity, so there is no record type, no mapper and no repository.
2. **No rename.** `InvoiceHeader`/`InvoiceLine` and their tables stay. Issued invoices are `IssuedInvoice`/`IssuedInvoiceLine` (tables `IssuedInvoices`, `IssuedInvoiceLines`). One additive migration also creates `SellerProfiles` and `InvoiceNumberCounters`.
3. **The send runs in the Worker.** It is a new `BackgroundService` that polls every 5 s. The `IssuedInvoices` row is the outbox, so there is no outbox table. Write-ahead order: the session ref and `SendStartedAt` are saved before the send, and the element ref is saved right after it. On recovery the worker matches the invoice by hash through `GET sessions/{ref}/invoices` before it resends. KSeF `440` duplicate detection is the backstop. Auth uses the existing `IKSeFGateway.InitSessionAsync` and token decryption.
4. **XML is built and validated in the API at issue time.** The exact bytes are stored. KSeF receives those bytes, the KOD I hash is computed from them, and the PDF is rendered from them. The KOD I URL comes from KSeF.Client `IVerificationLinkService` and is stored at issue time.
5. **The PDF is rendered on the client** with CIRFMF `ksef-pdf-generator`, pinned to tag `1.1.46`. The portal uses a vendored build. Mobile loads the same bundle in a HybridWebView. There is no server PDF.
6. **The contract is additive.** New `Api/Models/*` types only. `InvoiceResponse` is unchanged. Status and enum fields are strings.
7. **NIP lookup is an API adapter.** A typed `HttpClient` to `wl-api.mf.gov.pl`, cached for 24 h per NIP, with no interface.

## Consequences

- Rollback = previous image tags. The schema is additive, and old API/Worker images never read the new tables.
- Never run `Down` on prod after the first real invoice. It drops legal documents (XML/UPO).
- The domain stays testable without EF, because the aggregate has no EF attributes and is mapped in `ApplicationDbContext`.
- The Worker becomes the single writer of KSeF results. No endpoint can set a KSeF number.
- Mobile depends on a JS bundle for the PDF. If HybridWebView is unstable, mobile ships without the PDF and the portal provides it.

## Rejected

- Keep 5 `Invoices.*` projects: 4 layers + CQRS for CRUD-plus-send, and no second consumer.
- One `OpenKSeF.Invoices` project: it would need both EF (Domain) and KSeF.Client (Sync), so it cuts across the layers instead of fitting them.
- `SyncedInvoice` rename: it is wire-neutral (T-015) but breaks rollback (T-081) for zero gain.
- Send in an API hosted service: CLAUDE.md puts background jobs in the Worker (`:64`), and the API may scale out.
- Send inline in HTTP: KSeF takes seconds to minutes and returns 429.
- Separate outbox table: the invoice row already carries the state.
- Server PDF (QuestPDF): it is not the official layout and needs its own KOD I rendering.
- Node sidecar for the generator: a new runtime in the API image.
- Value objects `Money`/`PartyName`/`PostalAddress`: PLN only, flat columns, and the checks live in one issue check list.
- Feature switch: CEO Q2 = on for everyone.
