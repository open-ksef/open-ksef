# ADR-003: Hosted Keycloak pages identical to the portal, portal account pages, TOTP 2FA

Status: accepted (2026-11-14, T-723; backend T-726, portal T-725). Amends ADR-002 decisions 1, 3, 6, 7. Deviation from decision 8 (T-731 principal, CEO-level rule): the API never grants itself `manage-users`; see "Admin reset contract".
Number: ADR-004 line 3 reserves 003 for T-161 (shared companies, not started). T-161 takes ADR-005.
Code: open-ksef `main` `8f8a4d1` (#145), open-ksef-mobile `main` `c517ac3` + PR #89 head. Keycloak facts: docs 26.8.1 (ctx7 `/keycloak/keycloak/26.8.1`) and a run on stock `quay.io/keycloak/keycloak:26.8` (26.8.2), section "Verified".

## Context

- Since #145 the portal logs in with ROPC from its own form: `AuthProvider.tsx:100-106` (`signinResourceOwnerCredentials`). Register goes through the API and then ROPC (`AuthProvider.tsx:115-136`, `AccountController.cs:68`). Google uses `signinRedirect` with `kc_idp_hint=google` (`AuthProvider.tsx:108-113`).
- Mobile PR #89 restores the v1.0.8 in-app ROPC form (`AuthService.cs:83` on `tick/T-730-gate-browser-login`). Browser login and pairing sit behind `LoginViewModel.BrowserLoginEnabled = false` (`LoginViewModel.cs:10`).
- Direct grants are on for both public clients (`SystemSetupService.cs:323,336`). The realm policy (`SystemSetupService.cs:713-788`) sets no theme, OTP or flow settings. It runs on wizard and Settings save (`SystemSetupService.cs:160`, `SystemSettingsService.cs:147`), with the master admin token typed by the admin (`SystemSettingsController.cs:51,72`).
- Theme image: `FROM quay.io/keycloak/keycloak:26.0.8`, CSS only, `parent=keycloak.v2` (`infra/keycloak/Dockerfile:1`, `themes/openksef/login/theme.properties:1-4`). Prod runs the stock 26.0 image with `--features` (T-722 QA setup, CEO 2026-11-14).
- The portal already calls the Account REST API with its own token (`signOutAll.ts:6-19`). The account link opens the stock console (`Settings.tsx:33-41`).
- Admin gate: policy `openksef-admin` on the `realm_access` role (`Program.cs:121-125`, `SystemSettingsController.cs:12-17`). The `openksef-api` service account lacks `manage-users` and `view-realm` on wizard realms (T-722 QA D1). T-731 fixes this.
- CEO 2026-11-14: one look like one app, Keycloak invisible. TOTP for password logins only, not for Google. Recovery = recovery codes + admin reset. Pairing returns only when consent looks like the portal. Designer spec: `production/design/auth-screens.md` (21 pages, one `.auth-*` shell, §5 flags the ROPC + OTP conflict).

## Key decision: where the password and OTP are entered

| Flow | A. Own portal form (ROPC) + TOTP | B. Hosted Keycloak pages, pixel-identical (chosen) |
|---|---|---|
| Address bar | `/login` | `/auth/realms/openksef/...` on the same host |
| Password login | Form posts `grant_type=password`. Keycloak returns the same `invalid_grant "Invalid user credentials"` for a wrong password and a missing OTP (verified). The form cannot tell when to show the code field | `login.ftl` with the v0.0.23 markup, then `login-otp.ftl` |
| TOTP enrolment | Impossible. With `CONFIGURE_TOTP` pending, ROPC returns `Account is not fully set up` (verified). Flow enforcement is skipped by direct grant, so a user without OTP gets a token (verified). The other route is enrolment over the Admin API, which puts the secret in our code (REQ-0048 rule) | Forced by the browser flow: setup page, then recovery codes (verified) |
| OTP code | Passes through portal JS (`totp` parameter) | Never leaves Keycloak |
| Recovery codes | No direct-grant authenticator. Lost phone = locked out of the form | Alternative to OTP, "Użyj kodu zapasowego" (verified) |
| Google | Redirect, no OTP | The button on `login.ftl` goes to the broker, then Google, no OTP (verified). Same click count as today |
| Register | API `/account/register` + ROPC. The new user gets no TOTP | `register.ftl`. The registration flow forces TOTP setup (verified) |
| Password reset | Needs hosted pages anyway (e-mail link) | `login-reset-password.ftl`, then `login-update-password.ftl`, then OTP |
| Pairing `/device` | No SSO cookie after ROPC, so Keycloak asks for the password and OTP on its own page (ADR-002 D3) | The SSO cookie from the hosted login skips the password. Then consent, then result |
| Logout | `signoutRedirect`, no page | Same (T-722 QA 3c). `logout-confirm.ftl` themed as fallback |
| Pages to theme | All of B's pages except login and register, plus the ROPC form code kept | 21 pages, one shell |

A still needs every themed page in B, keeps ROPC and fails REQ-0048 (enrolment line 20, recovery line 23, no code in our JS line 28). **Decision: B.** The only visible change from today is the address bar. QA T-722 showed pixel-level comparison works (`magick compare` 0 px), so "identical" can be tested.

## Decision

1. **Login surface.** Portal and mobile log in on Keycloak pages through the gateway (`/auth/`), with code + PKCE. Portal `/login` becomes a redirect shim (`signinRedirect` with `returnTo`), as at `2b1b07c` (#144). The portal's ROPC, register form and `loginWithCredentials` are removed. `POST /api/account/register` stays for released apps until T-142.
2. **Theme tech: FreeMarker, `parent=base`.** No `keycloak.v2`/PatternFly CSS. `template.ftl` renders the §1 shell once, and each page template fills it. CSS = portal `components/ui.css` + `auth.css`, copied into the image at build time (one source). Fonts and favicon come from the portal. Image: `FROM quay.io/keycloak/keycloak:<26.8.x pinned>`, built against the T-341 version only.
3. **Account theme `openksef`** (`parent=keycloak.v3`). Its `index.ftl` redirects to `/settings/security`, which retires the stock console. Account REST is unaffected (verified). The `Settings.tsx:33-41` link is removed.
4. **Policy guard on one `GET /admin/serverinfo`.** `loginTheme`/`accountTheme=openksef` are set only when serverinfo lists the theme. The 2FA part is applied only when `systemInfo.version >= 26.4`; otherwise log a warning and skip. Stock image = built-in pages, same flows (ADR-002 D6 kept). The other `PolishLoginTexts` overrides stay, with the designer §5 copy. The stock 26.8.2 `pl` login bundle already has every OTP and recovery-code key, so none is added; page copy belongs to the theme templates.
5. **2FA realm config** (`EnsureRealmPolicyAsync`, idempotent by alias, partial PUTs). Upgraded realms keep their old flows (26.3 upgrade notes), so the policy creates its own:
   - OTP policy `totp`, `HmacSHA1`, 6 digits, 30 s, look-around 1.
   - `openksef browser` = copy of `browser`. In `forms`: `Username Password Form` REQUIRED; `2FA` CONDITIONAL (`Condition - user configured`, OTP Form ALT, Recovery Authentication Code Form ALT); `OTP if no 2FA` CONDITIONAL (`Condition - sub-flow executed` on `2FA`, `not-executed`; OTP Form REQUIRED). Bound as `browserFlow`.
   - `openksef registration` = copy of `registration` + OTP Form REQUIRED at the top level. Bound as `registrationFlow`.
   - `openksef reset credentials` = copy of `reset credentials`. In the conditional subflow, `Reset OTP` is replaced by OTP Form ALT + Recovery Code Form ALT. Otherwise an e-mail reset removes the OTP and logs the user in with mailbox access alone. Verified with mailpit (T-726): the e-mail link asks for the code first, then the new password; the OTP credential stays.
   - `CONFIGURE_RECOVERY_AUTHN_CODES` enabled (registered first when an upgraded realm lacks it). `CONFIGURE_TOTP` config `add-recovery-codes=true` (26.4+).
   - Idempotency: a copy is built only when its alias is missing or not bound to the realm. An unbound copy (half-built by a failed save, or unbound by hand) is deleted and rebuilt. A bound copy is never touched, so a second save changes nothing.
   - Google: no post-broker flow. The broker login never enters `forms` (verified). The first-broker re-authentication path asks OTP only when the user picks the password (built-in conditional 2FA).
   - Events: add `UPDATE_TOTP`, `REMOVE_TOTP`, `UPDATE_CREDENTIAL`, `REMOVE_CREDENTIAL` to `enabledEventTypes`. Expiry stays 30 d.
6. **Direct grants.** Direct grant keeps its built-in conditional OTP. Effect: an enrolled user cannot log in with ROPC without `totp`; an unenrolled user can (verified). Residual risk = today's, for unenrolled users only, until T-142. `openksef-portal-web` keeps direct grants for portal rollback and loses them in the T-142 step. **T-136 drops its line "Policy sets openksef-portal-web directAccessGrants=false"**: with #145 it would break the current portal.
7. **Portal account pages: Account REST from the SPA**, with the portal token (the same as `signOutAll.ts`). `GET {authority}/account/credentials`: password `createdDate`, `otp` present + `createdDate`, `recovery-authn-codes` `credentialData.remainingCodes`. `GET .../account/sessions/devices`: browser, OS, IP, lastAccess, clients (verified 200). `GET .../account/linked-accounts`: Google. Actions go through AIA `kc_action` (`UPDATE_PASSWORD`, `CONFIGURE_TOTP`, `CONFIGURE_RECOVERY_AUTHN_CODES`) on themed pages, back to `/settings/security` (`kc_action_status`). After a successful "Zmień aplikację", the portal deletes the older `otp` credential (`DELETE .../account/credentials/{id}`). Verify in T-725 that AIA adds a second OTP and that DELETE is allowed.
8. **Admin reset: own API on the Admin API**, `openksef-api` service account. **Deviation (T-731):** the wizard and Settings save grant no roles. Reset works only when the self-hoster granted `realm-management` `manage-users` to the service account by hand; the API reads the roles from the service-account token, the same check as the registration probe (`KeycloakUserService.HasRealmManagementRole`). Without the role the portal shows the Keycloak console steps. Contract below.
9. **Mobile.** The next app version sets `BrowserLoginEnabled = true` (Custom Tab, themed pages, pairing on) and drops the in-app password and register form (REQ-0049 "no password form in the app"). Released apps keep ROPC until T-142.
10. **Render test per page in CI.** The `keycloak` job builds the image, runs it with mailpit and a second realm as fake Google, seeds users (no OTP, OTP with a known secret), and walks every designer page and state (`production/design/auth/render.cjs` list, moved to `infra/keycloak/render-test/`). Checks: designer §4 items 1, 3, 8, 9, 11, 13, plus key element boxes ±4 px against the reference shots. It fails when a theme `.ftl` has no case. Runtime: `playwright-core` devDependency (axe-core is already in the portal) and runner Chrome.

## Admin reset contract (portal and backend build in parallel)

`SystemUsersController`, `[Route("api/system/users")]`, `[Authorize(Policy = SystemSettingsController.AdminPolicy)]`, `[EnableRateLimiting(SystemSettingsController.RateLimitPolicy)]`. Service account token, no master credentials in the body. The API never grants itself a role.

```
GET  /api/system/users/two-factor?email={email}
200  { "resetAvailable": true, "reason": null,
       "userId": "uuid", "firstName": "Anna", "lastName": "Nowak", "email": "anna@firma.pl",
       "status": "enabled" | "notSet" | "google", "recoveryCodesLeft": 11 | null, "enabledAt": "2026-11-20T10:00:00+00:00" | null }
200  { "resetAvailable": false, "reason": "manage_users_missing", ...user fields as above }   service account has view-users only
200  { "resetAvailable": false, "reason": "manage_users_missing", every user field null }     service account has neither role: no lookup possible
404  { "error": "user_not_found" }      lookup possible, no such e-mail
403  non-admin (policy)
503  { "error": "keycloak_unavailable" } no service account token or Keycloak error

POST /api/system/users/{userId}/two-factor/reset
204  removed every credential of type otp and recovery-authn-codes (idempotent; 204 also when none)
409  { "error": "reset_unavailable" }   service account lacks manage-users; portal shows the console steps
404  { "error": "user_not_found" }
403  non-admin
503  { "error": "keycloak_unavailable" }
```

- `resetAvailable` = the service-account token holds `realm-management` `manage-users`. `view-users` alone allows the lookup only.
- `status`: `google` = linked `google` identity and no `password` credential. Otherwise `enabled` if an `otp` credential exists, else `notSet`.
- Audit: table `AuditEvents` (`Id` uuid, `OccurredAt` timestamptz, `ActorUserId`, `ActorEmail`, `Action` = `TwoFactorReset`, `TargetUserId`, `TargetEmail`). One row per reset that removed at least one credential, plus a structured log line. No read endpoint; prod reads it through the read-only SQL helper. Migration `AddAuditEvents`: one new table, nothing else touched.
- Models: `Api/Models/TwoFactorUserResponse.cs`; portal `api/endpoints/system.ts`. Mobile: no change (ADR-004 C5/C6, portal-only).
- Console steps for the portal when `resetAvailable` is false: Keycloak admin, realm `openksef`, Users, the user, Credentials, delete `otp` and `recovery-authn-codes`. To enable reset in the portal: Clients, `openksef-api`, Service account roles, assign `realm-management` `manage-users` (it can reset any user's password; see `docs/login-page-setup.md`).

## Verified (2026-11-14, stock Keycloak 26.8.2, throwaway container, removed)

Scripts: `production/reports/T-723/keycloak-2fa-flows.mjs`, `keycloak-registration-otp.mjs`, `keycloak-account-redirect.mjs` (admin REST + headless Edge; fake Google = second realm as OIDC IdP).

| # | Case | Result |
|---|---|---|
| 1 | Password user, no OTP | `CONFIGURE_TOTP` page, then 12 recovery codes, then code returned |
| 2 | Password + OTP | OTP form with "Try another way", then code |
| 3 | Recovery code instead of OTP | Select method, then code page, then code; remaining 11/12 |
| 4 | Google first login (`kc_idp_hint`) | broker, then first-broker-login, then code; no OTP page |
| 5 | Google via button on login page | broker, then code; no OTP |
| 6 | ROPC: OTP user without `totp` / with / unenrolled / `CONFIGURE_TOTP` pending | 400 `Invalid user credentials` / 200 / 200 / 400 `Account is not fully set up` |
| 7 | Admin API credentials, then DELETE otp + recovery codes | `recovery-authn-codes` `{"totalCodes":12,"remainingCodes":11}`; next login forces setup again |
| 8 | Registration with `openksef registration` | Lands on TOTP setup before the code |
| 9 | Account theme `index.ftl` redirect | `/account/` HTML redirects; `/account/` and `/account/credentials` JSON unaffected; serverinfo lists `openksef` |

Not run in T-723: Account REST DELETE of an OTP credential, AIA re-enrol, and Polish copy. T-726 integration tests (`TwoFactorRealmTests`, `SystemUsersApiTests`, Keycloak 26.8.2 Testcontainers) cover cases 1-3, 4-5 (Google login and a password+OTP user with a linked Google identity), 6 (enrolled without `totp` 400, with 200, unenrolled 200), 7-8 via the admin reset API, the reset-credentials flow with mailpit, and the policy idempotency.

## Migration safety and release order

| Step | Content | Forward | Rollback | Prod data | Who |
|---|---|---|---|---|---|
| R2 v0.0.24 (T-413) | #145, no change here | as planned | as planned | none | orchestrator |
| R3 (T-136 minus portal direct grants) | Token exchange code removed | API image | previous image | none | orchestrator |
| R4 (T-341) | Prod Keycloak to `ghcr.io/open-ksef/openksef-keycloak:<tag>` FROM 26.8.x, `--features` dropped, in one stack edit. Theme inert (no `loginTheme`) | Back up the Keycloak DB. Keycloak migrates its schema on start | **Forward-only schema**: restore the dump + old image. No downgrade | realm and users migrated | **CEO**: stack edit |
| R5 auth v2 | Theme FTL, hosted portal login, account pages, admin reset, 2FA flows + enforcement, account console redirect, pairing card on (`pairing.ts:3`) | Back up both DBs. Order: Keycloak (new tag), API (migration `AddAuditEvents`, additive, startup migrator per ADR-004), Worker, gateway; Settings save once; check login, Google, `/device`, one render pass; then portal | Portal: previous image = ROPC form (portal direct grants still on). 2FA: in the console, bind `browser`, `registration`, `reset credentials` back to the built-ins; OTP credentials stay. Theme: previous image falls back to the built-in theme. API: previous image, table stays | `AuditEvents` 0 rows; Keycloak: 3 flows, required-action config, theme attributes | orchestrator; **CEO** if the prod stack pins the Keycloak tag; Settings save needs master credentials |
| Mobile vNext | `BrowserLoginEnabled = true`, in-app form off | Play internal on R5 day, production after T-517 | Previous build | none | CEO tags |
| T-142 | ROPC off on both public clients, register endpoint removed | as in T-142 | console: re-enable direct grants | none | orchestrator after the cut-off |

Released apps after R5: logged-in phones keep refreshing (REQ-0048). A new password login in v1.0.8 fails for enrolled users, and the app shows its wrong-password message. They need the new app or Google login. CEO Q2.

## Consequences

- The login address shows `/auth/...`. Everything else matches the v0.0.23 card (CEO Q1).
- 21 FTL templates track `base` templates. Each Keycloak minor bump must pass the render test before merge.
- The portal depends on the Account REST API, which the Keycloak docs do not cover. It is pinned by portal tests on recorded JSON and re-checked on every Keycloak bump.
- Self-hosters on Keycloak < 26.4 get no 2FA (logged). On the stock image they get the full flow without branding.
- Unenrolled users can still use ROPC without OTP until T-142.
- One prod stack edit at R4. R5 needs a Settings save on prod.

## Rejected

- A, own form + ROPC + TOTP: OTP in JS, no enrolment, no recovery codes, and hosted pages still needed (table above).
- Hybrid "portal form for the password, hosted OTP": ROPC creates no Keycloak auth session to continue from.
- Keycloakify: Maven/JDK in CI and a React runtime on the auth pages. The shell is CSS classes, so reusing React components gains little.
- CSS-only theme on `keycloak.v2`: CEO rejected it (2026-11-14).
- Own API for self-service 2FA/sessions: duplicates Account REST, and the Admin API has no browser/OS data for sessions.
- Keycloak admin events as the audit: the actor is the API service account, not the portal admin.
- `--features-disabled=account` to retire the console: leaves a Keycloak error page; the redirect lands in Settings.
- Gateway rule on `/account`: has to tell HTML from JSON on the same path; the theme redirect does not.
- `CONFIGURE_TOTP` as a realm default action: it would also hit Google-created users.
- Post-broker OTP for Google: against CEO Q3.
- Requiring OTP for every ROPC login now: it would lock unenrolled v1.0.8 users out before they can enrol.

## Hand-off

`dev-backend` (T-726, plus T-731 and T-136 first):
1. T-136 without the portal direct-grant line. T-341 to the pinned 26.8.x; move the integration fixture `TestcontainersFixture.cs:19` to the same version.
2. serverinfo guard; theme attributes; 2FA flows, OTP policy, required-action config, events (decision 5); `PolishLoginTexts` updates (designer §5 + missing 2FA keys).
3. `SystemUsersController` + `AuditEvents` entity, DbSet and migration, per the contract (with `resetAvailable`, no role grants).
4. Integration tests on the theme image: policy applied once and a second save is a no-op; cases 1-8 above; the reset-credentials flow with mailpit.

`dev-portal` (T-725):
1. Theme FTL for every designer page, account theme redirect, CSS copied from the portal in the image build.
2. Hosted login: Login.tsx/AuthProvider back to the `2b1b07c` shape; register and Google on Keycloak pages; ROPC removed.
3. Ustawienia > Konto / Bezpieczeństwo on Account REST + AIA; remove `Settings.tsx:33-41`; admin reset card on the contract; `PAIRING_CODE_ENABLED = true`.
4. Render test in the CI `keycloak` job (decision 10).

`dev-mobile` (new task): flag on, in-app form off, release after R5.
