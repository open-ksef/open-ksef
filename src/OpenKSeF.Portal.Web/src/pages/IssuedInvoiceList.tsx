import { useQuery } from '@tanstack/react-query'
import { useEffect, useState, type ReactElement } from 'react'
import { Link, Navigate, useLocation, useSearchParams } from 'react-router-dom'

import { listIssuedFeed } from '@/api/endpoints/issuedInvoices'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import type { IssuedFeedItemResponse, IssuedInvoiceStatus } from '@/api/types'
import { useCompany } from '@/company/useCompany'
import { AsyncStateView } from '@/components/AsyncStateView'
import { Button } from '@/components/Button'
import { EmptyState } from '@/components/EmptyState'
import { Icon } from '@/components/Icon'
import { EnvBadge, KsefStatusBadge } from '@/components/invoicing/KsefStatusBadge'
import '@/components/invoicing/invoicing.css'
import { Table, type TableColumn } from '@/components/Table'
import { useCredentials } from '@/hooks/useCredentials'
import { selectWordAt, useRowOpen } from '@/hooks/useRowOpen'
import { useTenants } from '@/hooks/useTenants'
import { formatDate, formatMoney } from '@/invoicing/format'

const PAGE_SIZE = 20
const SEARCH_DELAY_MS = 300

const tabs: { status: IssuedInvoiceStatus | ''; label: string }[] = [
  { status: '', label: 'Wszystkie' },
  { status: 'Draft', label: 'Szkice' },
  { status: 'Sending', label: 'Wysyłane' },
  { status: 'Accepted', label: 'Przyjęte' },
  { status: 'Rejected', label: 'Odrzucone' },
]

function lastPageSearch(current: URLSearchParams, lastPage: number): string {
  const next = new URLSearchParams(current)
  if (lastPage > 1) next.set('page', String(lastPage))
  else next.delete('page')
  return next.toString()
}

function ExternalMark({ row }: { row: IssuedFeedItemResponse }): ReactElement | null {
  return row.source === 'External' ? (
    <span className="received-sub" title="Faktura wystawiona w innym programie, tylko do odczytu">
      Spoza OpenKSeF
    </span>
  ) : null
}

export function IssuedInvoiceListPage(): ReactElement {
  const [searchParams, setSearchParams] = useSearchParams()
  const location = useLocation()
  const tenantsQuery = useTenants()
  const { tenantId } = useCompany()
  const status = (searchParams.get('status') ?? '') as IssuedInvoiceStatus | ''
  const month = searchParams.get('month') ?? ''
  const q = searchParams.get('q') ?? ''
  const page = Math.max(1, Number(searchParams.get('page')) || 1)

  const profileQuery = useQuery({
    queryKey: ['seller-profile', tenantId],
    queryFn: () => getSellerProfile(tenantId),
    enabled: Boolean(tenantId),
  })
  const listQuery = useQuery({
    queryKey: ['issued-invoices', tenantId, status, month, q, page],
    queryFn: () =>
      listIssuedFeed(tenantId, {
        page,
        pageSize: PAGE_SIZE,
        status: status || undefined,
        month: month || undefined,
        q: q || undefined,
      }),
    enabled: Boolean(tenantId),
  })

  const update = (next: { status?: string; month?: string; q?: string; page?: number }): void => {
    const params = new URLSearchParams()
    const values = { tenantId, status, month, q, page, ...next }
    if (values.tenantId) params.set('tenantId', values.tenantId)
    if (values.status) params.set('status', values.status)
    if (values.month) params.set('month', values.month)
    if (values.q) params.set('q', values.q)
    if (values.page > 1) params.set('page', String(values.page))
    setSearchParams(params)
  }

  const [draft, setDraft] = useState(q)
  const [urlTerm, setUrlTerm] = useState(q)
  if (q !== urlTerm) {
    setUrlTerm(q)
    if (q !== draft.trim()) setDraft(q)
  }

  useEffect(() => {
    const term = draft.trim()
    if (term === q) return
    const timer = setTimeout(() => update({ q: term, page: 1 }), SEARCH_DELAY_MS)
    return () => clearTimeout(timer)
  })

  const credentialsQuery = useCredentials()
  const tenantQuery = `tenantId=${encodeURIComponent(tenantId)}`
  const rows = listQuery.data?.items ?? []
  const totalPages = Math.max(listQuery.data?.totalPages ?? 1, 1)
  const unfilteredEmpty = listQuery.isSuccess && rows.length === 0 && !status && !month && !q && page === 1
  const hasCredential = credentialsQuery.data?.find((row) => row.tenantId === tenantId)?.hasCredential
  const incomplete = listQuery.data?.issuedFetchIncomplete ?? false
  const stateCard = unfilteredEmpty && credentialsQuery.isSuccess && !incomplete
  const totalCount = listQuery.data?.totalCount ?? 0
  const firstRow = (page - 1) * PAGE_SIZE + 1
  const pastLastPage = listQuery.isSuccess && rows.length === 0 && totalCount > 0 && page > totalPages
  const detailsPath = (row: IssuedFeedItemResponse): string =>
    row.source === 'External'
      ? `/invoices/${encodeURIComponent(row.ksefNumber!)}?${tenantQuery}`
      : `/issued/${encodeURIComponent(row.id)}?${tenantQuery}`
  const onRowClick = useRowOpen(detailsPath, { listSearch: location.search, fromIssued: true })

  const columns: TableColumn<IssuedFeedItemResponse>[] = [
    {
      key: 'buyerName',
      label: 'Nabywca i numer',
      render: (row) => {
        const number = row.number ?? '— (szkic)'
        return (
          <>
            <Link
              to={detailsPath(row)}
              className="rowlink"
              title={row.buyerName ?? undefined}
              aria-label={`${row.buyerName ?? 'Bez nabywcy'}, ${number}`}
              onClick={(event) => event.preventDefault()}
              onDoubleClick={selectWordAt}
            >
              {row.buyerName ?? '—'}
            </Link>
            <span className="received-sub" title={number}>
              {number} · {formatDate(row.issueDate)}
            </span>
          </>
        )
      },
    },
    { key: 'dueDate', label: 'Termin płatności', className: 'issued-due', render: (row) => formatDate(row.dueDate) },
    {
      key: 'totalNet',
      label: 'Netto',
      className: 'received-net received-num',
      render: (row) => formatMoney(row.totalNet, row.currency),
    },
    {
      key: 'totalGross',
      label: 'Brutto',
      className: 'received-gross received-num',
      render: (row) => (
        <>
          <span className="received-amount" title={formatMoney(row.totalGross, row.currency)}>
            {formatMoney(row.totalGross, row.currency)}
          </span>
          <span className="received-status-mobile">
            <KsefStatusBadge status={row.status} />
            <ExternalMark row={row} />
          </span>
        </>
      ),
    },
    {
      key: 'status',
      label: 'Status',
      className: 'issued-status',
      render: (row) => (
        <>
          <KsefStatusBadge status={row.status} />
          <ExternalMark row={row} />
          {row.needsAttention ? <span className="issued-attention">Wymaga uwagi</span> : null}
        </>
      ),
    },
    {
      key: 'ksefNumber',
      label: 'Numer KSeF',
      className: 'received-ksef',
      render: (row) => <span>{row.ksefNumber ?? '—'}</span>,
    },
  ]

  return (
    <div className="invoicing">
      <header className="page-header received-head">
        <h1>Wystawione faktury</h1>
        {profileQuery.data ? <EnvBadge environment={profileQuery.data.ksefEnvironment} /> : null}
        <Link to={`/issued/new?${tenantQuery}`} className="ui-button ui-button--primary ui-button--md">
          <Icon name="plus" /> Nowa faktura
        </Link>
      </header>

      {pastLastPage ? <Navigate to={{ search: lastPageSearch(searchParams, totalPages) }} replace /> : null}

      {stateCard ? null : (
        <div className="received-tools">
          <div className="inv-chips" role="group" aria-label="Status">
            {tabs.map((tab) => (
              <button key={tab.label} type="button" aria-pressed={status === tab.status} onClick={() => update({ status: tab.status, page: 1 })}>
                {tab.label}
              </button>
            ))}
          </div>
          <label htmlFor="issued-q" className="visually-hidden">
            Szukaj: numer, nabywca, NIP
          </label>
          <input id="issued-q" type="search" placeholder="Numer, nabywca, NIP" value={draft} onChange={(event) => setDraft(event.target.value)} />
          <label htmlFor="issued-month" className="visually-hidden">
            Miesiąc wystawienia
          </label>
          <input id="issued-month" type="month" value={month} onChange={(event) => update({ month: event.target.value, page: 1 })} />
        </div>
      )}

      {incomplete ? (
        <div className="inv-alert inv-alert--info company-note" role="status" data-testid="issued-fetch-incomplete">
          <Icon name="clock" />
          <span>Pobieranie faktur sprzedaży z KSeF jeszcze trwa. Lista może być niepełna.</span>
        </div>
      ) : null}

      {stateCard ? (
        <EmptyState
          icon="send"
          title="Nie masz jeszcze wystawionych faktur"
          message={
            hasCredential === false
              ? 'Możesz przygotować szkic. Do wysłania do KSeF potrzebny jest token z uprawnieniem zapisu.'
              : 'Wystawiona faktura pojawi się tutaj razem ze statusem w KSeF. Sprzedaż z innych programów zobaczysz po pobraniu z KSeF.'
          }
          actions={
            <div className="ui-empty-state__actions">
              <Link to={`/issued/new?${tenantQuery}`} className="ui-button ui-button--primary ui-button--md">
                <Icon name="plus" /> Nowa faktura
              </Link>
            </div>
          }
        />
      ) : (
        <AsyncStateView
          isLoading={tenantsQuery.isLoading || listQuery.isLoading || pastLastPage || (unfilteredEmpty && credentialsQuery.isLoading)}
          error={tenantsQuery.error ?? listQuery.error ?? (unfilteredEmpty ? credentialsQuery.error : null)}
          isEmpty={rows.length === 0 && !incomplete}
          emptyTitle="Brak faktur spełniających kryteria"
          emptyMessage="Zmień filtry lub kliknij „Nowa faktura”."
          onRetry={() => {
            void listQuery.refetch()
            void credentialsQuery.refetch()
          }}
        >
          {rows.length > 0 ? (
            <section className="received-list" aria-label="Lista faktur">
              <Table testId="issued-invoice-table" columns={columns} data={rows} onRowClick={onRowClick} />
              <nav className="ui-pagination" aria-label="Paginacja wystawionych faktur">
                <span className="ui-pagination-info">
                  {firstRow}–{firstRow + rows.length - 1} z {totalCount} {totalCount === 1 ? 'faktury' : 'faktur'}
                </span>
                <div className="ui-pagination-controls">
                  <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => update({ page: page - 1 })}>
                    Poprzednia
                  </Button>
                  <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => update({ page: page + 1 })}>
                    Następna
                  </Button>
                </div>
              </nav>
            </section>
          ) : null}
        </AsyncStateView>
      )}
    </div>
  )
}
