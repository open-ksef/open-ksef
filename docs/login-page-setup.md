# Login Page Setup

The portal `/login` page authenticates directly against Keycloak (no redirect). Three flows:

- **Username/password** -- ROPC grant via `oidc-client-ts`
- **Google sign-in** -- Keycloak IdP brokering (`kc_idp_hint=google`)
- **Registration** -- `POST /api/account/register` (Keycloak Admin API), then auto-login via ROPC

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

1. Start the Docker stack: `./scripts/dev-env-up.ps1`
2. Open Keycloak admin: http://localhost:8082/auth/admin (admin / admin)
3. Go to **Clients > openksef-api > Credentials** tab
4. Copy the **Client Secret**. Do not regenerate it on a wizard install: the API prefers the wizard's copy in `SystemConfigs` over `API_CLIENT_SECRET`, and registration then fails.
5. Set in `.env`:

```
API_CLIENT_SECRET=<paste-secret-here>
```

6. Ensure the service account has the roles:
   - **Clients > openksef-api > Service accounts roles**
   - Assign `realm-management` > `manage-users` and `view-realm`
7. With realm **Verify email** on, set **Realm settings > General > Frontend URL** to the public URL, `https://<your-host>/auth`. Otherwise the confirmation link in the email is unreachable and the user cannot log in. The API container must be able to reach this URL, because Keycloak then publishes its signing keys there. If it cannot, every authenticated API call returns 401 from the next API start. Check before you restart, and remove the Frontend URL to recover.
8. Restart API: `docker compose -f docker-compose.dev.yml up -d api`. `restart` does not reread `.env`.

The same `manage-users` role enables **Ustawienia > Administracja > Resetuj weryfikację dwuetapową**. Without it the reset returns `409` `reset_unavailable` and the portal shows the Keycloak console steps; the API never grants the role itself.

Without `API_CLIENT_SECRET` or these roles, Settings save keeps registration off and register returns `403` `registration_disabled`. Roles removed after the last save give `503 Service Unavailable`. Login and Google sign-in work independently.
