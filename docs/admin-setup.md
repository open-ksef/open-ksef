# Admin Setup Wizard

The admin setup wizard automates first-time system configuration. Instead of manually editing `.env` files, copying Keycloak secrets, and restarting containers, an admin runs `docker-compose up` and walks through a web-based wizard at `http://localhost:8080/admin-setup`.

## Prerequisites

- Docker and Docker Compose installed
- Ports 8080, 8081, 8082 available (or configure alternatives in `.env`)

## Quick Start

```bash
# 1. Start the stack
docker compose up -d

# 2. Open the portal in your browser
# http://localhost:8080
# The system detects it's not initialized and redirects to /admin-setup

# 3. Follow the wizard steps (see below)

# 4. After setup completes, log in with the admin account you created
```

## Wizard Steps

### Step 1: Keycloak Admin Authentication

Enter the Keycloak admin credentials (default: `admin` / `admin` from `docker-compose.yml`). The wizard authenticates against the Keycloak master realm and receives a short-lived setup session token (10 minutes).

### Step 2: Base Configuration

| Field | Description | Default |
|-------|-------------|---------|
| External URL | Public URL where the system is accessible | Current browser origin |
| KSeF Environment | Test or Production | Test (`ksef-test.mf.gov.pl`) |
| Admin email | Email for the first OpenKSeF user | (required) |
| Admin password | Password for the first user | (required, min 8 chars) |
| First name / Last name | Optional user details | Admin |

### Step 3: Authentication & Email

Configure Keycloak realm settings:

**Login policy:**
- Allow self-registration (default: ON)
- Require email verification (default: OFF, requires SMTP)
- Allow login with email (default: ON)
- Allow password reset (default: ON, requires SMTP)

**Password policy:**
- Basic: minimum 8 characters
- Strong: 12 characters, special chars, uppercase, digits

**SMTP (optional):**
If you enable email verification or password reset, SMTP is required. The wizard supports common providers:

| Provider | Host | Port | TLS |
|----------|------|------|-----|
| Gmail | smtp.gmail.com | 587 | StartTLS |
| Outlook/O365 | smtp.office365.com | 587 | StartTLS |
| Custom | (manual) | (manual) | (manual) |

### Step 4: Security

The wizard fetches the **API client secret** from the Keycloak `openksef-api` client and stores it in the database.

The **AES-256 encryption key** is not part of the wizard. Set `ENCRYPTION_KEY` (or `ENCRYPTION_KEY_FILE`) on the API and Worker before you start the stack, and back it up outside the database. The wizard refuses to apply without it. Shipped `docker-compose.yml` forwards only `ENCRYPTION_KEY` and refuses to start without it, so `ENCRYPTION_KEY_FILE` needs your own compose file. An override file alone is not enough, because the check in the base file still runs. See [Encryption key](#encryption-key) for the upgrade and rotation steps.

### Step 5: Optional Integrations

**Google OAuth** (optional): If you want Google sign-in, create a Google Cloud OAuth 2.0 Client ID and enter the credentials. The redirect URI is pre-filled.

**Push Notifications**: Three modes available:

- **Relay OpenKSeF (default, recommended)** -- Uses the team-operated relay server to deliver push notifications via FCM/APNs. No Firebase setup needed. The relay URL (`https://push.open-ksef.pl`) is pre-filled. Just leave it ON.
- **Own Firebase project (advanced)** -- Paste your own Firebase service account JSON for direct FCM delivery. See [push-notifications-setup.md](push-notifications-setup.md) for details.
- **Local only (SignalR)** -- No remote push. Users receive notifications only when the mobile app is actively connected to the server.

SignalR local push is always active regardless of which mode you choose. The mode only affects remote (background) delivery.

### Step 6: Summary & Apply

Review all settings and click "Apply". The wizard:
1. Fetches the Keycloak API client secret
2. Enables token-exchange for the service account
3. Updates client redirect URIs with the external URL
4. Configures the Keycloak realm (login policy, password policy, SMTP)
5. Creates the admin user in Keycloak
6. Configures Google IdP (if provided)
7. Stores all configuration in the database
8. Redirects to the login page

## Encryption key

The key encrypts stored KSeF tokens and certificates. Losing it loses them: tenants must enter them again.

### Upgrade from v0.0.7 or earlier

Up to v0.0.7 the key lives in the database next to the ciphertext, so every database dump can decrypt the tokens. The first start with a valid configured key re-encrypts all credentials and deletes the database row, in one transaction ("adoption").

If you pull the new images and set no key, nothing changes: the app keeps the database key and logs an Error at each start.

1. Stop both hosts: `docker compose stop api worker`. Stop both, because compose starts them in no fixed order. An old API that still serves during adoption writes under the old key, and that credential is lost.
2. Back up: `docker compose exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -Fc openksef' > before-key-move.dump`. This dump holds the old key and the ciphertext together, so protect it.
3. Generate the key: `openssl rand -base64 32`. It must have 44 characters. **Store it in a password manager before the next step**, outside the database and its backups. Do not reuse an older `ENCRYPTION_KEY` value.
4. Set `ENCRYPTION_KEY` to the new value in `.env` or the stack environment.
5. Start the new images: `docker compose pull api worker portal-web`, then `docker compose up -d`.
6. Verify:
   - One host logs `Encryption key moved out of the database`. The other may log `moved out of the database by another host`. Neither logs Critical.
   - `SELECT count(*) FROM "SystemConfigs" WHERE "Key"='encryption_key'` returns 0.
   - One manual sync succeeds.

A Critical log that names a credential means it could not be decrypted, and nothing changed. Delete or re-enter that credential in the portal and restart.

**Rollback**

| Situation | Result |
|---|---|
| Row still present, back to the previous image | Works, nothing changed |
| Row gone, previous image, `ENCRYPTION_KEY` kept | Works: the previous image reads the key from the environment when no row exists |
| Row gone, previous image, key only in `ENCRYPTION_KEY_FILE` | Credential use fails: the previous image does not read that file. Set `ENCRYPTION_KEY` to the same value |
| Row gone, previous image, `ENCRYPTION_KEY` reverted or emptied | Hosts run, credential use fails with logs, no data changes. Restore the new key. **Do not revert the key** |
| New key lost | Current ciphertext cannot be recovered. Tenants re-enter tokens, or restore the dump from step 2 and lose later changes |
| Dump from step 2 restored | Row and old ciphertext return. The next start adopts again |

Dumps taken before adoption can decrypt the tokens in them until you delete those dumps.

### Rotate the key

Run this on a stack that already runs the current images and has adopted: the row count query above returns 0.

1. Generate the new key: `openssl rand -base64 32`. **Store it in a password manager before you run anything.** Keep the old key until step 6 succeeds.
2. Stop both hosts: `docker compose stop api worker`.
3. Back up, as in the upgrade.
4. Re-encrypt, with `ENCRYPTION_KEY` still on the old key. The new key is read from the environment and is never an argument:
   ```
   read -s ENCRYPTION_KEY_NEW && export ENCRYPTION_KEY_NEW
   docker compose run --rm -e ENCRYPTION_KEY_NEW api rotate-encryption-key
   unset ENCRYPTION_KEY_NEW
   ```
   It prints `Re-encrypted N credentials.` and exits 0. A re-run is safe and reports 0.
5. Set `ENCRYPTION_KEY` to the new value in `.env` or the stack environment, then `docker compose up -d`.
6. Verify: no Critical in the logs and one manual sync succeeds. Only then discard the old key.

The command exits 1 and writes nothing when:

- `ENCRYPTION_KEY_NEW` is missing, malformed or equal to the current key, or the current key is missing or malformed.
- Other clients with no application name are connected to the database. This catches a running Worker and a busy API. An API idle for several minutes holds no connection and is not caught, so always do step 2. Tools that set a name (`psql`, `pg_dump`, pgAdmin) and autovacuum do not count. Close any other client the message counts.
- The database is not on this image's version, or the key is still in the database. Start the app once, stop it and retry.
- A credential cannot be decrypted. The message names it.

**Rollback**

| Situation | Action |
|---|---|
| Command refused | Nothing changed. Fix the reason and retry |
| After step 4, problem before step 6 | Rotate back with `ENCRYPTION_KEY` set to the new key and `ENCRYPTION_KEY_NEW` to the old key, or restore the dump from step 3 and keep the old key |
| New key lost after step 4 | Restore the dump from step 3 with the old key, and lose later changes. Without the old key, tenants re-enter tokens |

## Architecture

### Configuration Storage

Configuration is stored in the `system_config` database table (key-value store). The `ISystemConfigService` reads from this table with an in-memory cache, falling back to environment variables if a key isn't in the database.

Priority: **Database > Environment variable**

This means you can still override any setting via environment variables in `docker-compose.yml` if needed.

### API Endpoints

| Endpoint | Auth | Purpose |
|----------|------|---------|
| `GET /api/system/setup-status` | Anonymous | Returns `{isInitialized: bool}` |
| `POST /api/system/setup/authenticate` | Anonymous | Validates KC admin creds, returns setup token |
| `POST /api/system/setup/apply` | Setup token (X-Setup-Token header) | Executes full provisioning |

All setup endpoints are blocked after the system is initialized (return 403).

### Portal Routing

The portal checks `/api/system/setup-status` on every protected page load. If `isInitialized` is `false`, all routes redirect to `/admin-setup`. After setup completes, the wizard redirects to `/login`.

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Wizard doesn't appear | Check that `system_config` table is empty. Delete rows and restart the API. |
| Keycloak auth fails | Verify Keycloak is running (`docker compose logs keycloak`). Default creds: admin/admin. |
| Apply fails with 500 | Check API logs (`docker compose logs api`). Common: Keycloak realm not fully imported yet. |
| SMTP test email fails | Verify SMTP credentials. Gmail requires an App Password (not regular password). |
| Redirect loop after setup | Clear browser cache/cookies. The portal caches setup status for 60 seconds. |
| Need to re-run wizard | Delete all rows from `system_config` table and restart the API container. |

## Development

For local development against `docker-compose.dev.yml`:

```bash
# Start fresh (no dev-env-up.ps1 provisioning)
docker compose -f docker-compose.dev.yml up -d --build

# Open http://localhost:8080 -> wizard appears
# Walk through the wizard

# Run E2E tests for the wizard
dotnet test src/OpenKSeF.Portal.E2E -nologo --filter "FullyQualifiedName~AdminSetup"

# Run integration tests (requires Docker for Testcontainers)
dotnet test src/OpenKSeF.Api.IntegrationTests -nologo --filter "FullyQualifiedName~SystemSetup"
```
