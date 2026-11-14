# ADR-002: Keycloak-hosted login with code+PKCE, device-grant pairing

Status: accepted (2026-11-03), amended 2026-11-13 (M2 plan: decisions 4, 8, 9, deploy order, consequences), amended 2026-11-14 (T-722, CEO): decision 1 suspended, portal keeps its own ROPC /login form; the realm policy does not set loginTheme; pairing card hidden in the portal.

## Context

- Portal and mobile log in with ROPC on public clients (`AuthProvider.tsx:100-134`, `SystemSetupService.cs:323,336`). ROPC bypasses required actions and login-page protections, and it sets no browser session.
- QR setup-token login never worked and relied on realm-wide impersonation (`AccountController`, `KeycloakTokenExchangeService`). CEO decision 2026-10-28: replace both with hosted login and device pairing. CEO 2026-11-03: pairing goes ahead of the rest of M4, and hosted login with a theme ships with it.
- Keycloak is 26.0.8 (`docker-compose.yml:26`). Its Polish bundle has no `oauth2Device*` keys and lacks 180 of 460 login keys. Keycloak patches only its newest line, so 26.0.8 has had no fixes since 26.1 shipped.
- Settings save already reconciles the realm: `EnsureApiAudienceMappersAsync` (#138) runs from the wizard and from every Settings save.

## Decision

1. **Portal:** Authorization Code + PKCE S256 on a Keycloak page with the `openksef` theme. No credential form in the SPA. Register, forgot password, Google and required actions are served by Keycloak.
2. **Pairing:** RFC 8628 device grant with PKCE on `openksef-mobile`. The phone shows `user_code`, the user enters it on `/mobile-setup`, the portal sends the browser to `{authority}/device?user_code=`, the user approves on the consent screen, the phone polls for offline tokens. Pairing binds a device to a Keycloak user, never to a company.
3. **No password on `/device` needs a Keycloak browser session.** ROPC sets no cookie, so a user with a portal session from the old form is asked once. The prompt also returns after SSO idle (30 min) or max (10 h).
4. **Realm config in code.** `SystemSetupService.EnsureRealmPolicyAsync`, a static with the same shape as `EnsureApiAudienceMappersAsync`, is called by the setup wizard after `ConfigureRealmAsync` and by every Settings save right after the mappers, outside the `ExternalBaseUrl` branch. Idempotent, partial PUTs only. Existing installs get it on the next Settings save. No API startup reconciler, and no separate policy class: two callers of one static is the house pattern.
5. **Locale:** `supportedLocales=["pl"]`, `defaultLocale="pl"`. The product and its docs are Polish, and this removes the English path past the warning.
6. **Text that must survive an image rollback lives in the realm.** Device and consent strings are realm localization overrides, set by the policy. They work on the stock image and win over theme messages. Everything else on the login pages lives in the theme.
7. **Theme:** `infra/keycloak`, `FROM quay.io/keycloak/keycloak:26.0.8`, `parent=keycloak.v2`. CSS, logo and `messages_pl.properties` only. No FreeMarker overrides, so an upgrade does not need a template merge. Tokens are copied from `design-system.md` section 1, light and dark.
8. **Removal is staged.** The setup-token code goes with pairing (R2). No code uses token exchange after that, so `--features` also leaves the Keycloak command in R2. Portal direct grants and client tightening go one release later (R3, T-136). Mobile ROPC stays until T-142.
9. **Keycloak upgrade is a separate release** (T-341), after `--features` is gone. Timing is a pending CEO decision (M2 plan Q1); the recommendation is a release a few days after R2 runs without problems.

## Deploy order

| Release | Content | Operator steps |
|---------|---------|----------------|
| R1 | T-404, T-399, and T-094 if merged. No realm change | Normal upgrade |
| R2 | Realm policy, 300 s access token, theme image, hosted login, pairing field, setup-token code removed, `--features` removed | See below |
| Mobile | App with pairing | Store release after R2. An app on a server without the grant gets `unauthorized_client` and reports that pairing is not supported |
| R3, at least 14 days after R2 with no rollback | T-136: portal direct grants off, client tightening | Settings save |
| R4 | Keycloak upgrade (T-341) | Own runbook |
| T-142 | Mobile ROPC off, after its cut-off criterion | Settings save |

R2 order, each step checked before the next:

1. Back up the app and Keycloak databases.
2. Keycloak service to `ghcr.io/open-ksef/openksef-keycloak:${OPENKSEF_VERSION:-latest}`, and drop `--features` from its command. Same version, no schema change. The theme is inert until the realm points at it.
3. API, Worker and gateway to `<tag>`.
4. Settings save once. Device grant, locale, strings, theme, events and the 300 s access token are now on.
5. Check by direct URL: `/auth/realms/openksef/.well-known/openid-configuration` lists `device_authorization_endpoint`, and `/auth/realms/openksef/account/` shows the themed Polish login.
6. Portal to `<tag>`. Users now get hosted login.

T-399 must be live before step 4, because the grant creates sessions that only it can end from the portal. That is why it ships in R1.

## Rollback

| What | How | Effect |
|------|-----|--------|
| Hosted login back to the form | Previous portal image only | Works until R3, because portal direct grants stay on. Sessions stay valid |
| Keycloak image back with `loginTheme=openksef` still set | Previous image, no realm step | Built-in theme is used, with `ERROR Failed to find LOGIN theme openksef` in the log on each render. Sessions and strings stay. Optional: set the theme to `keycloak.v2` to silence the log |
| Pairing off | Turn off OAuth 2.0 Device Authorization Grant on `openksef-mobile` in the console | New pairing returns `unauthorized_client`. Paired phones stay until sign-out-all |
| API | Previous image | Realm settings are inert. Setup-token routes return, with no caller. The R1 API needs no token exchange to run, so `--features` can stay off |
| Access token lifespan | Set **Access Token Lifespan** back to 30 min in the realm console | Revoked sessions keep a working access token for up to 30 min again |
| R3 | Previous images, re-enable portal direct grants in the console | As before R3 |

No schema migration in the app DB. The Keycloak DB gets realm attributes, localization rows and event rows (30 d).

## Existing sessions at cutover

- Portal sessions from the form stay valid until the SSO limits. Their refresh tokens keep working, even with direct grants off. They have no cookie, so `/device` asks for the password once, then the session exists.
- Portal tokens are in `sessionStorage`. A new tab goes through Keycloak, and is not prompted if the SSO session is live.
- Released apps with ROPC, Google and offline tokens are unaffected until T-142.
- `offlineSessionMaxLifespan` of 365 d starts to apply to existing offline sessions, counted from their start.

## Self-hosting

Take the new `docker-compose.yml` (Keycloak image and command change), `docker compose pull`, `docker compose up -d`, then save Settings once in the portal.

- Without the Settings save: login works but in English, and pairing reports that the server does not support it.
- With the old Keycloak image: everything works, with the stock look and the log line above.
- No proxy change: all pages stay under `/auth/`.
- Without SMTP, forgot password stays hidden, as before.
- Fresh installs get all of this from the wizard.

## Device-code phishing

An attacker can start a device request and send a victim `/device?user_code=...`. That link skips the portal page and its warning. With a live SSO session it takes one click, and the attacker gets an offline token (30 d idle, 365 d max) that can issue invoices.

Controls: forced consent on every device verification, the warning in the consent text in the realm, 300 s code lifespan, offline cap, sign-out-all, device events kept 30 d. The phone shows the code only, never a link or QR of it.

Accepted residual risk: a user who approves anyway. Review before the first third-party tenant.

## Consequences

- Login page changes for every user at R2. The Keycloak page is a product surface and needs an image built by CI.
- The login page will not match the portal until redesign B ships. Both follow `design-system.md`, and neither blocks the other.
- About 600 lines leave the codebase over R2 and T-142.
- Pairing is more typing than a QR, and consent shows every time.
- Access tokens live 5 minutes (T-400), so a revoked session keeps API access for at most 5 minutes. The API accepts 60 s clock skew. Clients refresh more often.
- 26.0.8 stays until R4.

## Rejected

- Pairing first, hosted login later: CEO decision against it.
- All steps in one release: portal rollback would also need console changes, and the login surface would change with client tightening.
- Device strings in the theme: lost on image rollback, absent on a stock image.
- `supportedLocales` with `en`: English browsers see no warning. Translating it is more text for no user.
- Consent template override to show the code: the code is not in the template model, and templates would block upgrades.
- Removing `?user_code=` prefill: does not stop an attacker's link, and adds typing.
- Password on every `/device`: reverses the CEO decision.
- Alert on new pairing: no SMTP on the hosted instance, and an event listener needs Java.
- API realm reconciler at startup: needs `manage-realm` on the API secret.
- A `KeycloakRealmPolicy` class: one more type for the same two callers; moving the mappers and brute-force settings into it is churn.
- Keycloak upgrade in R2: stacks a second login risk on the same release.
- Volume-mounted theme on prod: no checkout there. An image follows the gateway pattern.
- Keep ROPC forms hardened; WebView login; reverse device flow; API wrapper for approval: as in the T-091 design.
