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

The wizard auto-generates:
- **AES-256 encryption key** for encrypting KSeF tokens in the database
- **API client secret** fetched from the Keycloak `openksef-api` client

These are stored securely in the database and shared between services automatically. No manual copying required.

### Step 5: Optional Integrations

**Google OAuth** (optional): If you want Google sign-in, create a Google Cloud OAuth 2.0 Client ID and enter the credentials. The redirect URI is pre-filled.

**Push Notifications**: Three modes available:

- **Relay OpenKSeF (default, recommended)** -- Uses the team-operated relay server to deliver push notifications via FCM/APNs. No Firebase setup needed. The relay URL (`https://push.open-ksef.pl`) is pre-filled. Just leave it ON.
- **Own Firebase project (advanced)** -- Paste your own Firebase service account JSON for direct FCM delivery. See [push-notifications-setup.md](push-notifications-setup.md) for details.
- **Local only (SignalR)** -- No remote push. Users receive notifications only when the mobile app is actively connected to the server.

SignalR local push is always active regardless of which mode you choose. The mode only affects remote (background) delivery.

### Step 6: Summary & Apply

Review all settings and click "Apply". The wizard:
1. Generates the encryption key
2. Fetches the Keycloak API client secret
3. Enables token-exchange for the service account
4. Updates client redirect URIs with the external URL
5. Configures the Keycloak realm (login policy, password policy, SMTP)
6. Creates the admin user in Keycloak
7. Configures Google IdP (if provided)
8. Stores all configuration in the database
9. Redirects to the login page

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
