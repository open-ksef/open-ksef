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

`POST /api/account/register` needs the `openksef-api` service account with `realm-management` roles `manage-users` and `view-realm`. The setup wizard and every Settings save grant them, and the client secret comes from the wizard's stored value or, without it, from `API_CLIENT_SECRET`. The steps below are only for a stack without the wizard.

Every request reads the realm, with no cache: registration off returns 403, a failed read returns 503, and no user is created in either case.

New users are created with `emailVerified=false`. With realm **Verify email** on, they get the `VERIFY_EMAIL` required action and a verification email, and password login stays blocked until they confirm it.

1. Start the Docker stack: `./scripts/dev-env-up.ps1`
2. Open Keycloak admin: http://localhost:8082/auth/admin (admin / admin)
3. Go to **Clients > openksef-api > Credentials** tab
4. Copy the **Client Secret** (regenerate if needed)
5. Set in `.env`:

```
API_CLIENT_SECRET=<paste-secret-here>
```

6. Ensure the service account has the roles:
   - **Clients > openksef-api > Service Account Roles**
   - Assign `realm-management` > `manage-users` and `view-realm`
7. Restart API: `docker compose -f docker-compose.dev.yml restart api`

Without a client secret or these roles, registration returns `503 Service Unavailable`. Login and Google sign-in work independently.
