import { useQuery } from '@tanstack/react-query'
import { useState, type ReactElement } from 'react'
import { Link } from 'react-router-dom'

import { listInvoices } from '@/api/endpoints/invoices'
import { listIssuedInvoices } from '@/api/endpoints/issuedInvoices'
import type { InvoiceResponse, IssuedInvoiceListItemResponse, TenantDashboardSummaryResponse } from '@/api/types'
import { useCompany } from '@/company/useCompany'
import { AsyncStateView } from '@/components/AsyncStateView'
import { Button } from '@/components/Button'
import { CompanyStateCard } from '@/components/CompanyStateCard'
import { ErrorBanner } from '@/components/ErrorBanner'
import { Icon, type IconName } from '@/components/Icon'
import { KsefStatusBadge } from '@/components/invoicing/KsefStatusBadge'
import '@/components/invoicing/invoicing.css'
import { KsefSync } from '@/components/KsefSync'
import { PaidStatus } from '@/components/PaidStatus'
import { Skeleton } from '@/components/Skeleton'
import { useCompanyState } from '@/hooks/useCompanyState'
import { useKsefFetch } from '@/hooks/useKsefFetch'
import { formatDate, formatDateTime, formatMoney } from '@/invoicing/format'

const WIDE_FROM_PX = 1800
const RESERVED_HEIGHT_PX = 640
const ROW_HEIGHT_PX = 53
const MIN_WIDE_ROWS = 5
const NARROW_ROWS = 6
const REJECTED_SHOWN = 5
const ISSUED_SCANNED = 100
const COMPANIES_SHOWN = 5

const rowsForViewport = (): number =>
  window.innerWidth >= WIDE_FROM_PX ? Math.max(MIN_WIDE_ROWS, Math.floor((window.innerHeight - RESERVED_HEIGHT_PX) / ROW_HEIGHT_PX)) : NARROW_ROWS

const invoicesWord = (count: number): string => {
  if (count === 1) return 'faktura'
  const lastDigit = count % 10
  const lastTwo = count % 100
  return lastDigit >= 2 && lastDigit <= 4 && (lastTwo < 12 || lastTwo > 14) ? 'faktury' : 'faktur'
}

const issuedPath = (row: IssuedInvoiceListItemResponse, tenantId: string): string =>
  `/issued/${encodeURIComponent(row.id)}?tenantId=${encodeURIComponent(tenantId)}`

interface AttentionItem {
  key: string
  icon: IconName
  tone: 'error' | 'warning'
  to: string
  title: string
  sub: string
}

function issuedAttentionItems(rejected: IssuedInvoiceListItemResponse[], issued: IssuedInvoiceListItemResponse[], tenantId: string): AttentionItem[] {
  const items: AttentionItem[] = rejected.map((row) => ({
    key: row.id,
    icon: 'x',
    tone: 'error',
    to: issuedPath(row, tenantId),
    title: `${row.number ?? '—'}: KSeF nie przyjął faktury`,
    sub: row.buyerName ?? '',
  }))
  // shortcut: API has no needsAttention filter, only the 100 latest by IssueDate are scanned, drafts included, so an older stuck invoice is missed; add filter when REQ-0042 allows API changes
  for (const row of issued) {
    if (!row.needsAttention) continue
    const base = { key: row.id, icon: 'triangle-alert', tone: 'warning', to: issuedPath(row, tenantId) } as const
    const number = row.number ?? '—'
    if (row.status === 'Sending') {
      items.push({ ...base, title: `${number}: wysyłanie trwa dłużej`, sub: `Nie wysyłaj faktury drugi raz. ${row.buyerName ?? ''}`.trim() })
    } else if (row.status === 'Accepted') {
      items.push({ ...base, title: `${number}: sprawdź status w KSeF`, sub: row.buyerName ?? '' })
    }
  }
  return items
}

interface AttentionCardProps {
  items: AttentionItem[]
  rejectedTotal: number
  isLoading: boolean
  error: unknown
  onRetry: () => void
  scanCapped: boolean
}

function AttentionCard({ items, rejectedTotal, isLoading, error, onRetry, scanCapped }: AttentionCardProps): ReactElement {
  return (
    <section className="start-card start-card--attention" aria-labelledby="start-attention">
      <h2 id="start-attention">Wymaga uwagi</h2>
      {error ? <ErrorBanner message={error instanceof Error ? error.message : 'Nie udało się załadować danych'} onRetry={onRetry} /> : null}
      {isLoading && items.length === 0 ? <Skeleton lines={2} /> : null}
      {items.length > 0 ? (
        <ul className="start-list">
          {items.map((item) => (
            <li key={`${item.key}-${item.title}`}>
              <span className={`start-attention-icon start-attention-icon--${item.tone}`}>
                <Icon name={item.icon} size={18} />
              </span>
              <span className="start-row__main">
                <Link to={item.to}>{item.title}</Link>
                {item.sub ? <span className="start-row__sub">{item.sub}</span> : null}
              </span>
            </li>
          ))}
        </ul>
      ) : null}
      {rejectedTotal > 0 ? (
        <Link className="start-more" to="/issued?status=Rejected">
          Wszystkie odrzucone ({rejectedTotal})
        </Link>
      ) : null}
      {!error && !isLoading && items.length === 0 ? (
        <p className="start-clear">
          <Icon name="circle-check" size={18} /> {scanCapped ? `W ${ISSUED_SCANNED} najnowszych fakturach wystawionych nic nie wymaga uwagi.` : 'Nic nie wymaga uwagi.'}
        </p>
      ) : null}
    </section>
  )
}

function CompaniesCard({ summaries, tenantId, onSelect }: { summaries: TenantDashboardSummaryResponse[]; tenantId: string; onSelect: (id: string) => void }): ReactElement {
  return (
    <section className="start-card start-card--companies" aria-labelledby="start-companies">
      <h2 id="start-companies">Twoje firmy</h2>
      <ul className="start-list">
        {summaries.slice(0, COMPANIES_SHOWN).map((row) => {
          const name = row.displayName || row.nip
          return (
            <li key={row.tenantId}>
              <span className="start-row__main">
                <b title={name}>{name}</b>
                <span className="start-row__sub">
                  NIP {row.nip} · {row.totalInvoices} {invoicesWord(row.totalInvoices)}, {row.invoicesLast7Days} z ostatnich 7 dni
                </span>
                {row.lastSuccessfulSync ? (
                  row.syncStatus === 'Success' ? (
                    <span className="start-row__sub">Pobrano z KSeF: {formatDateTime(row.lastSuccessfulSync)}</span>
                  ) : (
                    <span className="start-row__sub received-sync--stale">Nie pobieramy od {formatDateTime(row.lastSuccessfulSync)}</span>
                  )
                ) : null}
              </span>
              {row.tenantId === tenantId ? (
                <span className="start-row__current">Wybrana</span>
              ) : (
                <Button variant="outline" size="sm" aria-label={`Wybierz firmę: ${name}`} onClick={() => onSelect(row.tenantId)}>
                  Wybierz
                </Button>
              )}
            </li>
          )
        })}
      </ul>
      {summaries.length > COMPANIES_SHOWN ? (
        <p className="start-row__sub">Pozostałe: {summaries.length - COMPANIES_SHOWN}. Wybierzesz je w menu firmy.</p>
      ) : null}
    </section>
  )
}

function ReceivedRow({ row, tenantId, siblings }: { row: InvoiceResponse; tenantId: string; siblings: string[] }): ReactElement {
  return (
    <li>
      <span className="start-row__main">
        <Link
          to={`/invoices/${encodeURIComponent(row.ksefInvoiceNumber)}?tenantId=${encodeURIComponent(tenantId)}`}
          state={{ listSearch: '', siblings }}
          title={row.vendorName}
        >
          {row.vendorName}
        </Link>
        <span className="start-row__sub">
          {row.invoiceNumber ?? row.ksefInvoiceNumber} · {formatDate(row.issueDate)}
        </span>
      </span>
      <span className="start-row__end">
        <b>{formatMoney(row.amountGross, row.currency)}</b>
        <PaidStatus row={row} testId="start-paid-status" />
      </span>
    </li>
  )
}

export function StartPage(): ReactElement {
  const { tenantId, switchCompany } = useCompany()
  const { state, summary, summaries, credential, isLoading, error, refetch } = useCompanyState()
  const ksef = useKsefFetch(tenantId)
  const [rowCount] = useState(rowsForViewport)
  const hasTenant = Boolean(tenantId)
  const isFull = state === 'full' || state === null

  const rejectedQuery = useQuery({
    queryKey: ['issued-invoices', tenantId, 'start', 'rejected'],
    queryFn: () => listIssuedInvoices(tenantId, { page: 1, pageSize: REJECTED_SHOWN, status: 'Rejected' }),
    enabled: hasTenant,
  })
  const issuedQuery = useQuery({
    queryKey: ['issued-invoices', tenantId, 'start', 'recent'],
    queryFn: () => listIssuedInvoices(tenantId, { page: 1, pageSize: ISSUED_SCANNED }),
    enabled: hasTenant,
  })
  const receivedQuery = useQuery({
    queryKey: ['invoices', tenantId, 'start', rowCount],
    queryFn: () => listInvoices(tenantId, { page: 1, pageSize: rowCount }),
    enabled: hasTenant && !isLoading && isFull,
  })

  const issuedRows = issuedQuery.data?.items ?? []
  const received = receivedQuery.data?.items ?? []
  const siblings = received.map((row) => row.ksefInvoiceNumber)
  const recentIssued = issuedRows.filter((row) => row.status !== 'Draft').slice(0, rowCount)

  const attention = issuedAttentionItems(rejectedQuery.data?.items ?? [], issuedRows, tenantId)
  if (state === 'full' && credential && !credential.hasCredential) {
    attention.push({ key: 'token', icon: 'key-round', tone: 'error', to: '/credentials', title: 'Ta firma nie ma tokenu KSeF', sub: 'Dodaj token KSeF' })
  } else if (state !== 'no-token' && summary?.lastSuccessfulSync && summary.syncStatus !== 'Success') {
    attention.push({
      key: 'stale',
      icon: 'circle-alert',
      tone: 'error',
      to: '/credentials',
      title: `Nie pobieramy faktur od ${formatDateTime(summary.lastSuccessfulSync)}`,
      sub: 'Sprawdź token KSeF',
    })
  }
  const attentionError = rejectedQuery.error ?? issuedQuery.error
  const attentionLoading = rejectedQuery.isLoading || issuedQuery.isLoading
  const scanCapped = (issuedQuery.data?.totalCount ?? 0) > ISSUED_SCANNED
  const attentionClear = attention.length === 0 && !attentionError && !attentionLoading && !scanCapped
  const retryAttention = (): void => {
    void rejectedQuery.refetch()
    void issuedQuery.refetch()
  }
  const attentionCard = (
    <AttentionCard
      items={attention}
      rejectedTotal={rejectedQuery.data?.totalCount ?? 0}
      isLoading={attentionLoading}
      error={attentionError}
      onRetry={retryAttention}
      scanCapped={scanCapped}
    />
  )

  return (
    <section className={isFull && !isLoading && !error ? 'start start--full' : 'start'}>
      <header className="page-header received-head">
        <h1>Co dziś do zrobienia</h1>
        <KsefSync state={state} summary={summary} ksef={ksef} disabled={!tenantId} variant="outline" />
        <Link to="/issued/new" className="ui-button ui-button--primary ui-button--md">
          <Icon name="plus" /> Nowa faktura
        </Link>
      </header>

      <AsyncStateView isLoading={isLoading} error={error} isEmpty={false} loadingLines={8} emptyTitle="" emptyMessage="" onRetry={refetch}>
        {state && state !== 'full' ? (
          <>
            {attention.length > 0 || attentionError ? attentionCard : null}
            <CompanyStateCard state={state} place="start" summary={summary} credential={credential} attentionClear={attentionClear} />
          </>
        ) : (
          <div className={summaries.length >= 2 ? 'start-grid' : 'start-grid start-grid--solo'}>
            {attentionCard}
            <section className="start-card start-card--received" aria-labelledby="start-received">
              <h2 id="start-received">Ostatnio otrzymane</h2>
              <AsyncStateView
                isLoading={receivedQuery.isLoading}
                error={receivedQuery.error}
                isEmpty={received.length === 0}
                emptyTitle="Nie znaleziono faktur"
                emptyMessage="Faktury pojawią się tutaj po pobraniu z KSeF."
                onRetry={() => void receivedQuery.refetch()}
              >
                <ul className="start-list">
                  {received.map((row) => (
                    <ReceivedRow key={row.id} row={row} tenantId={tenantId} siblings={siblings} />
                  ))}
                </ul>
              </AsyncStateView>
              <Link className="start-more" to="/invoices">
                Wszystkie otrzymane
              </Link>
            </section>
            <section className="start-card start-card--issued" aria-labelledby="start-issued">
              <h2 id="start-issued">Ostatnio wystawione</h2>
              <AsyncStateView
                isLoading={issuedQuery.isLoading}
                error={issuedQuery.error}
                isEmpty={recentIssued.length === 0}
                emptyTitle="Nie masz jeszcze wystawionych faktur"
                emptyMessage="Wystawiona faktura pojawi się tutaj razem ze statusem w KSeF."
                onRetry={() => void issuedQuery.refetch()}
              >
                <ul className="start-list">
                  {recentIssued.map((row) => (
                    <li key={row.id}>
                      <span className="start-row__main">
                        <Link to={issuedPath(row, tenantId)}>{row.number ?? '—'}</Link>
                        <span className="start-row__sub">{row.buyerName ?? '—'}</span>
                      </span>
                      <span className="start-row__end">
                        <b>{formatMoney(row.totalGross, row.currency)}</b>
                        <KsefStatusBadge status={row.status} />
                      </span>
                    </li>
                  ))}
                </ul>
              </AsyncStateView>
              <Link className="start-more" to="/issued">
                Wszystkie wystawione
              </Link>
            </section>
            {summaries.length >= 2 ? <CompaniesCard summaries={summaries} tenantId={tenantId} onSelect={switchCompany} /> : null}
          </div>
        )}
      </AsyncStateView>
    </section>
  )
}
