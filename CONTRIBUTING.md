# Contributing to OpenKSeF

Thank you for your interest in contributing to OpenKSeF! This guide will help you get started.

## Getting started

1. **Fork** the repository and clone your fork
2. Set up the dev environment (see [Development setup](#development-setup))
3. Create a feature branch from `main`
4. Make your changes
5. Run tests to verify nothing is broken
6. Submit a pull request

## Development setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 22](https://nodejs.org/), the version CI uses
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)

### CIRFMF NuGet feed (KSeF.Client dependency)

The .NET projects depend on `KSeF.Client` from the [CIRFMF](https://github.com/CIRFMF) GitHub Packages feed. This is a third-party library that provides the KSeF API client.

To restore NuGet packages you need a **GitHub Personal Access Token (classic)** with the `read:packages` scope:

1. Go to [github.com/settings/tokens](https://github.com/settings/tokens) and generate a classic token with `read:packages`
2. For image builds, put the token in `.env.test` as `NUGET_CIRFMF_USER` and `NUGET_CIRFMF_PAT` (see Quick start)
3. Register the feed in your user-level NuGet config. Run this from the repository root, not from `src/`. Never put credentials in `src/nuget.config`: it is tracked.
   ```bash
   dotnet nuget add source "https://nuget.pkg.github.com/CIRFMF/index.json" \
     --name CIRFMF \
     --username <your-github-username> \
     --password <your-pat> \
     --store-password-in-clear-text
   ```

### Quick start

Bootstrap script (PowerShell):

```powershell
./scripts/dev-env-up.ps1 -SkipNgrok
```

The first run creates `.env.test` from `.env.test.example`, generates `ENCRYPTION_KEY` and stops until you set `NUGET_CIRFMF_USER` and `NUGET_CIRFMF_PAT` in `.env.test`. Run it again: it builds and starts the stack, then stops at the setup wizard. Complete the wizard at <http://localhost:8080> with Keycloak admin `admin` / `admin`, choose **Tylko lokalne (SignalR)** in step 5, then run `./scripts/dev-env-up.ps1 -SkipBuild -SkipNgrok` to create the test user. Omit `-SkipNgrok` only for mobile OIDC testing: it opens a public tunnel to your stack.

### Running tests

```bash
# .NET unit tests
dotnet test src/OpenKSeF.Api.Tests/OpenKSeF.Api.Tests.csproj
dotnet test src/OpenKSeF.Domain.Tests/OpenKSeF.Domain.Tests.csproj

# Integration tests (requires Docker Desktop)
dotnet test src/OpenKSeF.Api.IntegrationTests/OpenKSeF.Api.IntegrationTests.csproj

# Portal Web
cd src/OpenKSeF.Portal.Web
npm ci
npm run lint
npm test
```

## Code style

- **.NET:** Follow standard C# conventions. Use `dotnet format` before committing.
- **TypeScript/React:** Follow the existing ESLint configuration. Run `npm run lint` before committing.
- **Commits:** Write clear, concise commit messages. Use imperative mood ("Add feature" not "Added feature").

## Pull request process

1. Ensure all tests pass
2. Update documentation if your change affects public APIs or configuration
3. Keep PRs focused -- one feature or fix per PR
4. Fill in the PR template describing your changes and how to test them
5. **Sign the CLA** -- the bot will comment on your first PR with instructions
6. A maintainer will review your PR and may request changes

## Reporting bugs

Use [GitHub Issues](../../issues) with the **Bug report** template. Include:

- Steps to reproduce
- Expected vs actual behavior
- Environment details (OS, .NET version, Docker version)

## Requesting features

Use [GitHub Issues](../../issues) with the **Feature request** template. Describe:

- The problem you're trying to solve
- Your proposed solution
- Any alternatives you've considered

## Security vulnerabilities

**Do NOT open a public issue for security vulnerabilities.** See [SECURITY.md](SECURITY.md) for responsible disclosure instructions.

## Contributor License Agreement (CLA)

Before your pull request can be merged, you must sign the
[Contributor License Agreement](CLA.md). This is a one-time step per
contributor.

When you open a PR, the CLA bot will post a comment with signing instructions.
You sign by replying to the comment with a specific phrase. Your signature is
recorded in this repository.

The CLA grants the copyright holder the right to dual-license your
contributions (Elastic License 2.0 and commercial), while you retain full copyright
ownership of your work.

## License

By contributing, you agree that your contributions will be licensed under the
[Elastic License 2.0 (ELv2)](LICENSE). The copyright holder reserves the right
to offer OpenKSeF under alternative commercial licensing terms. See [CLA.md](CLA.md).
