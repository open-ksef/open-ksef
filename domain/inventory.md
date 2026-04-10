# Invoice Domain Inventory

Generated: 2026-04-09  
Scope: E0-S1 — baseline scan of `InvoiceHeader` / `InvoiceLine` usages before the domain refactor.

---

## 1. Entity definitions

| Class | File | Role |
|-------|------|------|
| `InvoiceHeader` | `OpenKSeF.Domain/Entities/InvoiceHeader.cs` | EF entity — synced read-side invoice record |
| `InvoiceLine` | `OpenKSeF.Domain/Entities/InvoiceLine.cs` | EF entity — line item belonging to `InvoiceHeader` |

### InvoiceHeader fields
`Id`, `TenantId`, `KSeFInvoiceNumber`, `KSeFReferenceNumber`, `InvoiceNumber`, `VendorName`, `VendorNip`, `BuyerName`, `BuyerNip`, `AmountNet`, `AmountVat`, `AmountGross`, `Currency`, `IssueDate`, `AcquisitionDate`, `InvoiceType`, `FirstSeenAt`, `LastUpdatedAt`, `VendorBankAccount`, `IsPaid`, `PaidAt`  
Navigation: `Tenant`, `ICollection<InvoiceLine> Lines`

### InvoiceLine fields
`Id`, `InvoiceHeaderId`, `LineNumber`, `Name`, `Unit`, `Quantity`, `UnitPriceNet`, `UnitPriceGross`, `AmountNet`, `AmountGross`, `AmountVat`, `VatRate`  
Navigation: `InvoiceHeader`

---

## 2. PostgreSQL tables

| Table | PK | FKs | Unique indexes | Non-unique indexes |
|-------|----|-----|----------------|-------------------|
| `InvoiceHeaders` | `PK_InvoiceHeaders (Id)` | `FK_InvoiceHeaders_Tenants_TenantId` | `IX_InvoiceHeaders_TenantId_KSeFInvoiceNumber` | `IX_InvoiceHeaders_IssueDate` |
| `InvoiceLines` | `PK_InvoiceLines (Id)` | `FK_InvoiceLines_InvoiceHeaders_InvoiceHeaderId` (cascade delete) | — | `IX_InvoiceLines_InvoiceHeaderId` |

EF model snapshot confirms: `b.ToTable("InvoiceHeaders")` and `b.ToTable("InvoiceLines")` — no custom table name overrides.

Target rename (per spec `01:L299-L316`):
- `InvoiceHeaders` → `synced_invoices`
- `InvoiceLines` → `synced_invoice_lines`

---

## 3. EF mappings (`ApplicationDbContext`)

```
DbSet<InvoiceHeader> InvoiceHeaders
DbSet<InvoiceLine>   InvoiceLines
```

Configuration highlights:
- `InvoiceHeader`: precision(18,2) on monetary fields; MaxLength on string fields; unique composite index on `(TenantId, KSeFInvoiceNumber)`.
- `InvoiceLine`: precision(18,6) on quantity/unit price; precision(18,2) on amount fields; cascade delete from header.
- `Tenant.Invoices` is `ICollection<InvoiceHeader>` — navigation property must be renamed as part of the refactor.

---

## 4. All files referencing `InvoiceHeader` / `InvoiceLine`

### OpenKSeF.Domain

| File | Usage |
|------|-------|
| `Entities/InvoiceHeader.cs` | Definition |
| `Entities/InvoiceLine.cs` | Definition |
| `Entities/Tenant.cs` | Navigation property `ICollection<InvoiceHeader> Invoices` |
| `Data/ApplicationDbContext.cs` | `DbSet`, EF model configuration |
| `Data/Migrations/*.cs` | All migration files (create, alter, snapshot) |
| `DTOs/InvoiceDto.cs` | Internal sync DTO (not the entity; maps to `InvoiceHeader` fields) |
| `DTOs/InvoiceLineDto.cs` | Internal sync DTO (maps to `InvoiceLine` fields) |
| `Abstractions/IKSeFGateway.cs` | `KSeFInvoiceHeader` record (distinct type — KSeF query result) |
| `Services/InvoiceService.cs` | Creates/updates `InvoiceHeader` + `InvoiceLine` entities; write path |

### OpenKSeF.Api

| File | Usage |
|------|-------|
| `Controllers/InvoicesController.cs` | Reads `_db.InvoiceHeaders` with `.Include(l => l.Lines)` |
| `Controllers/InvoicesSummaryController.cs` | Reads/aggregates `_db.InvoiceHeaders` |
| `Controllers/DashboardController.cs` | Counts `_db.InvoiceHeaders` per tenant |
| `Services/SystemSettingsService.cs` | `db.InvoiceHeaders.AnyAsync()` — checks data presence |
| `Models/InvoiceResponse.cs` | API response model mapped from `InvoiceHeader` |
| `Models/InvoiceLineResponse.cs` | API response model mapped from `InvoiceLine` |

### OpenKSeF.Sync

| File | Usage |
|------|-------|
| `TenantSyncService.cs` | Reads `_db.InvoiceHeaders` (earliest date, already-parsed check); calls `InvoiceService.UpsertInvoicesAsync` |
| `KSeFInvoiceXmlParser.cs` | Returns `InvoiceLineDto` objects (DTO, not entity) |
| `KSeFGateway.cs` | Uses `KSeFInvoiceHeader` record from `IKSeFGateway` abstraction |

### OpenKSeF.Worker

No direct `InvoiceHeader`/`InvoiceLine` references — uses `InvoiceService` indirectly through sync orchestration.

### OpenKSeF.Portal / OpenKSeF.Portal.Web

No TypeScript/React references to `InvoiceHeader` or `InvoiceLine` — portal consumes REST API responses only.

### Test projects

| File | Usage |
|------|-------|
| `OpenKSeF.Api.Tests/Controllers/InvoicesControllerTests.cs` | Test fixtures with `InvoiceHeader` objects |
| `OpenKSeF.Api.Tests/Controllers/InvoicesSummaryControllerTests.cs` | Same |
| `OpenKSeF.Api.Tests/Controllers/DashboardControllerTests.cs` | Same |
| `OpenKSeF.Domain.Tests/Services/TransferDetailsServiceTests.cs` | Uses `InvoiceLine` / `InvoiceHeader` test data |

---

## 5. Write paths

| Path | Trigger | What it writes |
|------|---------|----------------|
| `InvoiceService.UpsertInvoicesAsync()` | Called by `TenantSyncService` after KSeF sync | Creates or updates `InvoiceHeader` + `InvoiceLine` records |
| `InvoicesController.SetPaid()` | `PATCH /api/invoices/{id}/paid` | Updates `InvoiceHeader.IsPaid`, `PaidAt` |

---

## 6. Read paths

| Path | Trigger | What it reads |
|------|---------|--------------|
| `InvoicesController.GetAll()` | `GET /api/invoices` | Paged `InvoiceHeaders` for tenant |
| `InvoicesController.GetById()` | `GET /api/invoices/{id}` | Single `InvoiceHeader` + `Lines` |
| `InvoicesController.GetXml()` | `GET /api/invoices/{id}/xml` | Single `InvoiceHeader` (for XML download) |
| `InvoicesController.SetPaid()` | `PATCH` (read before write) | Single `InvoiceHeader` |
| `InvoicesSummaryController.GetSummary()` | `GET /api/invoices/summary` | Aggregated `InvoiceHeaders` |
| `DashboardController` | `GET /api/dashboard` | Count of `InvoiceHeaders` per tenant/period |
| `SystemSettingsService` | Setup wizard check | `AnyAsync()` on `InvoiceHeaders` |
| `TenantSyncService` | Sync run | Earliest `IssueDate` from `InvoiceHeaders`; already-parsed set |

---

## 7. Naming collisions with target name `Invoice`

The new domain aggregate must be named `Invoice`. The following names currently exist and need attention:

| Name | Location | Conflict level | Resolution |
|------|----------|---------------|-----------|
| `InvoiceDto` | `Domain/DTOs/InvoiceDto.cs` | Medium — internal sync DTO | Rename to `SyncedInvoiceDto` |
| `InvoiceLineDto` | `Domain/DTOs/InvoiceLineDto.cs` | Medium | Rename to `SyncedInvoiceLineDto` |
| `InvoiceService` | `Domain/Services/InvoiceService.cs` | Medium — sync service, not domain | Rename to `SyncedInvoiceSyncService` or `InvoiceSyncService` |
| `IInvoiceService` | `Domain/Services/IInvoiceService.cs` | Medium | Rename accordingly |
| `InvoicesController` | `Api/Controllers/InvoicesController.cs` | Low — HTTP routing only | Keep (routes `/api/invoices`) |
| `InvoiceResponse` | `Api/Models/InvoiceResponse.cs` | Low — API response DTO | Keep or rename to `SyncedInvoiceResponse` for clarity |
| `InvoiceLineResponse` | `Api/Models/InvoiceLineResponse.cs` | Low | Same |
| `KSeFInvoiceHeader` | `Domain/Abstractions/IKSeFGateway.cs` | None — different namespace and purpose | Keep |

---

## 8. Summary

- **2 legacy EF entities** (`InvoiceHeader`, `InvoiceLine`) covering the sync read-side model.
- **2 PostgreSQL tables** (`InvoiceHeaders`, `InvoiceLines`) — both named after the legacy entities.
- **~14 production source files** and **4 test files** reference `InvoiceHeader` or `InvoiceLine` directly.
- **No Portal/frontend code** references these names — the boundary is the REST API.
- **The `Invoice` name is currently free** at the aggregate/domain layer; only the sync-side DTOs and services use `Invoice*` naming and need renaming before the new domain project is created.
- Target rename for persistence: `InvoiceHeaders` → `synced_invoices`, `InvoiceLines` → `synced_invoice_lines`.
