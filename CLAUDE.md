# CLAUDE.md

Repository guidance for coding agents.

## Project

OpenKSeF is a .NET 8 solution for KSeF invoice sync and browsing.

Main projects:
- `src/OpenKSeF.Api` - API
- `src/OpenKSeF.Worker` - background sync
- `src/OpenKSeF.Domain` - shared domain/data (includes `IKSeFGateway` abstraction)
- `src/OpenKSeF.Sync` - sync orchestration + KSeF gateway (uses CIRFMF `KSeF.Client` via NuGet)
- `src/OpenKSeF.Portal.Web` - React portal (active)

Legacy projects retained for rollback/reference (not active runtime path):
- `src/OpenKSeF.Portal`
- `src/OpenKSeF.Portal.Tests`
- `src/OpenKSeF.Portal.E2E`

## Infrastructure

`docker-compose.dev.yml` provides local-build dev stack (6 services):
- **Postgres** (:5432) - shared by Keycloak and app
- **Keycloak** (:8082) - identity provider, `openksef` realm auto-imported
- **API** (:8081) - .NET 8 REST API with Swagger
- **Worker** - background KSeF sync
- **Portal Web** (:8083) - React SPA
- **Gateway** (:8080) - nginx reverse proxy: `/` portal, `/api/` API, `/auth/` Keycloak

`docker-compose.yml` is the production-image variant (pulls from GHCR).

## Dev environment setup

One-command bootstrap:

```powershell
./scripts/dev-env-up.ps1          # start Docker, configure Keycloak, create test user
./scripts/dev-env-down.ps1        # stop all (add -RemoveVolumes for clean state)
```

The script copies `.env.test` to `.env` if missing. `.env.test` contains deterministic dev credentials with placeholder values for secrets. Real credentials (CIRFMF NuGet PAT, KSeF tokens) must be set in `.env` -- see `.env.example` for the full list.

### URLs (after dev-env-up)

| Service | URL |
|---------|-----|
| Gateway (portal+API+auth) | http://localhost:8080 |
| Keycloak admin console | http://localhost:8082/auth/admin |
| API direct | http://localhost:8081 |
| API Swagger | http://localhost:8081/swagger |
| Portal direct | http://localhost:8083 |

### Test credentials

| Account | Username | Password |
|---------|----------|----------|
| Keycloak admin | `admin` | `admin` |
| E2E test user | `testuser` | `Test1234!` |

### Test company & KSeF

`setup-test-data.ps1` (called automatically by `dev-env-up.ps1`) provisions a test tenant:

| Item | Value |
|------|-------|
| NIP | `1111111111` |
| KSeF token | stored in `E2E_TEST_KSEF_TOKEN` in `.env.test` |
| KSeF environment | `https://ksef-test.mf.gov.pl/api` (docker default) |

The script is **idempotent** -- re-running skips existing data. It:
1. Authenticates as `testuser` via OIDC direct-access grant
2. Creates tenant with the test NIP if missing
3. Adds KSeF credentials (token) to the tenant
4. Triggers invoice sync from KSeF test

Run standalone: `./scripts/setup-test-data.ps1` (requires Docker stack + test user)

### Custom login page

Portal `/login` uses ROPC grant (no Keycloak redirect), Google IdP brokering, and a registration endpoint. Requires `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `API_CLIENT_SECRET` in `.env`. Setup: [docs/login-page-setup.md](docs/login-page-setup.md).

## MCP servers for agent testing

Configured in `.cursor/mcp.json` (project-level). All connect to the local Docker stack.

| Server | Package | Purpose |
|--------|---------|---------|
| **playwright** | `@playwright/mcp` | Browse portal UI, fill forms, verify pages via accessibility tree. Config: `.cursor/playwright-mcp.config.json` (headless chromium, 1280x720, saveTrace). |
| **postgres** | `@modelcontextprotocol/server-postgres` | Read-only SQL queries on `openksef` database. Inspect `tenants`, `invoice_headers`, `ksef_credentials`, `sync_states`, `device_tokens`. |
| **keycloak** | `keycloak-mcp` | Manage Keycloak users/realms/clients. Tools: `create-user`, `delete-user`, `list-users`, `list-clients`, `assign-client-role`, etc. |
| **context7** | (global) | Look up library/framework documentation on demand. |

### Agent testing workflow

1. **Start environment**: run `./scripts/dev-env-up.ps1` via shell
2. **Browse portal**: use Playwright MCP to navigate http://localhost:8080, log in as `testuser`, inspect pages
3. **Query database**: use Postgres MCP to run SQL (e.g. `SELECT * FROM tenants`)
4. **Manage users**: use Keycloak MCP to create/delete test users dynamically
5. **Run unit tests**: `./scripts/run-all-tests.ps1` or individual `dotnet test` / `npm test`
6. **Run Portal E2E**: `./scripts/run-e2e-portal.ps1`
7. **Check docs**: use Context7 MCP for up-to-date API references

### Debugging playbook

| Problem | How to debug |
|---------|-------------|
| Container not starting | `docker compose -f docker-compose.dev.yml logs <service>` |
| DB schema / data issues | Postgres MCP: `SELECT * FROM ...` or check migration history |
| Portal UI bug | Playwright MCP: navigate to page, inspect accessibility tree |
| Auth/login failure | Keycloak MCP: `list-users` in openksef realm; check Keycloak logs |
| API error | Swagger at http://localhost:8081/swagger; check API logs |

## Common commands

```bash
# Full environment (preferred -- includes test data provisioning)
./scripts/dev-env-up.ps1
./scripts/dev-env-down.ps1

# Provision test data only (tenant + KSeF creds + sync)
./scripts/setup-test-data.ps1

# Manual Docker
docker compose -f docker-compose.dev.yml up -d --build
docker compose -f docker-compose.dev.yml down

# Build
dotnet build src/OpenKSeF.sln

# Unit tests
dotnet test src/OpenKSeF.Api.Tests/OpenKSeF.Api.Tests.csproj
dotnet test src/OpenKSeF.Domain.Tests/OpenKSeF.Domain.Tests.csproj

# API integration tests (Testcontainers -- needs Docker Desktop, no docker-compose)
dotnet test src/OpenKSeF.Api.IntegrationTests/OpenKSeF.Api.IntegrationTests.csproj

# Portal Web
cd src/OpenKSeF.Portal.Web
npm ci
npm run build
npm run lint
npm test
```

## Configuration keys

- `APP_EXTERNAL_BASE_URL` - public URL of the gateway (used by admin setup wizard, API token issuer); defaults to `http://localhost:8080`
- `ConnectionStrings__Db`
- `Auth__Authority`
- `ENCRYPTION_KEY`
- `KSeF__BaseUrl`
