# Portal Migration (Blazor -> React)

## Cutover summary

- Cutover date: **February 28, 2026**
- Old portal: `src/OpenKSeF.Portal` (Blazor)
- New portal: `src/OpenKSeF.Portal.Web` (React)

## Runtime changes

- Gateway `/` route points to `portal-web` container.
- `docker-compose --profile app` no longer starts Blazor portal service.
- CI no longer builds/publishes Blazor portal Docker image.
- Keycloak realm no longer includes `openksef-portal` client; SPA uses `openksef-portal-web`.

## Service mapping

- Dashboard: `OpenKSeF.Portal/Components/Pages/Dashboard.razor` -> `OpenKSeF.Portal.Web/src/pages/Dashboard.tsx`
- Tenants: `.../Tenants/*.razor` -> `OpenKSeF.Portal.Web/src/pages/TenantList.tsx`
- Credentials: `.../Credentials/Index.razor` -> `OpenKSeF.Portal.Web/src/pages/CredentialList.tsx`
- Devices: `.../Devices/Index.razor` -> `OpenKSeF.Portal.Web/src/pages/DeviceList.tsx`
- Invoices: `.../Invoices/*.razor` -> `OpenKSeF.Portal.Web/src/pages/InvoiceList.tsx` and `InvoiceDetails.tsx`

## Rollback procedure

Rollback trigger examples:
- Critical production regression in invoice browsing
- Auth flow breakage for portal users
- Sustained gateway routing failures to SPA

Rollback steps:
1. Revert changes in `infra/nginx/default.conf` to point `/` back to Blazor service.
2. Restore `portal` service in `docker-compose.yml` and include it in runtime profile.
3. Restore Blazor build and publish steps in `.github/workflows/build.yml`.
4. Re-add Keycloak `openksef-portal` client in `keycloak/realm-openksef.json`.
5. Redeploy gateway and application containers.

Rollback support sunset:
- Planned sunset date is **March 30, 2026** (30 days after cutover).
