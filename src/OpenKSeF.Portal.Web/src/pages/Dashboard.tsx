import type { ReactElement } from 'react'
import { Link } from 'react-router-dom'

import { AsyncStateView } from '@/components/AsyncStateView'
import { StatusBadge } from '@/components/StatusBadge'
import { useDashboard } from '@/hooks/useDashboard'

function getTenantInitials(name: string | undefined | null, nip: string): string {
  if (name) {
    const parts = name.trim().split(/\s+/)
    if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase()
    return name.slice(0, 2).toUpperCase()
  }
  return nip.slice(0, 2)
}

function formatSyncDate(iso: string | null | undefined): string {
  if (!iso) return 'Nigdy'
  return new Date(iso).toLocaleString('pl-PL', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

export function DashboardPage(): ReactElement {
  const dashboardQuery = useDashboard()

  const items = dashboardQuery.data ?? []

  return (
    <section>
      <header className="page-header">
        <h1>Pulpit</h1>
        <button data-testid="dashboard-refresh-button" type="button" onClick={() => void dashboardQuery.refetch()}>
          ↺ Odśwież
        </button>
      </header>

      <AsyncStateView
        isLoading={dashboardQuery.isLoading}
        error={dashboardQuery.error}
        isEmpty={items.length === 0}
        loadingLines={8}
        emptyTitle="Brak skonfigurowanych firm"
        emptyMessage="Dodaj pierwszą firmę, aby rozpocząć synchronizację faktur i monitorowanie statusu."
        onRetry={() => void dashboardQuery.refetch()}
      >
        <div className="dashboard-grid">
          {items.map((item) => (
            <article key={item.tenantId} className="dashboard-card">
              <header className="dashboard-card-header">
                <div
                  className="dashboard-tenant-avatar"
                  aria-hidden="true"
                >
                  {getTenantInitials(item.displayName, item.nip)}
                </div>
                <div className="dashboard-tenant-info">
                  <div className="dashboard-tenant-name">{item.displayName || item.nip}</div>
                  <div className="dashboard-tenant-nip">{item.nip}</div>
                </div>
                {item.syncStatus === 'Success' ? (
                  <StatusBadge status="success" label="OK" />
                ) : item.syncStatus === 'Warning' ? (
                  <StatusBadge status="warning" label="Ostrzeżenie" />
                ) : (
                  <StatusBadge status="error" label="Błąd" />
                )}
              </header>

              <div className="dashboard-kpi-row">
                <div className="dashboard-kpi">
                  <span className="dashboard-kpi-value">{item.totalInvoices}</span>
                  <span className="dashboard-kpi-label">Łącznie</span>
                </div>
                <div className="dashboard-kpi">
                  <span className="dashboard-kpi-value">{item.invoicesLast7Days}</span>
                  <span className="dashboard-kpi-label">Ostatnie 7 dni</span>
                </div>
                <div className="dashboard-kpi">
                  <span className="dashboard-kpi-value">{item.invoicesLast30Days}</span>
                  <span className="dashboard-kpi-label">Ostatnie 30 dni</span>
                </div>
              </div>

              <div className="dashboard-sync-info">
                🕐 Ostatnia synchronizacja: <span>{formatSyncDate(item.lastSuccessfulSync)}</span>
              </div>

              <nav className="dashboard-links" aria-label={`Skróty dla ${item.displayName || item.nip}`}>
                <Link data-testid="dashboard-link-tenants" to="/tenants" className="dashboard-link">
                  Firmy
                </Link>
                <Link data-testid="dashboard-link-credentials" to="/credentials" className="dashboard-link">
                  Dane logowania
                </Link>
                <Link
                  data-testid="dashboard-link-invoices"
                  to={`/invoices?tenantId=${encodeURIComponent(item.tenantId)}`}
                  className="dashboard-link"
                >
                  Faktury
                </Link>
              </nav>
            </article>
          ))}
        </div>
      </AsyncStateView>
    </section>
  )
}
