import type { ReactElement } from 'react'
import { Link } from 'react-router-dom'

import type { TenantDashboardSummaryResponse } from '@/api/types'
import type { CompanyState } from '@/hooks/useCompanyState'
import type { KsefFetch } from '@/hooks/useKsefFetch'
import { formatDateTime } from '@/invoicing/format'
import { Button } from './Button'
import { Icon } from './Icon'

interface KsefSyncProps {
  state: CompanyState | null
  summary: TenantDashboardSummaryResponse | undefined
  ksef: KsefFetch
  disabled: boolean
  variant: 'primary' | 'outline'
}

export function KsefSync({ state, summary, ksef, disabled, variant }: KsefSyncProps): ReactElement {
  return (
    <>
      <div className="received-sync">
        {summary?.lastSuccessfulSync && state !== 'no-token' ? (
          summary.syncStatus === 'Success' ? (
            <span data-testid="invoice-sync-line">Pobrano z KSeF: {formatDateTime(summary.lastSuccessfulSync)}</span>
          ) : (
            <span data-testid="invoice-sync-line" className="received-sync--stale">
              <Icon name="circle-alert" size={14} /> Nie pobieramy faktur od {formatDateTime(summary.lastSuccessfulSync)}
              <br />
              <Link to="/credentials">Sprawdź token KSeF</Link>
            </span>
          )
        ) : null}
        <span role="status" data-testid="invoice-fetch-note" className={ksef.missingToken ? 'received-sync--stale' : undefined}>
          {ksef.missingToken ? (
            <>
              Ta firma nie ma tokenu KSeF. <Link to="/credentials">Dodaj token KSeF</Link>
            </>
          ) : ksef.fetched ? (
            `Pobrane: ${ksef.fetched.fetchedInvoices}, nowe: ${ksef.fetched.newInvoices}.`
          ) : null}
        </span>
      </div>
      <Button
        variant={variant}
        data-testid="invoice-fetch-button"
        disabled={disabled || state === 'no-token' || ksef.pending}
        onClick={ksef.start}
      >
        <Icon name="cloud-download" />
        {ksef.pending ? 'Pobieranie…' : 'Pobierz z KSeF'}
      </Button>
    </>
  )
}
