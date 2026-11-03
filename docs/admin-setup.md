# Admin Setup Wizard

The admin setup wizard automates first-time system configuration. Instead of manually editing `.env` files, copying Keycloak secrets, and restarting containers, an admin runs `docker-compose up` and walks through a web-based wizard at `http://localhost:8080/admin-setup`.

## Prerequisites

- Docker and Docker Compose installed
- Ports 8080, 8081, 8082 available (or configure alternatives in `.env`)
- `ENCRYPTION_KEY` set, see [Encryption key](#encryption-key)

## Quick Start

New installations only. On an existing installation do not run these steps: follow the [upgrade procedure](#upgrade-from-v007-or-earlier).

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

> **Check `ENCRYPTION_KEY` before you upgrade.** Before any `docker compose pull`, `docker compose up -d`, stack update or automatic updater run, check `ENCRYPTION_KEY` in `.env` or the stack environment. If it holds a valid value, the first start of the new images moves the key to that value ("adoption"), with no stop and no backup. Follow the [upgrade procedure](#upgrade-from-v007-or-earlier) first.
>
> Already adopted (the query in its step 1 returns 0)? Keep that value, because it is the only copy of the key, then [rotate the key](#rotate-the-key). Is the value empty? Do not generate a key and do not run `docker compose up -d`. Step 1 of the procedure says where to recover it.

The key encrypts stored KSeF tokens and certificates. Losing it loses them: tenants must enter them again.

A valid key is 32 bytes in base64, which is 44 characters. Generate it with one of these commands:

```bash
openssl rand -base64 32
# or, without openssl, with Docker alone:
docker run --rm postgres:16-alpine sh -c "head -c 32 /dev/urandom | base64"
```

The commands in the procedures below are written for bash. On Windows use WSL or Git Bash. PowerShell corrupts a database dump when it redirects with `>`.

### What happens if you only pull the new images

Up to v0.0.7 the key lives in the database next to the ciphertext, so every database dump can decrypt the tokens. The new images change that, but only once you provide a key:

| State after the upgrade | Result |
|---|---|
| New images, row in the database, `ENCRYPTION_KEY` empty | Legacy mode. The app keeps the database key, nothing is lost, and each start logs an Error |
| New images, row in the database, `ENCRYPTION_KEY` malformed (wrong format or length) | The same: legacy mode and an Error at each start |
| New images, row in the database, `ENCRYPTION_KEY` valid | Adoption at the first start, to exactly this value. Make sure it is stored outside the database, and follow the procedure below **before** you pull the images |
| New `docker-compose.yml` without `ENCRYPTION_KEY` | `docker compose` refuses and names the command that generates a key. `stop`, `exec`, `ps`, `logs` refuse unless the project name is set. `up`, `pull`, `run` always refuse. Running containers stay untouched |

Legacy mode is temporary: later releases are planned to stop reading the key from the database. Adopt as soon as you can.

### Upgrade from v0.0.7 or earlier

The first start with a valid configured key re-encrypts all credentials and deletes the database row, in one transaction.

Run steps 1 to 3 with the compose file you already use. The new `docker-compose.yml` arrives at step 6.

`docker compose` refuses to run (old file overwritten or `.env` lost)? Do not add the key to get past it: one `up` adopts with no backup. Run steps 1 to 3 with Docker alone, then continue from step 4. Run one command at a time. The first is step 1: on a result of `0`, stop as that step says. The container names are fixed:

```bash
echo "SELECT count(*) FROM \"SystemConfigs\" WHERE \"Key\"='encryption_key';" | docker exec -i openksef-postgres sh -c 'psql -U "$POSTGRES_USER" -d openksef -tA'
```

Continue only if the result is `1`:

```bash
docker stop openksef-api openksef-worker
docker exec openksef-postgres sh -c 'pg_dump -U "$POSTGRES_USER" -Fc openksef' > before-key-move.dump
docker exec -i openksef-postgres pg_restore -l < before-key-move.dump > /dev/null; echo "exit=$?"
```

1. **Check that the key is still in the database:**
   ```bash
   echo "SELECT count(*) FROM \"SystemConfigs\" WHERE \"Key\"='encryption_key';" | docker compose exec -T postgres sh -c 'psql -U "$POSTGRES_USER" -d openksef -tA'
   ```
   Continue only if the result is `1`. A result of `0` means the stack has already adopted, and `ENCRYPTION_KEY` is the only copy of the key. **Stop this procedure, do not change `ENCRYPTION_KEY`.** If `ENCRYPTION_KEY` is empty, **do not generate a key and do not run `docker compose up -d`**. Restore the previous value. If you have no copy, the containers still hold it: `docker inspect openksef-api | grep '"ENCRYPTION_KEY='`. This command prints a secret to the terminal. If it shows no value (`"ENCRYPTION_KEY="` or no such container), run it on `openksef-worker`. If that shows none either, no copy remains. Only then generate a key, set it, run `docker compose up -d` and follow the `New key lost` row under Rollback below. `does not decrypt` stays in the logs until every credential is re-entered. Once you have the key, go to [rotation](#rotate-the-key).
2. **Stop both hosts:** `docker compose stop api worker`. Stop both, because compose starts them in no fixed order. An old API that still serves during adoption writes under the old key, and that credential is lost.
3. **Back up:** `docker compose exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -Fc openksef' > before-key-move.dump`. This dump holds the old key and the ciphertext together, so protect it. Verify it: `docker compose exec -T postgres pg_restore -l < before-key-move.dump > /dev/null; echo "exit=$?"` must print `exit=0`.
4. **Generate the key:** `openssl rand -base64 32`, or the Docker command above. It must have 44 characters. **Store it in a password manager before the next step**, outside the database and its backups. Do not reuse an older `ENCRYPTION_KEY` value.
5. **Set the key:** copy the current `ENCRYPTION_KEY` value, if one is set, to a password manager before you overwrite it. Then put the new value in `ENCRYPTION_KEY` in `.env` or the stack environment.
6. **Start the new images:** replace `docker-compose.yml` with the new file, then `docker compose pull api worker portal-web gateway` and `docker compose up -d`.
7. **Verify:**
   - In `docker compose logs api worker` one host logs `Encryption key moved out of the database`. The other logs nothing about it. Neither logs Critical.
   - The query from step 1 returns 0.
   - One manual sync succeeds: **Dane logowania** (Credentials) > the company row > **Wymuś synchronizację** (Force sync).

The dump from step 3 is now the only copy of the old key. Keep it at least until a sync succeeds. Dumps taken before adoption can decrypt the tokens in them until you delete those dumps.

A Critical log that names a credential means it could not be decrypted, and nothing changed. Delete or re-enter that credential in the portal and restart. If the log says `does not decrypt`, restore the key and delete nothing. A Critical log that names no credential means nothing changed: restart.

**Rollback**

Previous images carry a release tag, e.g. `:v0.0.7`.

| Situation | Result |
|---|---|
| Row still present, back to the previous image | Works, nothing changed |
| Row gone, previous image, `ENCRYPTION_KEY` kept | Works: the previous image reads the key from the environment when no row exists |
| Row gone, previous image, key only in `ENCRYPTION_KEY_FILE` | Credential use fails: the previous image does not read that file. Set `ENCRYPTION_KEY` to the same value |
| Row gone, previous image, `ENCRYPTION_KEY` reverted or emptied | Hosts run, credential use fails with logs, no data changes. Restore the new key. **Do not revert the key** |
| New key lost | Current ciphertext cannot be recovered. Tenants re-enter tokens, or restore a dump from before adoption and lose later changes |
| Dump from step 3 restored | Row and old ciphertext return. The next start adopts again |

To restore a dump, stop both hosts first, then run `docker compose exec -T postgres sh -c 'pg_restore -U "$POSTGRES_USER" -d openksef --clean --if-exists' < before-key-move.dump`.

### Rotate the key

Run this on a stack that already runs the current images and has adopted: the query from step 1 of the upgrade returns 0.

1. Generate the new key: `openssl rand -base64 32`, or the Docker command above. **Store it in a password manager before you run anything.** Keep the old key until step 6 succeeds.
2. Stop both hosts: `docker compose stop api worker`.
3. Back up and verify the dump, as in the upgrade, but to `before-rotation.dump`. Do not overwrite `before-key-move.dump`.
4. Re-encrypt, with `ENCRYPTION_KEY` still on the old key. The new key is read from the environment and is never an argument:
   ```bash
   read -rsp 'New key: ' ENCRYPTION_KEY_NEW && echo && export ENCRYPTION_KEY_NEW
   docker compose run --rm -e ENCRYPTION_KEY_NEW api rotate-encryption-key
   unset ENCRYPTION_KEY_NEW
   ```
   It prints `Re-encrypted N credentials.` and exits 0. A re-run is safe and reports 0.
5. Set `ENCRYPTION_KEY` to the new value in `.env` or the stack environment, then `docker compose up -d`.
6. Verify: no Critical in the logs and one manual sync succeeds. Only then discard the old key.

The command exits 1 and writes nothing when:

- `ENCRYPTION_KEY_NEW` is missing, malformed or equal to the current key, or the current key is missing or malformed.
- Other clients with no application name are connected to the database. This catches a running Worker and a busy API. An API idle for several minutes holds no connection and is not caught, so always do step 2. Tools that set a name (`psql`, `pg_dump`, pgAdmin) and autovacuum do not count. Close any other client the message counts. To find them: `SELECT pid, usename, client_addr, backend_start FROM pg_stat_activity WHERE datname = current_database() AND backend_type = 'client backend' AND application_name = '';`
- The database is not on this image's version, or the key is still in the database. Start the app once, stop it and retry.
- A credential cannot be decrypted. The message names it.

**Rollback**

| Situation | Action |
|---|---|
| Command refused | Nothing changed. Fix the reason and retry |
| After step 4, problem before step 6 | Stop both hosts. Then rotate back (`ENCRYPTION_KEY` = new key, `ENCRYPTION_KEY_NEW` = old key), or restore `before-rotation.dump` and keep the old key |
| New key lost after step 4 | Restore `before-rotation.dump` with the old key, and lose later changes. Without the old key, tenants re-enter tokens |

`before-rotation.dump` is the dump that rolls back a rotation. Restore it with the same command as in the upgrade, after you stop both hosts. `before-key-move.dump` returns to the state before adoption and does not roll back a rotation.

## Architecture

### Configuration Storage

Configuration is stored in the `SystemConfigs` database table (key-value store). The `ISystemConfigService` reads from this table with an in-memory cache, falling back to environment variables if a key isn't in the database.

Priority: **Database > Environment variable**

This means you can still override any setting via environment variables in `docker-compose.yml` if needed. The exception is the [encryption key](#encryption-key): the wizard does not store it.

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

> **Legacy mode and deleting rows.** While the key is in the database, the `encryption_key` row is its only copy. If you delete rows from `SystemConfigs` before [adoption](#upgrade-from-v007-or-earlier), you lose stored KSeF tokens and certificates. After adoption, the wizard must run with the same `ENCRYPTION_KEY`, or the credentials stop decrypting.

| Problem | Solution |
|---------|----------|
| Wizard doesn't appear | Only if the query from [upgrade step 1](#upgrade-from-v007-or-earlier) returns 0: check that the `SystemConfigs` table is empty. Delete rows and restart the API. |
| Keycloak auth fails | Verify Keycloak is running (`docker compose logs keycloak`). Default creds: admin/admin. |
| Apply fails with 500 | Check API logs (`docker compose logs api`). Common: Keycloak realm not fully imported yet. |
| SMTP test email fails | Verify SMTP credentials. Gmail requires an App Password (not regular password). |
| Redirect loop after setup | Clear browser cache/cookies. The portal caches setup status for 60 seconds. |
| Need to re-run wizard | Only if the query from [upgrade step 1](#upgrade-from-v007-or-earlier) returns 0: delete all rows from the `SystemConfigs` table and restart the API container. |
| Wizard reports no valid encryption key | Generate a key, set `ENCRYPTION_KEY`, keep a copy outside the database and run `docker compose up -d`. Then click **Dalej** (Next) and go through the wizard again. Entered data is kept. |
| `docker compose` reports a missing `ENCRYPTION_KEY`, `POSTGRES_USER` or another required variable | The new `docker-compose.yml` requires the key. On an existing installation first run the row count query in its `docker exec` form from the [upgrade](#upgrade-from-v007-or-earlier). Result 1: follow the upgrade, because setting the key alone skips stopping the hosts and the backup. Result 0: the stack has adopted, so restore the previous `ENCRYPTION_KEY` value and do not generate a new one, because a new key does not decrypt the stored data. If you have no copy, the containers still hold it: `docker inspect openksef-api \| grep '"ENCRYPTION_KEY='`. This command prints a secret to the terminal. No value: see upgrade step 1. Do not run `docker compose up -d` before that. Other variables: restore them from the old `.env` before `up -d`. `docker inspect openksef-postgres | grep POSTGRES_` and `docker inspect openksef-keycloak | grep KEYCLOAK_ADMIN` print them, including secrets. |
| Critical: `The configured encryption key does not decrypt` | The configured key does not match the ciphertext. Restore the key that encrypted it. |
| Critical: `The configured encryption key cannot be used` | The row is gone and `ENCRYPTION_KEY` is empty or malformed. Login and invoice reads work. Adding credentials, sync and send do not. Restore the key. |

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
