# open-ksef

[![License: Elastic-2.0](https://img.shields.io/badge/License-Elastic--2.0-blue.svg)](LICENSE)

OpenKSeF is a .NET 8 system for syncing and browsing invoices from Poland's KSeF.

Components:
- `OpenKSeF.Api` - REST API (JWT via Keycloak)
- `OpenKSeF.Worker` - scheduled invoice sync
- `OpenKSeF.Portal.Web` - React portal

Mobile app: see [open-ksef-mobile](https://github.com/OpenKSeF/open-ksef-mobile).

Legacy notice:
- The Blazor portal (`OpenKSeF.Portal`) was removed from active runtime paths after React cutover on **February 28, 2026**.
- Legacy code remains in the repository for rollback and reference, but is not part of the default runtime stack.

## Quick start (Docker)

1. Create env file:
```bash
cp .env.example .env
```

2. Start full stack from published images:
```bash
docker compose up -d
```

3. Start full stack with local builds (API + Worker + React portal build locally):
```bash
docker compose -f docker-compose.dev.yml up -d --build
```

`docker-compose.yml` assumes published images in GHCR. `docker-compose.dev.yml` builds app images from local source.

Default ports:
- Public gateway: `8080` (default, configurable with `APP_HOST_PORT`)
  - Portal Web: `/`
  - API: `/api/*`
  - Keycloak: `/auth/*`
- API direct: `8081` (default, configurable with `API_HOST_PORT`)
- Keycloak direct: `8082` (default, configurable with `KEYCLOAK_HOST_PORT`)
- Portal Web direct: `8083` (default, configurable with `PORTAL_WEB_HOST_PORT`)
- Postgres: `5432` (default, configurable with `POSTGRES_HOST_PORT`)

If you access the app from another host/IP, set `APP_EXTERNAL_BASE_URL` (for example `http://192.168.1.50:8080`) so SPA/OIDC redirect URLs use the correct public address.

Realm import file: `keycloak/realm-openksef.json` (clients: `openksef-api`, `openksef-portal-web`).

## Local development

### NuGet feed setup (one-time)

The project uses [CIRFMF/ksef-client-csharp](https://github.com/CIRFMF/ksef-client-csharp) packages from GitHub Packages. You need a GitHub PAT to restore them.

1. Create a **Personal Access Token (classic)** at [github.com/settings/tokens](https://github.com/settings/tokens) with the **`read:packages`** scope.

2. Register the feed locally (credentials are stored in your user-level NuGet config, **not** in the repo):

```bash
dotnet nuget add source "https://nuget.pkg.github.com/CIRFMF/index.json" \
  --name CIRFMF \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_PAT \
  --store-password-in-clear-text
```

3. Verify:

```bash
dotnet nuget list source
# Should show "CIRFMF [Enabled]"
```

To update credentials later:

```bash
dotnet nuget update source CIRFMF \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_NEW_PAT \
  --store-password-in-clear-text
```

### Build

```bash
dotnet build src/OpenKSeF.sln
```

Run services locally:
```bash
dotnet run --project src/OpenKSeF.Api/OpenKSeF.Api.csproj
dotnet run --project src/OpenKSeF.Worker/OpenKSeF.Worker.csproj
```

Run Portal Web locally:
```bash
cd src/OpenKSeF.Portal.Web
npm ci
npm run dev
```

Run tests:
```bash
dotnet test src/OpenKSeF.Api.Tests/OpenKSeF.Api.Tests.csproj
dotnet test src/OpenKSeF.Domain.Tests/OpenKSeF.Domain.Tests.csproj
dotnet test src/OpenKSeF.KSeFClient.Tests/OpenKSeF.KSeFClient.Tests.csproj
cd src/OpenKSeF.Portal.Web && npm test
```

## Quick debug

Start dependencies:
```bash
docker compose up -d postgres keycloak
```

Run backend locally (terminal 1):
```bash
dotnet run --project src/OpenKSeF.Api/OpenKSeF.Api.csproj
```

Run React portal locally (terminal 2):
```bash
cd src/OpenKSeF.Portal.Web
npm ci
npm run dev
```

Useful debug URLs:
- React app: `http://localhost:5173`
- API swagger: `http://localhost:8081/swagger`
- Keycloak realm: `http://localhost:8082/auth/realms/openksef`

Account creation:
- Open portal login (`/login`) and use **Create account**.
- Self-registration is enabled in realm `openksef` and each user only sees their own tenants in API queries.

Run Playwright E2E against local React app:
```bash
PORTAL_BASE_URL=http://localhost:5173 dotnet test src/OpenKSeF.Portal.E2E/OpenKSeF.Portal.E2E.csproj -c Release
```

## Required configuration

API/Worker minimum:
- `ConnectionStrings__Db`
- `Auth__Authority`
- `ENCRYPTION_KEY` (required in non-development)
- `KSeF__BaseUrl`

Portal Web minimum:
- `VITE_API_BASE_URL`
- `VITE_AUTH_AUTHORITY`
- `VITE_AUTH_CLIENT_ID`

Optional push config:
- `Firebase__CredentialsJson`
- `APNs__BundleId`
- `APNs__BaseUrl`

## KSeF auth cutoff

`KSeF__AuthMode` supports `Token`, `Certificate`, and `Auto`.

In `Auto` mode, certificate auth is enforced from **January 1, 2027 (UTC)**.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on how to contribute.

## Security

If you discover a security vulnerability, **do not open a public issue**. See [SECURITY.md](SECURITY.md) for responsible disclosure instructions.

## Migration and rollback

See [MIGRATION.md](MIGRATION.md) for React cutover details and rollback procedure.

## License

This project is licensed under the [Elastic License 2.0 (ELv2)](LICENSE).

Free to use, modify, and self-host for internal purposes. Offering OpenKSeF as
a hosted or managed service to third parties requires explicit written permission
from the copyright holder. See [COMMERCIAL.md](COMMERCIAL.md) for details.

