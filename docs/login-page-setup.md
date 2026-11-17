# Login Page Setup

The portal signs in on the Keycloak pages (Authorization Code + PKCE via `oidc-client-ts`, [ADR-003](adr/adr-003-auth-pages-2fa.md)). A protected route redirects there and returns to the requested route; `/login` redirects too, and shows one button only after a session ended on a final 401. Login, registration, forgot password, 2FA (TOTP and recovery codes), Google, device pairing and required actions are Keycloak pages in the `openksef` theme: FreeMarker on `base`, the same `.auth-*` markup and CSS as the portal (`infra/keycloak`, image `openksef-keycloak`; the image build copies `ui.css`, `auth.css`, `index.css`, fonts, favicon and icons from `src/OpenKSeF.Portal.Web`). The account theme sends `/account` to the portal `Ustawienia > Bezpieczeństwo` at the `openksef-portal-web` client Base URL (set to `APP_EXTERNAL_BASE_URL` by the wizard and Settings save; relative path when empty), which reads the Account REST API and starts password, TOTP and recovery-code changes with `kc_action`.

- **Google sign-in** -- Keycloak shows the Google button when the `google` IdP is configured.
- **Registration** -- Keycloak register page when realm self-registration is on. `POST /api/account/register` stays for released mobile apps.
- **Render test** -- `node infra/keycloak/render-test/render.mjs <image> [outDir]` starts the image with mailpit, walks every theme page and state, and compares key element boxes with the designer mock (CI job `keycloak`).

## Google OAuth setup (optional)

Skip if you don't need Google login -- the button shows an error if not configured.

1. Go to [Google Cloud Console > Credentials](https://console.cloud.google.com/apis/credentials)
2. Create an **OAuth 2.0 Client ID** (type: Web application)
3. Add authorized redirect URI: `{APP_EXTERNAL_BASE_URL}/auth/realms/openksef/broker/google/endpoint`
   - Local dev with ngrok: `https://<ngrok-id>.ngrok-free.app/auth/realms/openksef/broker/google/endpoint`
   - Local dev without ngrok: `http://localhost:8080/auth/realms/openksef/broker/google/endpoint`
4. Set in `.env`:

```
GOOGLE_CLIENT_ID=123456789-abcdef.apps.googleusercontent.com
GOOGLE_CLIENT_SECRET=GOCSPX-xxxxxxxxxxxxxxxxxxxxxxxx
```

5. Restart Keycloak: `docker compose -f docker-compose.dev.yml restart keycloak`

## Registration endpoint

`POST /api/account/register` works only after the manual steps below. The setup wizard does not do them, on purpose: `manage-users` lets whoever holds the `openksef-api` secret reset the password of any user, and the wizard's copy of that secret is in the database and its backups. On a wizard install registration therefore stays off. Use Google sign-in or create the account in the Keycloak console.

The portal and the mobile app hide the register link until registration works: each wizard run and Settings save checks that realm registration is on and that the service account gets a token, reads the realm and holds `manage-users`, and stores the result. After the manual steps below, open Settings and save once.

Every request reads the realm, with no cache: registration off returns 403, a failed read returns 503, and no user is created in either case.

New users are created with `emailVerified=false`. With realm **Verify email** on, they get the `VERIFY_EMAIL` required action and a verification email, and password login stays blocked until they confirm it.

1. Open Keycloak admin: `https://<your-host>/auth/admin` (dev: http://localhost:8082/auth/admin, admin / admin)
2. Go to **Clients > openksef-api > Service accounts roles**
3. Assign `realm-management` > `manage-users` (creates the user) and `view-realm` (reads the realm registration and verify-email policy)
4. With realm **Verify email** on, set **Realm settings > General > Frontend URL** to the public URL, `https://<your-host>/auth`. Otherwise the confirmation link in the email is unreachable and the user cannot log in. The API container must be able to reach this URL, because Keycloak then publishes its signing keys there. If it cannot, every authenticated API call returns 401 from the next API start. Check before you restart, and remove the Frontend URL to recover.
5. Open **Ustawienia** in the portal and save once.

A wizard install needs no `.env` change and no API restart: the API uses the client secret the wizard stored in `SystemConfigs`. Do not regenerate the secret in **Clients > openksef-api > Credentials**: the stored copy then stops working and registration fails. Only an install without the wizard sets `API_CLIENT_SECRET` in `.env` to that secret, then runs `docker compose up -d api` (`restart` does not reread `.env`).

The same `manage-users` role enables **Ustawienia > Administracja > Resetuj weryfikację dwuetapową**. Without it the reset returns `409` `reset_unavailable` and the portal shows the Keycloak console steps; the API never grants the role itself.

Without these roles, Settings save keeps registration off and register returns `403` `registration_disabled`. Roles removed after the last save give `503 Service Unavailable`. Login and Google sign-in work independently.
