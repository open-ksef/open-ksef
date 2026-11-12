# ADR-004: API contract and DB migration policy

Status: accepted (2026-11-03). Numbers 002 and 003 are reserved by T-133 and T-161.

## Context

- No API versioning and no minimum client version. Google Play cannot force an update, so released builds call the API for as long as they stay installed. Last public build: mobile `v1.0.4`. `v1.0.5` has not reached Play (T-337). No iOS build is published.
- The mobile models lost no public property from `v1.0.0` to `v1.0.4`, so the `v1.0.4` models cover every released build.
- Mobile deserializes `Platform` as `int` (`open-ksef-mobile: Models/DeviceTokenDto.cs:14`). The server sends it as a number, because only `CredentialType` has a string converter (`Domain/Entities/CredentialType.cs:5`).
- A contract meaning can break without a shape change: mobile omitted `notificationEmail` on tenant PUT and wiped the stored value (T-067).
- The API is the only migrator, at startup, under an advisory lock (`Api/Program.cs:317-325`, `Domain/Data/MigrationHelper.cs:24-31`). The Worker never migrates.
- API and Worker start together. There is no real deploy order: on `v0.0.7` the Worker hit `42P01` on `IssuedInvoices` in the same second the API migrated (T-329, `Worker/Services/IssuedInvoiceSendService.cs:32`). The Worker has no dependency on the API (`docker-compose.yml:75-79`).
- Rollback by image works on an expanded schema. T-217 on a copy of a `v0.0.6` database: 3 migrations applied, row hashes unchanged, `v0.0.6` API and Worker ran on the migrated schema (sync, paid flag), then head ran forward again.
- Not all data changes are EF migrations. T-026 moves the encryption key at startup. Its rollback depends on config: going back works only if the env key is kept.
- A model change without a migration stops EF 9+ `Migrate` at startup (T-152). With `restart: unless-stopped`, any startup throw is a crash loop (T-026).

## Decision

### Contract

The mobile surface is what any Play build uses: `open-ksef-mobile` `ApiService.cs` and `AuthService.cs` REST calls, their JSON fields, and SignalR `ReceiveNotification` (`Api/Push/SignalRPushProvider.cs:28`). Baseline: models at tag `v1.0.4`, plus each later tag that reaches Play.

| # | Rule | Check |
|---|---|---|
| C1 | Mobile surface is additive only. No rename, removal, type change, or null in a field a released build holds as a value type. | T-079 fails on the backend. T-127 fails on the mobile side. T-086 shows the diff. Issuing: `IssuingContractSerializationTests`. |
| C2 | No global `JsonStringEnumConverter`. `Platform` stays an int. New enums are `string` properties or carry a converter on that type. | T-079 asserts `Platform` is a number. |
| C3 | New request fields are optional. An absent field leaves the stored value unchanged. | Controller test with the body a released build sends, as in T-067. |
| C4 | A new value in a string enum is a contract change. The PR states what released builds show for it (today the raw value, `open-ksef-mobile: ViewModels/IssuingFormat.cs:32`). | Principal review. |
| C5 | A contract PR set names backend `Api/Models`, portal `types.ts` and mobile `Models/*`, including "no change". | Diff of `openapi/v1.json` (T-086), principal review. |
| C6 | A portal-only field may change or go in one release, because portal and API ship from one tag (T-026 removed `SetupApplyResponse.EncryptionKey`). | `git -C open-ksef-mobile grep <field> <last Play tag> -- src/OpenKSeF.Mobile` is empty. Paste it in the PR. |
| C7 | Removing anything from the mobile surface needs a CEO cut-off in `decisions.md`, with evidence as in T-142: a replacement store release at 100% for 28 days and no use of the old path. | Principal rejects a removal without the entry. |
| C8 | A store release needs its endpoints on prod first. On a server without them (self-hosted, 404), the feature hides and nothing crashes (`open-ksef-mobile: CLAUDE.md:65`). | Unit test on the 404 path. `qa-mobile` before the store release. |

### Migrations

| # | Rule | Check |
|---|---|---|
| M1 | Every PR with a migration or a startup data step states 4 lines in its body: forward, rollback, data on prod, deploy order. | Principal rejects a PR without them. |
| M2 | Expand by default. `Up` may only create tables and their indexes and keys, add nullable columns or columns with a default, and add non-unique indexes on existing tables. | `MigrationPolicyTests` reads `UpOperations` of every migration after `20261102093230` and fails on anything else. |
| M3 | Anything else (drop, rename, type change, unique index on an existing table, raw `Sql`) is non-additive. It goes on the allow-list in that test with a reason, and triggers D3. | Same test. |
| M4 | A column or table is dropped only when the rollback target no longer maps it. So a removal takes two releases: unmap, then drop. | `git show <previous tag>:src/OpenKSeF.Domain/Data/Migrations/ApplicationDbContextModelSnapshot.cs` has no match. Paste it in the PR. |
| M5 | The model and the snapshot match. | T-152. |
| M6 | Only the API migrates. No host relies on start order: code reading a new table or column survives its absence for one cycle, without an Error log. | T-329. T-082 adds `depends_on` to the shipped compose only, not to the prod stack. |
| M7 | A startup data step runs in one transaction, is idempotent, is safe when two hosts race, and does not throw on a state the operator can fix. The PR has a rollback table per state. Removing the step later says in the release notes what an instance still before it must do, because self-hosters skip versions. | Testcontainers test, as in `EncryptionKeyUpgradeTests`. |

### Deploy and rollback

| # | Rule | Check |
|---|---|---|
| D1 | Sequence: backup, then API + Worker + portal from one `v*` tag, then verify, then mobile store last. | `release-checklist` steps 1 and 4, pasted in the release report. |
| D2 | Rollback target is the previous `v*` tag, for API, Worker and portal together, on the migrated schema. Set the tag in the stack instead of `:latest`, pull, compare `docker inspect .Created`. Config stays as it is unless the release notes say otherwise. The release report states the rollback path. | Release report. |
| D3 | A release with a non-additive migration or a startup data step needs a rehearsal first, as in T-217: restore a previous-release dump to a scratch container, apply, compare row hashes, run previous images on it, go forward again. Additive-only releases skip it. | `release-check` flags it, the checklist blocks the tag. |
| D4 | Never run `Down` on prod. It drops legal documents (ADR-001). Restoring a dump is the last resort and needs a CEO OK: it loses paid flags and issued-invoice state since the dump. | No automated check. |

Breaking a rule needs a `decisions.md` entry or a new ADR.

## Consequences

- Until T-079, T-127, T-086, T-152, T-329 and `MigrationPolicyTests` land, the rules above rest on principal review alone.
- Dropping a column takes two releases. A mobile field stays until a CEO cut-off, even when unused.
- Known users of M3 and D3: T-090 (dedupe, unique index) and T-161 (backfill SQL). `SyncState.Watermark` is still mapped at `v0.0.7`, so its drop needs M4.
- Rollback has been rehearsed only on a copy of the database, never on prod. A restore has not been rehearsed.
- The prod stack uses `:latest`. A failure while moving `:latest` can leave images mixed (`release.yml:143`), which D2 also recovers from.

## Rejected

- URL versioning (`/api/v2`): no breaking change to version yet.
- Min-version endpoint: built with the first unavoidable break, not before.
- OpenAPI codegen for portal and mobile: a toolchain in both clients for about 15 small types.
- Migrations in a separate job or by the Worker: API startup with the lock works, and a job needs an orchestrator the stack lacks.
- Scripted `Down` rollback: old images already run on the expanded schema, and `Down` destroys data.
- Rehearsal on every release: `MigrationPolicyTests` already covers the additive case, and a manual step on every tag gets skipped.
- `CREATE INDEX CONCURRENTLY` rules: prod tables are small, so there is no lock problem to solve yet.
- Renumbering T-133 and T-161 to take 002: their designs already name those numbers.
