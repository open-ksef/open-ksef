# Admin Setup Wizard

The admin setup wizard automates first-time system configuration. Instead of copying Keycloak secrets and restarting containers, an admin writes a short `.env`, runs `docker compose up -d` and walks through a web-based wizard at `http://localhost:8080/admin-setup`.

## Prerequisites

- Docker and Docker Compose v2 installed
- Port 8080 free, or `APP_HOST_PORT` set in `.env`: `docker-compose.yml` publishes only the gateway
- `.env` next to `docker-compose.yml` with `POSTGRES_USER`, `POSTGRES_PASSWORD`, `KEYCLOAK_ADMIN`, `KEYCLOAK_ADMIN_PASSWORD` and `ENCRYPTION_KEY`, see [Encryption key](#encryption-key). `docker compose` refuses to run without them

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

Enter the Keycloak admin credentials: `KEYCLOAK_ADMIN` and `KEYCLOAK_ADMIN_PASSWORD` from `.env`. The wizard authenticates against the Keycloak master realm and receives a short-lived setup session token (10 minutes).

### Step 2: Base Configuration

| Field | Description | Default |
|-------|-------------|---------|
| External URL | Public URL where the system is accessible | Current browser origin |
| KSeF Environment | Test or Production | Test (`https://api-test.ksef.mf.gov.pl/v2`) |
| Admin email | Email for the first OpenKSeF user | (required) |
| Admin password | Password for the first user | (required, min 8 chars) |
| First name / Last name | Optional user details | Admin |
| NIP | NIP of the first company | (required) |
| Company name | Optional | (none) |

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

The wizard fetches the **API client secret** from the Keycloak `openksef-api` client and stores it in the database. The API never returns it to the browser. If you need it, for example for `API_CLIENT_SECRET`, read it in the Keycloak admin console: **Clients > openksef-api > Credentials**, see [login page setup](login-page-setup.md). Do not regenerate it there: the API prefers the copy in the database, and setup-token exchange then fails.

The **AES-256 encryption key** is not part of the wizard, and no API response contains it. Get it from your copy outside the database, or from `ENCRYPTION_KEY` in `.env` or the stack environment. An installation that has not adopted yet also holds it in the `encryption_key` row of `SystemConfigs`. If you cannot find it, do not generate a new one: step 1 of the [upgrade](#upgrade-from-v007-or-earlier) says where to recover it. Set `ENCRYPTION_KEY` (or `ENCRYPTION_KEY_FILE`) on the API and Worker before you start the stack, and back it up outside the database. The wizard refuses to apply without it. Shipped `docker-compose.yml` forwards only `ENCRYPTION_KEY` and refuses to start without it, so `ENCRYPTION_KEY_FILE` needs your own compose file. An override file alone is not enough, because the check in the base file still runs. See [Encryption key](#encryption-key) for the upgrade and rotation steps.

### Step 5: Optional Integrations

**Google OAuth** (optional): If you want Google sign-in, create a Google Cloud OAuth 2.0 Client ID and enter the credentials. The redirect URI is pre-filled.

**Push Notifications**: Three modes available:

- **Relay OpenKSeF (default, recommended)** -- Uses the team-operated relay server to deliver push notifications via FCM/APNs. No Firebase setup needed. The relay URL (`https://push.open-ksef.pl`) is pre-filled. Just leave it ON.
- **Own Firebase project (advanced)** -- Paste your own Firebase service account JSON for direct FCM delivery. See [push-notifications-setup.md](push-notifications-setup.md) for details.
- **Local only (SignalR)** -- No remote push. Users receive notifications only when the mobile app is actively connected to the server.

SignalR local push is always active regardless of which mode you choose. The mode only affects remote (background) delivery.

The settings page says only whether a relay key is stored, and no API response contains it. On the default relay, **Zarejestruj ponownie** and then saving issues a new key. On a custom relay, type the new key; an empty field keeps the stored one. An empty SMTP password also keeps the stored one.

### Step 6: Summary & Apply

Review all settings and click "Apply". The wizard:
1. Creates the `openksef` realm in Keycloak if it does not exist
2. Creates the OAuth clients `openksef-api`, `openksef-mobile` and `openksef-portal-web`
3. Fetches the Keycloak API client secret
4. Enables token-exchange for the service account
5. Updates client redirect URIs with the external URL
6. Configures the Keycloak realm (login policy, password policy, SMTP)
7. Creates the admin user in Keycloak
8. Configures Google IdP (if provided)
9. Stores all configuration in the database
10. Redirects to the login page

## Encryption key

> **Check `ENCRYPTION_KEY` before you upgrade.** Before any `docker compose pull`, `docker compose up -d`, stack update or automatic updater run, check `ENCRYPTION_KEY` in `.env` or the stack environment. If it holds a valid value, the first start of the new images moves the key to that value ("adoption"), with no stop and no backup. Follow the [upgrade procedure](#upgrade-from-v007-or-earlier) first.
>
> Already adopted (the query in its step 1 returns 0)? Keep that value, because it is the only copy of the key, and [rotate the key](#rotate-the-key) only if you want a new one. Is the value empty? Do not generate a key and do not run `docker compose up -d`. Step 1 of the procedure says where to recover it.

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
| New `docker-compose.yml` without `ENCRYPTION_KEY` | `docker compose` refuses. Its message sends an existing installation to this page and names the generator command for a new one. `stop`, `exec`, `ps`, `logs` refuse unless the project name is set. `up`, `pull`, `run` always refuse. Running containers stay untouched |

Legacy mode is temporary: later releases are planned to stop reading the key from the database. Adopt as soon as you can.

### Upgrade from v0.0.7 or earlier

The first start with a valid configured key re-encrypts all credentials and deletes the database row, in one transaction.

Run steps 1 to 3 with the compose file you already use. The new `docker-compose.yml` arrives at step 6.

`docker compose` refuses to run (old file overwritten or `.env` lost)? Do not add the key to get past it: one `up` adopts with no backup. Run steps 1 to 3 with Docker alone, then continue from step 4. `.env` lost: rebuild it as the [`docker compose` reports a missing](#troubleshooting) row says. Run one command at a time. The first is step 1: on a result of `0`, stop as that step says. The container names are fixed:

```bash
echo "SELECT count(*) FROM \"SystemConfigs\" WHERE \"Key\"='encryption_key';" | docker exec -i openksef-postgres sh -c 'psql -U "$POSTGRES_USER" -d openksef -tA'
```

`is not running`? Run `docker start openksef-postgres`, wait a few seconds, then run the query.

`No such container`? Go to the directory with the stack's `docker-compose.yml` and `.env`: compose names the project after the directory, and another directory means a new empty volume. Run `docker volume ls --filter name=postgres_data` and note your volume: `<project>_postgres_data`, where `<project>` is the directory name or the Portainer stack name. An empty list means no database remains: stop and restore your backup. Start Postgres alone. This command starts neither API nor Worker and moves no key: `ENCRYPTION_KEY=unused docker compose up -d --no-deps postgres`. This needs `POSTGRES_USER`, `POSTGRES_PASSWORD`, `KEYCLOAK_ADMIN` and `KEYCLOAK_ADMIN_PASSWORD` in `.env`. Check the volume: `docker inspect openksef-postgres --format '{{range .Mounts}}{{.Name}}{{end}}'` must print the noted name. Another name means an empty database that is not yours: run `docker rm -f openksef-postgres`, which keeps the volumes, and repeat from the right directory. No such directory, as with a Portainer stack: put `COMPOSE_PROJECT_NAME=<project>` before the command. Wait a few seconds, then run the query. Any error, such as `relation "SystemConfigs" does not exist`, is not a result of `0`: do not continue and do not generate a key. Check the volume and `POSTGRES_USER`, then retry. With API and Worker gone, `docker stop` below reports `No such container`: continue.

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
   `service "postgres" is not running`? Do not run `docker compose up -d`. Run `docker start openksef-postgres`, wait a few seconds, then retry. Same error? This is not the stack's directory. Go there, or use the Docker-alone commands above, then continue from step 4. `No such container`? Follow the `No such container` paragraph above. Continue only if the result is `1`. A result of `0` means the stack has already adopted, and `ENCRYPTION_KEY` is the only copy of the key. **Stop this procedure, do not change `ENCRYPTION_KEY`.** If `ENCRYPTION_KEY` is empty, **do not generate a key and do not run `docker compose up -d`**. Restore the previous value. If you have no copy, the containers still hold it: `docker inspect openksef-api | grep '"ENCRYPTION_KEY='`. This command prints a secret to the terminal. If it shows no value (`"ENCRYPTION_KEY="` or `No such object`), run it on `openksef-worker`. If that shows none either, no copy remains. Only then generate a key, set it, run `docker compose up -d` and follow the `New key lost` row under Rollback below. `does not decrypt` stays in the logs until every credential is re-entered. Once the key is back in `ENCRYPTION_KEY`, run `docker compose up -d` and check that one manual sync succeeds. [Rotate the key](#rotate-the-key) only if you want a new one.
2. **Stop both hosts:** `docker compose stop api worker`. Stop both, because compose starts them in no fixed order. An old API that still serves during adoption writes under the old key, and that credential is lost.
3. **Back up:** `docker compose exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -Fc openksef' > before-key-move.dump`. This dump holds the old key and the ciphertext together, so protect it. Verify it: `docker compose exec -T postgres pg_restore -l < before-key-move.dump > /dev/null; echo "exit=$?"` must print `exit=0`.
4. **Generate the key:** `openssl rand -base64 32`, or the Docker command above. It must have 44 characters. **Store it in a password manager before the next step**, outside the database and its backups. Do not reuse an older `ENCRYPTION_KEY` value.
5. **Set the key:** copy the current `ENCRYPTION_KEY` value, if one is set, to a password manager before you overwrite it. Then put the new value in `ENCRYPTION_KEY` in `.env` or the stack environment.
6. **Start the new images:** replace `docker-compose.yml` with the new file (attached to the release page), then `docker compose pull api worker portal-web gateway` and `docker compose up -d`.
7. **Verify:**
   - In `docker compose logs api worker` one host logs `Encryption key moved out of the database`. The other logs nothing about it. Neither logs Critical.
   - The query from step 1 returns 0.
   - One manual sync succeeds: **Firma** > **Token KSeF i pobieranie** > **Pobierz z KSeF**.

The dump from step 3 is now the only copy of the old key. Keep it at least until a sync succeeds. Dumps taken before adoption can decrypt the tokens in them until you delete those dumps.

A Critical log `Moving the encryption key out of the database failed` followed by `Credential <id> cannot be decrypted` means that credential could not be decrypted, and nothing changed. Delete or re-enter that credential in the portal and restart. If the log says `does not decrypt`, restore the key and delete nothing. The same Critical with no credential line means nothing changed: restart. For `cannot be used`, see Troubleshooting.

**Rollback**

Previous images carry a release tag, e.g. `:v0.0.7`. Set it in the four `ghcr.io/open-ksef/openksef-*` `image:` lines of `docker-compose.yml`, then run `docker compose up -d`.

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
| After step 4, problem before step 6 | Stop both hosts. Then rotate back (`ENCRYPTION_KEY` = new key, `ENCRYPTION_KEY_NEW` = old key), then set `ENCRYPTION_KEY` to the old key and run `docker compose up -d`, or restore `before-rotation.dump` and keep the old key |
| New key lost after step 4 | Restore `before-rotation.dump` with the old key, and lose later changes. Without the old key, tenants re-enter tokens |

`before-rotation.dump` is the dump that rolls back a rotation. Restore it with the same command as in the upgrade, after you stop both hosts. `before-key-move.dump` returns to the state before adoption and does not roll back a rotation.

## Issued invoice sync: upgrade and rollback

The release that adds the issued invoice feed has an additive migration: new columns on `InvoiceHeaders` and `SyncStates` and one index. Existing rows become `Received`. Paid flags, due dates and lines do not change. The first sync after the upgrade loads 12 months of issued invoices from KSeF and sends no notifications.

Upgrade:

1. Back up the database.
2. Deploy the API, Worker and portal from one tag. The API applies the migration. The Worker waits until it is applied.

Rollback: an older image has no direction filter and would list issued invoices as unpaid received invoices. Do not run the migration `Down`. Stop the new API and Worker, run this SQL, then start the previous tag:

```sql
BEGIN;
DELETE FROM "InvoiceHeaders" WHERE "Direction" = 'Issued';
UPDATE "SyncStates" SET "IssuedContinuationAt" = NULL;
COMMIT;
```

The script removes only issued rows and their lines. They hold no user data, and sync loads them again after the next upgrade. The columns stay. Inserts from the older image get the database default, `Received`.

## KSeF environment: upgrade

`KSEF_BASE_URL` takes `test`, `demo` or `production`. It is read only until the wizard has run: after that the `ksef_environment` row in `SystemConfigs` wins.

A legacy test or demo URL (`ksef-test.mf.gov.pl`, `ksef-demo.mf.gov.pl`, `api-test.ksef.mf.gov.pl`, `api-demo.ksef.mf.gov.pl`) and a legacy production URL (`ksef.podatki.gov.pl`, `api.ksef.mf.gov.pl`) keep working. No action is needed.

API or Worker stops at start with `Unknown KSeF environment` when the value is anything else, so nothing is sent to KSeF on a guessed environment. This includes `https://ksef.mf.gov.pl/api`, which ran as TEST in earlier versions.

If no wizard has run, fix `KSEF_BASE_URL` in `.env` and run `docker compose up -d`. Otherwise run the SQL below with the environment the install ran on: `production` if the old value contains `ksef.podatki.gov.pl` or `api.ksef.mf.gov.pl`, otherwise `demo` if it contains `demo`, otherwise `test`. Choosing `production` needs a production KSeF token.

```bash
docker exec -i openksef-postgres sh -c 'psql -U "$POSTGRES_USER" -d openksef' <<'SQL'
INSERT INTO "SystemConfigs" ("Key", "Value", "UpdatedAt")
VALUES ('ksef_environment', 'test', now())
ON CONFLICT ("Key") DO UPDATE SET "Value" = EXCLUDED."Value", "UpdatedAt" = EXCLUDED."UpdatedAt";
SQL
docker compose restart api worker
```

A row that goes bad while the hosts run does not stop them. The Worker keeps running and sends nothing to KSeF: each sync cycle logs the error and skips its tenants, the send loop logs the same error at most every 15 minutes, and stuck invoices are not escalated. The API keeps its cached value until a settings save. Both stop with `Unknown KSeF environment` at the next restart. Fix the row with the SQL above.

The API also stops at start when it cannot read `SystemConfigs` after 5 attempts, rather than running on `KSEF_BASE_URL`. Check Postgres and start it again.

## Registration flag: upgrade

After the upgrade, open Settings and save once. This stores the registration flag in `SystemConfigs` (`registration_allowed`). Until then the login page shows the register link, and register without a service account secret returns 503. Settings is the only supported toggle for registration: the API does not see changes made in the Keycloak console.

## Upgrade: ports and Keycloak admin

Read this before you pull a release whose `docker-compose.yml` publishes only the gateway. Nothing is deleted: the database volume and all data stay. What changes is how you reach the services from outside Docker.

| Before | Now |
|---|---|
| Gateway on `APP_HOST_PORT` (8080) | Unchanged |
| API on 8081, Keycloak on 8082, portal on 8083, Postgres on 5432 (`API_HOST_PORT`, `KEYCLOAK_HOST_PORT`, `PORTAL_WEB_HOST_PORT`, `POSTGRES_HOST_PORT`) | Not published. The variables are ignored |
| `/auth/admin` open through the gateway | 403 unless `KEYCLOAK_ADMIN_ALLOW` lists the client address |
| Images `:latest` | Images `:${OPENKSEF_VERSION}`. The compose attached to a release defaults to that release |
| No restart policy | `restart: unless-stopped` on every service. The Worker starts after the API is healthy |

Portal, API and login keep working through the gateway, and so does the setup wizard: it calls Keycloak inside the Docker network. Check what used the other ports before you upgrade: a backup script that connects to `localhost:5432`, a monitor that calls `:8081/health`, a bookmark to `:8082/auth/admin`.

**Postgres.** Use `docker exec`, as all procedures on this page do: `docker exec -it openksef-postgres sh -c 'psql -U "$POSTGRES_USER" -d openksef'`, or `docker exec openksef-postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d openksef' > openksef.sql` for a backup. A tool that needs a TCP port gets one bound to the host loopback only, through `docker-compose.override.yml` next to `docker-compose.yml` (`docker compose` reads it on its own):

```yaml
services:
  postgres:
    ports:
      - "127.0.0.1:5432:5432"
  keycloak:
    ports:
      - "127.0.0.1:8082:8080"
```

Then run `docker compose up -d`.

**Keycloak admin console, SSH tunnel (recommended).** With the `keycloak` entry of the override above, run on your computer `ssh -L 8082:127.0.0.1:8082 user@your-server` and open <http://localhost:8082/auth/admin>. The console is not reachable from any network, and the gateway stays at 403.

**Keycloak admin console, allow-list.** Set `KEYCLOAK_ADMIN_ALLOW` in `.env` to the addresses that may open `/auth/admin` through the gateway, comma separated, IP or CIDR, for example `KEYCLOAK_ADMIN_ALLOW=192.168.88.0/24,10.8.0.5`. Then run `docker compose up -d`. Empty or unset means 403 for everyone. nginx checks the address of the connection it receives. Behind a reverse proxy or Cloudflare, that is the proxy's address, not the browser's. Listing it opens the console to every visitor, so use the SSH tunnel there. The same applies when Docker's userland proxy forwards the port: on Docker Desktop (Windows, macOS) for every client, on Linux for connections to `localhost` and for all clients when `"iptables": false` is set in `daemon.json`. nginx then sees the Docker bridge gateway, for example `172.18.0.1`, and listing that address opens the console to everyone who reaches the port. Check the address nginx sees with `docker compose logs gateway`. The gateway skips an invalid entry, logs `KEYCLOAK_ADMIN_ALLOW entry '<entry>' is not a valid nginx allow address, skipped` and keeps the other entries.

**Image version.** `OPENKSEF_VERSION` in `.env` picks the tag of `api`, `worker`, `portal-web` and `gateway`, for example `OPENKSEF_VERSION=v1.2.3`. Leave it unset to use the release the compose came with. Upgrade: change it (or take the new compose), then `docker compose pull && docker compose up -d`. Roll back: set the previous tag and run the same commands. Migrations are not rolled back, so read the release notes first. A `docker-compose.yml` from v0.0.11 or older names `:latest`, which points to the newest release. Taking that file back restores the old ports, not the old images.

## Reverse proxy and public URL

Keycloak builds the token issuer (`iss`) and every absolute link (login redirects, email verification, password reset) from what the gateway sends it. The gateway takes the host and port from the `Host` header. It takes the scheme from `APP_EXTERNAL_BASE_URL` when that is set in `.env`, otherwise from `X-Forwarded-Proto`. It ignores `X-Forwarded-Host` and `X-Forwarded-Port` from the client. `Host`, and `X-Forwarded-Proto` when `APP_EXTERNAL_BASE_URL` is not set, must come from a proxy that overwrites them; with direct access, or a proxy that passes the client's value, a request can still change the host or port in its own links, and without `APP_EXTERNAL_BASE_URL` also the scheme. The issuer must equal `APP_EXTERNAL_BASE_URL/auth/realms/openksef` (or the URL saved by the wizard), otherwise the API answers 401.

| Setup | What the gateway needs | Issuer |
|---|---|---|
| Direct, `APP_HOST_PORT=18480` | Nothing | `http://<host>:18480/auth/realms/openksef` |
| Nginx Proxy Manager, HTTPS on 443 | `APP_EXTERNAL_BASE_URL=https://<domain>`. NPM passes a client `X-Forwarded-Proto` through; see below | `https://<domain>/auth/realms/openksef` |
| ngrok, Cloudflare Tunnel, Caddy, Traefik | Nothing: they keep the public `Host` and send `X-Forwarded-Proto` | `https://<public host>/auth/realms/openksef` |
| nginx with your own `location` | `proxy_set_header Host $host:$server_port;` and `proxy_set_header X-Forwarded-Proto $scheme;` | public host and port |
| Apache | `ProxyPreserveHost On`, `RequestHeader set Host "<domain>"` and `RequestHeader set X-Forwarded-Proto https` | public host |
| Public HTTPS on a port other than 443 | The proxy must send that port in `Host`, for example `Host: example.com:8443` (nginx `$host:$server_port`) | `https://example.com:8443/auth/realms/openksef` |

A proxy that sends its upstream name as `Host` (for example `gateway:8080`) and the public name only in `X-Forwarded-Host` no longer works: set `Host` as above. The proxy must overwrite `X-Forwarded-Proto`. Nginx Proxy Manager passes a client value through, so without `APP_EXTERNAL_BASE_URL` a request can get `http://` links. With `APP_EXTERNAL_BASE_URL` set, every access through the gateway gets its scheme, including a tunnel such as ngrok, so a plain `http://` value there gives `http://` links on an HTTPS tunnel. The gateway then logs `APP_EXTERNAL_BASE_URL is http:// but requests arrive with X-Forwarded-Proto https, links stay http://` once per start, within 10 seconds of the first such request; set `APP_EXTERNAL_BASE_URL` to the `https://` address or unset it. The gateway reads `APP_EXTERNAL_BASE_URL` only from a `docker-compose.yml` that passes it to the `gateway` service; an older file pulls the new image without it, and the scheme then still comes from `X-Forwarded-Proto`. Check the result with `curl -s https://<domain>/auth/realms/openksef/.well-known/openid-configuration` and compare `issuer` with `APP_EXTERNAL_BASE_URL`.

## Start failures and restart loops

Every service has `restart: unless-stopped`. When API or Worker exits at start, Docker starts it again after a short, growing delay, without end. You see a loop, not a stopped container. Causes that exit at start:

- a bad `ksef_environment` row or `KSEF_BASE_URL` (`Unknown KSeF environment`), see [KSeF environment: upgrade](#ksef-environment-upgrade)
- `KSeF:ForbidProduction` set on a stack whose environment is production
- `SystemConfigs` unreadable after 5 attempts (Postgres down or broken)

API and Worker log one `FTL` line with the reason. The KSeF environment refusals exit with code 1. How to see the loop and the reason:

```bash
docker compose ps                     # STATUS "Restarting (1) ..." for the looping service
docker inspect -f '{{.RestartCount}} {{.State.ExitCode}}' openksef-api
docker compose logs --tail 50 api     # the reason; use worker or gateway for the others
```

`docker compose up -d` waits for the API health check before it starts the Worker. If the API loops, `up -d` ends with `dependency failed to start: container openksef-api is unhealthy` and the Worker stays down. That is expected.

Fix the cause. A container that already runs needs nothing more for a fixed database row: the next automatic restart starts it normally. Then run `docker compose up -d` once, so a Worker that a failed `up -d` never started comes up too. A fixed `.env` value needs `docker compose up -d`, because a restart keeps the old environment. Stop a loop while you work on it with `docker compose stop api worker`.

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
| Keycloak auth fails | Verify Keycloak is running (`docker compose logs keycloak`). Credentials: `KEYCLOAK_ADMIN` and `KEYCLOAK_ADMIN_PASSWORD` from `.env`. |
| Apply fails with 500 | Check API logs (`docker compose logs api`). Common: Keycloak realm not fully imported yet. |
| SMTP test email fails | Verify SMTP credentials. Gmail requires an App Password (not regular password). |
| Redirect loop after setup | Clear browser cache/cookies. The portal caches setup status for 60 seconds. |
| Need to re-run wizard | Only if the query from [upgrade step 1](#upgrade-from-v007-or-earlier) returns 0: delete all rows from the `SystemConfigs` table and restart the API container. |
| Wizard reports no valid encryption key | Generate a key, set `ENCRYPTION_KEY`, keep a copy outside the database and run `docker compose up -d`. Then click **Dalej** (Next) and go through the wizard again. Entered data is kept. |
| `docker compose` reports a missing `ENCRYPTION_KEY`, `POSTGRES_USER` or another required variable | The new `docker-compose.yml` requires the key. On an existing installation first run the row count query in its `docker exec` form from the [upgrade](#upgrade-from-v007-or-earlier). Result 1: follow the upgrade, because setting the key alone skips stopping the hosts and the backup. Result 0: the stack has adopted, so restore the previous `ENCRYPTION_KEY` value and do not generate a new one, because a new key does not decrypt the stored data. If you have no copy, the containers still hold it: `docker inspect openksef-api \| grep '"ENCRYPTION_KEY='`. This command prints a secret to the terminal. No value: see upgrade step 1. Do not run `docker compose up -d` before that. Other variables: restore them from the old `.env` before `up -d`. `docker inspect openksef-postgres \| grep POSTGRES_` and `docker inspect openksef-keycloak \| grep -E 'KEYCLOAK_ADMIN\|KC_DB_'` print them, including secrets. Do the same for the optional values, because `up -d` removes this copy: `docker inspect openksef-api --format '{{range .Config.Env}}{{println .}}{{end}}' \| grep -E 'Auth__PublicAuthority\|KSeF__BaseUrl\|Auth__ServiceAccount__ClientSecret\|Firebase__CredentialsJson'`. This command prints secrets too. In `.env` they are `APP_EXTERNAL_BASE_URL` (`Auth__PublicAuthority` without `/auth/realms/openksef`), `KSEF_BASE_URL`, `API_CLIENT_SECRET` and `FIREBASE_CREDENTIALS_JSON`. `docker ps` shows custom host ports. Only `APP_HOST_PORT` is still read; for the others see [Upgrade: ports and Keycloak admin](#upgrade-ports-and-keycloak-admin). |
| A container shows `Restarting` | See [Start failures and restart loops](#start-failures-and-restart-loops). |
| API or Worker stops with `Unknown KSeF environment` | The stored or configured value is not a known environment. See [KSeF environment: upgrade](#ksef-environment-upgrade). |
| Critical: `The configured encryption key does not decrypt` | The configured key does not match the ciphertext. Restore the key that encrypted it. |
| Critical: `The configured encryption key cannot be used` | The row is gone and `ENCRYPTION_KEY` is empty or malformed. Login and invoice reads work. Adding credentials, sync and send do not. Restore the key. |

## Development

For local development against `docker-compose.dev.yml`:

```bash
# Start fresh (no dev-env-up.ps1 provisioning)
docker compose -f docker-compose.dev.yml up -d --build

# Open http://localhost:8080 -> wizard appears
# Walk through the wizard

# Run integration tests (requires Docker for Testcontainers)
dotnet test src/OpenKSeF.Api.IntegrationTests -nologo --filter "FullyQualifiedName~SystemSetup"
```
