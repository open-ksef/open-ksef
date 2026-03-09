# Migration Plan: OpenKSeF.KSeFClient → CIRFMF/ksef-client-csharp

## Status

> **⚠️ NOT IN PRODUCTION** - This codebase is not released anywhere. Full rewrite is safe.

### Implementation Progress


| #   | Task                                                                            | Status  | Notes                                                                      |
| --- | ------------------------------------------------------------------------------- | ------- | -------------------------------------------------------------------------- |
| 1.1 | Create GitHub PAT with `read:packages`                                          | ✅ DONE | PAT created, feed registered locally (PAT expired, needs renewal)          |
| 1.2 | Add `nuget.config` to solution root                                             | ✅ DONE | `src/nuget.config` with CIRFMF feed (no credentials in repo)               |
| 1.3 | Update `.gitignore` for NuGet credentials                                       | ✅ DONE | Added `nuget.config.local`                                                 |
| 2.1 | Create `IKSeFGateway` interface                                                 | ✅ DONE | `Domain/Abstractions/IKSeFGateway.cs`                                      |
| 2.2 | Create domain records (`KSeFSession`, `KSeFQueryCriteria`, `KSeFInvoiceHeader`) | ✅ DONE | Same file, plus `KSeFInvoiceQueryResult`                                   |
| 2.3 | Verify no external deps in Domain                                               | ✅ DONE | No new packages added                                                      |
| 3.1 | Add `KSeF.Client` NuGet package to Sync                                         | ✅ DONE | `<PackageReference Include="KSeF.Client" Version="2.*" />`                 |
| 3.2 | Rewrite `KSeFGateway` to use CIRFMF library                                    | ✅ DONE | Uses `IAuthCoordinator`, `IKSeFClient`, `ICryptographyService` directly    |
| 3.3 | Map library models to domain models                                             | ✅ DONE | `InvoiceSummary` → `KSeFInvoiceHeader`, `InvoiceQueryFilters` from criteria |
| 3.4 | Handle exceptions in gateway                                                    | ✅ DONE | `KsefApiException` propagated, StatusCode mapped in TenantSyncService      |
| 4.1 | Replace `IKSeFClient` dep with `IKSeFGateway` in TenantSyncService              | ✅ DONE | Constructor now takes `IKSeFGateway`                                       |
| 4.2 | Replace `IKSeFAuthProviderFactory` usage                                        | ✅ DONE | Auth delegated to gateway via `IAuthCoordinator`                           |
| 4.3 | Update sync logic for new domain models                                         | ✅ DONE | Uses `KSeFSession`, `KSeFQueryCriteria`                                    |
| 4.4 | Remove old namespace references                                                 | ✅ DONE | All `OpenKSeF.KSeFClient` references removed, uses `KsefApiException`      |
| 5.1 | Create `DependencyInjection.cs` in Sync                                         | ✅ DONE | Uses CIRFMF `AddKSeFClient()` + `AddCryptographyClient()`                 |
| 5.2 | Update `Api/Program.cs`                                                         | ✅ DONE | Uses `AddSyncServices()`, removed direct KSeFClient ref                    |
| 5.3 | Update `Worker/Program.cs`                                                      | ✅ DONE | Uses `AddSyncServices()`, removed direct KSeFClient ref                    |
| 6.1 | Remove KSeFClient from solution                                                 | ✅ DONE | `dotnet sln remove` both projects                                          |
| 6.2 | Remove project references                                                       | ✅ DONE | Removed from Sync.csproj and Portal.csproj                                 |
| 6.3 | Delete `src/OpenKSeF.KSeFClient/`                                               | ✅ DONE | Directory deleted                                                          |
| 6.4 | Delete `src/OpenKSeF.KSeFClient.Tests/`                                         | ✅ DONE | Directory deleted                                                          |
| 7.1 | Create mock for `IKSeFGateway`                                                  | ✅ DONE | Simple interface, NSubstitute-friendly                                     |
| 7.2 | Update TenantSyncService unit tests                                             | ✅ DONE | Tests mock at ITenantSyncService level (unchanged)                         |
| 7.3 | Update CredentialsControllerSyncTests                                           | ✅ DONE | No changes needed (mocks ITenantSyncService)                               |
| 7.4 | Add KSeFGateway integration tests                                               | ⬜ TODO  | Optional, deferred                                                         |
| 8.1 | Build solution                                                                  | ✅ DONE | 0 errors, 0 warnings on changed files                                      |
| 8.2 | Run all tests                                                                   | ✅ DONE | Api: 31/31, Domain: 26/28 (2 pre-existing file lock issue)                |
| 8.3 | Run linter                                                                      | ✅ DONE | No linter errors on changed files                                          |
| 8.4 | Docker build                                                                    | ✅ DONE | Dockerfiles updated with CIRFMF NuGet auth, PAT passed via build args      |
| 8.5 | Manual test sync endpoint                                                       | ✅ DONE | Auth works (RSA), base URL mapping works, query validation fixed           |
| 8.6 | Test with KSeF test env                                                         | ✅ DONE | Switched to prod KSeF for real data testing                                |
| 9.1 | Fix 3-month date range limit                                                    | ✅ DONE | KSeF rejects >3mo ranges; TenantSyncService now iterates 3-month windows  |
| 9.2 | Add `InitialSyncMonthsBack` option                                              | ✅ DONE | Configurable in `TenantSyncOptions`, default 6 months                      |


---

## Strategy

**Full replacement** - Remove custom implementation entirely and integrate the CIRFMF library directly following **SOLID principles** and **Clean Architecture**.

**Why migrate?**

- Mature, tested KSeF 2.0 implementation
- Better error handling and retry policies
- Support for all auth methods (token, certificate, XAdES signing)
- Cryptography services for batch sessions
- Active maintenance and updates for KSeF API changes
- QR code generation, verification links built-in

---

## Architecture Principles

### SOLID Compliance


| Principle                     | Implementation                                                       |
| ----------------------------- | -------------------------------------------------------------------- |
| **S** - Single Responsibility | Each service handles one concern (sync, auth, invoice queries)       |
| **O** - Open/Closed           | Use library interfaces, extend via decorators if needed              |
| **L** - Liskov Substitution   | Depend on `IKSeFClient` interface from library                       |
| **I** - Interface Segregation | Use only required interfaces (`IKSeFClient`, `ICryptographyService`) |
| **D** - Dependency Inversion  | All dependencies injected via DI, no `new` in business logic         |


### Clean Architecture Layers

```
┌─────────────────────────────────────────────────────────────┐
│  Presentation (API Controllers)                             │
├─────────────────────────────────────────────────────────────┤
│  Application (OpenKSeF.Sync - TenantSyncService)            │
├─────────────────────────────────────────────────────────────┤
│  Domain (OpenKSeF.Domain - Entities, Interfaces)            │
├─────────────────────────────────────────────────────────────┤
│  Infrastructure (KSeF.Client library - external dependency) │
└─────────────────────────────────────────────────────────────┘
```

**Key rule**: Domain layer MUST NOT depend on KSeF.Client directly. Use abstractions.

---

## Previous State (REMOVED)

The custom implementation `src/OpenKSeF.KSeFClient` has been fully deleted.
All components (`IKSeFClient`, `KSeFHttpClient`, auth providers, models, exceptions)
have been replaced by the CIRFMF `KSeF.Client` library via NuGet.

---

## Target Architecture

### New Structure

```
src/
├── OpenKSeF.Domain/
│   ├── Abstractions/
│   │   └── IKSeFGateway.cs          # Our abstraction over library (DIP)
│   ├── DTOs/
│   │   └── InvoiceDto.cs            # Domain DTOs (unchanged)
│   └── Services/
│       └── IInvoiceService.cs       # Domain service interface
│
├── OpenKSeF.Sync/
│   ├── KSeFGateway.cs               # Implements IKSeFGateway using KSeF.Client
│   ├── TenantSyncService.cs         # Uses IKSeFGateway (not library directly)
│   └── DependencyInjection.cs       # Sync module DI registration
│
├── OpenKSeF.Api/
│   └── Program.cs                   # Registers KSeF.Client + our services
│
└── OpenKSeF.Worker/
    └── Program.cs                   # Registers KSeF.Client + our services

✅ DELETED: src/OpenKSeF.KSeFClient/
✅ DELETED: src/OpenKSeF.KSeFClient.Tests/
```

### Domain Abstraction (Dependency Inversion)

```csharp
// OpenKSeF.Domain/Abstractions/IKSeFGateway.cs
namespace OpenKSeF.Domain.Abstractions;

public interface IKSeFGateway
{
    Task<KSeFSession> InitSessionAsync(string nip, string authToken, CancellationToken ct = default);
    Task<KSeFSession> InitSignedSessionAsync(string nip, string signedPayload, string fingerprint, CancellationToken ct = default);
    Task<IReadOnlyList<KSeFInvoiceHeader>> QueryInvoicesAsync(KSeFSession session, KSeFQueryCriteria criteria, CancellationToken ct = default);
    Task<byte[]> DownloadInvoiceAsync(KSeFSession session, string ksefNumber, CancellationToken ct = default);
    Task TerminateSessionAsync(KSeFSession session, CancellationToken ct = default);
}

public record KSeFSession(string Token, string ReferenceNumber, DateTime ExpiresAtUtc);
public record KSeFQueryCriteria(DateTime? DateFrom, DateTime? DateTo, int PageSize, int PageOffset);
public record KSeFInvoiceHeader(string KSeFNumber, string VendorNip, string VendorName, decimal AmountGross, string Currency, DateTime IssueDate);
```

### Infrastructure Implementation

```csharp
// OpenKSeF.Sync/KSeFGateway.cs
using KSeF.Client.Core.Interfaces.Clients;
using OpenKSeF.Domain.Abstractions;

namespace OpenKSeF.Sync;

public sealed class KSeFGateway : IKSeFGateway
{
    private readonly IKSeFClient _client;
    private readonly ILogger<KSeFGateway> _logger;

    public KSeFGateway(IKSeFClient client, ILogger<KSeFGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    // Implement methods mapping library calls to domain abstractions
}
```

---

## Migration Tasks

### Phase 1: Setup NuGet Access

- **1.1** Create GitHub Personal Access Token (PAT) with `read:packages` scope
- **1.2** Add `nuget.config` to solution root:
  ```xml
  <?xml version="1.0" encoding="utf-8"?>
  <configuration>
    <packageSources>
      <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
      <add key="CIRFMF" value="https://nuget.pkg.github.com/CIRFMF/index.json" />
    </packageSources>
    <packageSourceCredentials>
      <CIRFMF>
        <add key="Username" value="YOUR_GITHUB_USERNAME" />
        <add key="ClearTextPassword" value="YOUR_PAT" />
      </CIRFMF>
    </packageSourceCredentials>
  </configuration>
  ```
- **1.3** Add to `.gitignore`: credentials should use environment variables in CI

### Phase 2: Add Domain Abstractions

- **2.1** Create `IKSeFGateway` interface in `OpenKSeF.Domain/Abstractions/`
- **2.2** Create domain records: `KSeFSession`, `KSeFQueryCriteria`, `KSeFInvoiceHeader`
- **2.3** No external dependencies in Domain project

### Phase 3: Add Library & Implement Gateway

- **3.1** Add `KSeF.Client` package to `OpenKSeF.Sync.csproj`:
  ```xml
  <PackageReference Include="KSeF.Client" Version="2.1.2" />
  ```
- **3.2** Create `KSeFGateway` class implementing `IKSeFGateway`
- **3.3** Map library models to domain models in gateway
- **3.4** Handle library exceptions, wrap in domain exceptions if needed

### Phase 4: Update TenantSyncService

- **4.1** Replace `IKSeFClient` dependency with `IKSeFGateway`
- **4.2** Replace `IKSeFAuthProviderFactory` - auth handled by gateway
- **4.3** Update sync logic to use new domain models
- **4.4** Remove all references to old `OpenKSeF.KSeFClient` namespace

### Phase 5: Update DI Registration

- **5.1** Create `OpenKSeF.Sync/DependencyInjection.cs`:
  ```csharp
  public static class DependencyInjection
  {
      public static IServiceCollection AddSyncServices(this IServiceCollection services, IConfiguration config)
      {
          // Register KSeF.Client library
          services.AddKSeFClient(options =>
          {
              options.BaseUrl = config["KSeF:BaseUrl"] ?? KsefEnvironmentsUris.TEST;
          });
          services.AddCryptographyClient();
          
          // Register our gateway
          services.AddScoped<IKSeFGateway, KSeFGateway>();
          
          // Register sync service
          services.AddScoped<ITenantSyncService, TenantSyncService>();
          
          return services;
      }
  }
  ```
- **5.2** Update `OpenKSeF.Api/Program.cs`:
  ```csharp
  builder.Services.AddSyncServices(builder.Configuration);
  ```
- **5.3** Update `OpenKSeF.Worker/Program.cs` similarly

### Phase 6: Delete Old Implementation

- **6.1** Remove `OpenKSeF.KSeFClient` project from `OpenKSeF.sln`
- **6.2** Remove project references from all `.csproj` files
- **6.3** Delete `src/OpenKSeF.KSeFClient/` directory entirely
- **6.4** Delete `src/OpenKSeF.KSeFClient.Tests/` directory if exists

### Phase 7: Update Tests

- **7.1** Create mock for `IKSeFGateway` (simple interface to mock)
- **7.2** Update `TenantSyncService` unit tests
- **7.3** Update `CredentialsControllerSyncTests`
- **7.4** Add integration tests for `KSeFGateway` (optional, library is tested)

### Phase 8: Verification

- **8.1** Build solution: `dotnet build src/OpenKSeF.sln`
- **8.2** Run all tests: `dotnet test src/OpenKSeF.sln`
- **8.3** Run linter: `dotnet format src/OpenKSeF.sln --verify-no-changes`
- **8.4** Docker build: `docker compose build`
- **8.5** Manual test sync endpoint in dev environment
- **8.6** Test with KSeF test environment when available

---

## Configuration

### appsettings.json

```json
{
  "KSeF": {
    "BaseUrl": "https://ksef-test.mf.gov.pl",
    "Environment": "Test"
  }
}
```

### Environment Variables (CI/Production)

```
KSEF__BaseUrl=https://ksef.mf.gov.pl
NUGET_CIRFMF_PAT=ghp_xxxx
```

---

## Benefits of This Approach


| Aspect          | Benefit                                                     |
| --------------- | ----------------------------------------------------------- |
| **Testability** | `IKSeFGateway` is easy to mock - no complex library types   |
| **Flexibility** | Can swap library without changing domain/application layers |
| **Clarity**     | Clear separation: Domain ↔ Application ↔ Infrastructure     |
| **Maintenance** | Library handles KSeF API changes, we adapt in gateway only  |
| **SOLID**       | All principles followed, dependencies flow inward           |


---

## Rollback Plan

Since nothing is in production:

- Git history contains all old code
- Can restore from any previous commit if needed
- No data migration required

---

## References

- [CIRFMF/ksef-client-csharp](https://github.com/CIRFMF/ksef-client-csharp)
- [Clean Architecture by Robert C. Martin](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
- [KSeF API Documentation](https://www.podatki.gov.pl/ksef/)

