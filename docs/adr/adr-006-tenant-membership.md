# ADR-006: Tenant membership with predefined roles

Status: accepted (2026-11-15). Requirement REQ-0037. Policy ADR-004.

## Context

- A tenant has one user, `Tenant.UserId` (`src/OpenKSeF.Domain/Entities/Tenant.cs:6`), with `(UserId, Nip)` unique (`src/OpenKSeF.Domain/Data/ApplicationDbContext.cs:35`). Access is checked by 20 `OwnsTenantAsync` calls (`src/OpenKSeF.Api/Controllers/TenantOwnership.cs:8`) and 9 direct `t.UserId ==` comparisons, including push routing (`src/OpenKSeF.Domain/Services/NotificationService.cs:151`) and sync (`src/OpenKSeF.Sync/TenantSyncService.cs:181`).
- REQ-0037: tenants stay as they are; owners invite people by e-mail; 4 predefined roles with a permission matrix in code, no custom roles; clients read effective permissions from the API.
- Paid flags are written only by `InvoicesController.SetPaid`, so today the tenant creator is the only possible marker.
- `AuditEvents` exists for instance-level user actions (`src/OpenKSeF.Domain/Entities/AuditEvent.cs`).

## Decision

1. Membership lives in the app DB: `TenantMembers(TenantId, UserId, Role, CreatedAt)`, PK `(TenantId, UserId)`, FK to `Tenants` with cascade. After the authorization switch it is the only source of access. `Tenant.UserId` stays as the write-once creator column: it backs the `(UserId, Nip)` index and image rollback.
2. `TenantRole { Owner, Administrator, Accountant, Employee }`, stored as a string. `TenantPermissions` maps roles to permission strings, which are also the wire format: `invoices.read`, `invoices.markPaid`, `sync.run`, `notifications.self`, `issuing.draft` (all roles); `issuing.submit` (not Employee); `sync.fullResync`, `credential.manage`, `tenant.edit`, `members.manage` (Owner, Administrator); `tenant.delete` (Owner). `CanManageRole`: only an Owner grants, changes or removes the Owner role. Leaving is not a permission; the last Owner cannot leave.
3. Authorization is one action filter `[TenantAccess("permission")]` on every `{tenantId}` route: non-member 404, missing permission 403 with a Polish `error`. Sync and the Worker get no user. A reflection test fails any `{tenantId}` action without it.
4. Effective permissions on the wire: `TenantResponse` gains `role` and `permissions`, additive (ADR-004 C1).
5. Expand-only rollout. Migration `AddTenantMembership` creates the table, adds nullable `InvoiceHeaders.PaidByUserId`, inserts one Owner per tenant from `Tenant.UserId`, and sets `PaidByUserId = Tenant.UserId` on paid rows. `IsPaid` and `PaidAt` are never written. The authorization switch ships in a later release and first re-runs the same idempotent backfill, covering rows written by a rolled-back image. The re-run also executes `UPDATE "InvoiceHeaders" SET "PaidByUserId" = NULL WHERE NOT "IsPaid" AND "PaidByUserId" IS NOT NULL;` before the fill.
6. Invites, user profiles and tenant audit columns arrive with their first consumer, each as its own additive migration. Tenant audit extends the existing `AuditEvents` table.

## Consequences

- Single-user tenants behave as before.
- Until the switch, membership is written but not read; old and new images are interchangeable.
- Non-members will get 404 where some endpoints return 403 today. JSON shape unchanged.
- A creator who left a company still holds its `(UserId, Nip)` slot and gets 409 on re-creating the same NIP. Accepted, rare.
- Rollback is the previous image; invited members lose access until roll-forward. No data loss.

## Rejected

- Keycloak groups/organizations as membership store: two stores, no join with invoice queries.
- `IAuthorizationHandler` with resource policies: 404/403 split plus Polish body needs more parts than one filter.
- EF global query filter per user: Worker has no user; hides 404 vs 403.
- Permissions or custom roles in the DB: CEO said no (REQ-0037 CEO-Q5).
- Dropping or renaming `Tenant.UserId`: breaks image rollback, still backs the unique index.
- Switching authorization in the same release as the schema: rewrites every test fixture and loses the safe rollback window.
- All shared-company tables in one migration: tables without a consumer, shapes still open.
- New `OpenKSeF.Membership` project: one consumer (Api).
- `issuing.approve`: no approve step exists.
