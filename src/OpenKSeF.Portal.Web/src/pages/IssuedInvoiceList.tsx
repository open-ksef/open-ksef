import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type ReactElement } from 'react'
import { Link, Navigate, useLocation, useNavigate, useSearchParams } from 'react-router-dom'

import { copyIssuedInvoice, createAdvance, listIssuedFeed } from '@/api/endpoints/issuedInvoices'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import type { IssuedFeedItemResponse, IssuedInvoiceStatus } from '@/api/types'
import { useCompany } from '@/company/useCompany'
import { AsyncStateView } from '@/components/AsyncStateView'
import { Button } from '@/components/Button'
import { AddToken } from '@/components/CompanyStateCard'
import { EmptyState } from '@/components/EmptyState'
import { ErrorBanner } from '@/components/ErrorBanner'
import { Icon } from '@/components/Icon'
import { EnvBadge, KsefStatusBadge } from '@/components/invoicing/KsefStatusBadge'
import '@/components/invoicing/invoicing.css'
import { Skeleton } from '@/components/Skeleton'
import { Table, type TableColumn } from '@/components/Table'
import { useCredentials } from '@/hooks/useCredentials'
import { selectWordAt, useRowOpen } from '@/hooks/useRowOpen'
import { useTenants } from '@/hooks/useTenants'
import { LIST_KIND_LABELS } from '@/invoicing/advance'
import { isCorrection } from '@/invoicing/correction'
import { formatDate, formatMoney, formatSignedMoney } from '@/invoicing/format'

const PAGE_SIZE = 20
const MAX_ROWS = 10_000
const SEARCH_DELAY_MS = 300

const tabs: { status: IssuedInvoiceStatus | ''; label: string }[] = [
  { status: '', label: 'Wszystkie' },
  { status: 'Draft', label: 'Szkice' },
  { status: 'Sending', label: 'Wysyłane' },
  { status: 'Accepted', label: 'Przyjęte' },
  { status: 'Rejected', label: 'Odrzucone' },
]

const proformaTabs: { status: IssuedInvoiceStatus | ''; label: string }[] = [
  { status: '', label: 'Wszystkie' },
  { status: 'Draft', label: 'Szkice' },
  { status: 'Issued', label: 'Wystawione' },
]

const PROFORMA_VIEW = 'proformy'

function lastPageSearch(current: URLSearchParams, lastPage: number): string {
  const next = new URLSearchParams(current)
  if (lastPage > 1) next.set('page', String(lastPage))
  else next.delete('page')
  return next.toString()
}

const amount = (row: IssuedFeedItemResponse, value: number): string =>
  isCorrection(row) ? formatSignedMoney(value, row.currency) : formatMoney(value, row.currency)

const canCopy = (row: IssuedFeedItemResponse): boolean =>
  row.source === 'OpenKSeF' && (((row.invoiceType ?? 'VAT') === 'VAT' && row.status === 'Accepted') || (row.invoiceType === 'PRO' && row.status === 'Issued'))

const EXTERNAL_HINT = 'Faktura wystawiona w innym programie, tylko do odczytu'

function ExternalMark({ row }: { row: IssuedFeedItemResponse }): ReactElement | null {
  return row.source === 'External' ? (
    <span className="received-sub" title={EXTERNAL_HINT}>
      Spoza OpenKSeF
    </span>
  ) : null
}

export function IssuedInvoiceListPage(): ReactElement {
  const [searchParams, setSearchParams] = useSearchParams()
  const location = useLocation()
  const tenantsQuery = useTenants()
  const { tenantId } = useCompany()
  const proformas = searchParams.get('view') === PROFORMA_VIEW
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
    queryKey: ['issued-invoices', tenantId, proformas, status, month, q, page],
    queryFn: () =>
      listIssuedFeed(tenantId, {
        page,
        pageSize: PAGE_SIZE,
        status: status || undefined,
        month: month || undefined,
        q: q || undefined,
        invoiceType: proformas ? 'PRO' : undefined,
      }),
    enabled: Boolean(tenantId),
  })

  const update = (next: { view?: boolean; status?: string; month?: string; q?: string; page?: number }): void => {
    const params = new URLSearchParams()
    const values = { tenantId, view: proformas, status, month, q, page, ...next }
    if (values.tenantId) params.set('tenantId', values.tenantId)
    if (values.view) params.set('view', PROFORMA_VIEW)
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
  const totalPages = Math.max(Math.min(listQuery.data?.totalPages ?? 1, Math.floor(MAX_ROWS / PAGE_SIZE)), 1)
  const unfilteredEmpty = listQuery.isSuccess && rows.length === 0 && !status && !month && !q && page === 1
  const hasCredential = credentialsQuery.data?.find((row) => row.tenantId === tenantId)?.hasCredential
  const incomplete = listQuery.data?.issuedFetchIncomplete ?? false
  const stateCard = !proformas && unfilteredEmpty && credentialsQuery.isSuccess && (!incomplete || hasCredential === false)
  const totalCount = listQuery.data?.totalCount ?? 0
  const shownPage = listQuery.data?.page ?? page
  const firstRow = (shownPage - 1) * PAGE_SIZE + 1
  const pastLastPage = listQuery.isSuccess && rows.length === 0 && totalCount > 0 && page > totalPages
  const detailsPath = (row: IssuedFeedItemResponse): string =>
    row.source === 'External'
      ? `/invoices/${encodeURIComponent(row.ksefNumber!)}?${tenantQuery}`
      : `/issued/${encodeURIComponent(row.id)}?${tenantQuery}`
  const onRowClick = useRowOpen(detailsPath, { listSearch: location.search, fromIssued: true })

  const navigate = useNavigate()
  const newAdvance = useMutation({
    mutationFn: () => createAdvance(tenantId),
    onSuccess: (draft) => navigate(`/issued/${encodeURIComponent(draft.id)}/chain?${tenantQuery}`),
  })
  const queryClient = useQueryClient()
  const copy = useMutation({
    mutationFn: (row: IssuedFeedItemResponse) => copyIssuedInvoice(tenantId, row.id, row.invoiceType === 'PRO' ? 'PRO' : undefined),
    onSuccess: (draft) => {
      void queryClient.invalidateQueries({ queryKey: ['issued-invoices', tenantId] })
      navigate(`/issued/${encodeURIComponent(draft.id)}/edit?${tenantQuery}`)
    },
  })

  const allColumns: TableColumn<IssuedFeedItemResponse>[] = [
    {
      key: 'buyerName',
      label: 'Nabywca i numer',
      render: (row) => {
        const number = row.number ?? (row.status === 'Draft' ? '— (szkic)' : '—')
        const external = row.source === 'External' ? `, Spoza OpenKSeF: ${EXTERNAL_HINT}` : ''
        const kind = row.invoiceType && LIST_KIND_LABELS[row.invoiceType] ? `${LIST_KIND_LABELS[row.invoiceType]} · ` : ''
        return (
          <>
            <Link
              to={detailsPath(row)}
              className="rowlink"
              title={row.buyerName ?? undefined}
              aria-label={`${row.buyerName ?? 'Bez nabywcy'}, ${kind}${number}${external}`}
              onClick={(event) => event.preventDefault()}
              onDoubleClick={selectWordAt}
            >
              {row.buyerName ?? '—'}
            </Link>
            <span className="received-sub" title={number}>
              {kind}
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
      render: (row) => amount(row, row.totalNet),
    },
    {
      key: 'totalGross',
      label: 'Brutto',
      className: 'received-gross received-num',
      render: (row) => (
        <>
          <span className="received-amount" title={amount(row, row.totalGross)}>
            {amount(row, row.totalGross)}
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
    {
      key: 'id',
      label: 'Akcje',
      className: 'issued-actions',
      render: (row) =>
        canCopy(row) ? (
          <button
            type="button"
            className="ui-button ui-button--ghost ui-button--sm"
            aria-label="Kopiuj jako nową"
            title="Kopiuj jako nową"
            disabled={copy.isPending}
            onClick={(event) => {
              event.stopPropagation()
              copy.mutate(row)
            }}
          >
            <Icon name="copy" />
          </button>
        ) : null,
    },
  ]

  const columns = proformas ? allColumns.filter((column) => column.key !== 'ksefNumber') : allColumns

  return (
    <div className="inv-stack">
      <header className="page-header received-head">
        <h1>{proformas ? 'Proformy' : 'Wystawione faktury'}</h1>
        {profileQuery.data && !proformas ? <EnvBadge environment={profileQuery.data.ksefEnvironment} /> : null}
        <Link to={`/issued/new?${tenantQuery}`} className="ui-button ui-button--primary ui-button--md">
          <Icon name="plus" /> Nowa faktura
        </Link>
        <button type="button" className="ui-button ui-button--outline ui-button--md" disabled={newAdvance.isPending} onClick={() => newAdvance.mutate()}>
          <Icon name="plus" /> {newAdvance.isPending ? 'Tworzenie…' : 'Nowa zaliczka'}
        </button>
        <Link to={`/issued/new?kind=PRO&${tenantQuery}`} className="ui-button ui-button--outline ui-button--md">
          <Icon name="plus" /> Nowa proforma
        </Link>
        <Link to={`/issued/templates?${tenantQuery}`} className="ui-button ui-button--outline ui-button--md">
          <Icon name="file-text" /> Szablony
        </Link>
      </header>
      <div className="inv-chips" role="group" aria-label="Rodzaj dokumentu">
        <button type="button" aria-pressed={!proformas} onClick={() => update({ view: false, status: '', page: 1 })}>
          Faktury
        </button>
        <button type="button" aria-pressed={proformas} onClick={() => update({ view: true, status: '', page: 1 })}>
          Proformy
        </button>
      </div>
      {copy.error ? <ErrorBanner message={copy.error instanceof Error ? copy.error.message : 'Nie udało się skopiować faktury'} /> : null}
      {newAdvance.error ? <ErrorBanner message={newAdvance.error instanceof Error ? newAdvance.error.message : 'Nie udało się utworzyć faktury zaliczkowej'} /> : null}

      {pastLastPage ? <Navigate to={{ search: lastPageSearch(searchParams, totalPages) }} replace /> : null}

      {stateCard ? null : (
        <div className="received-tools">
          <div className="inv-chips" role="group" aria-label="Status">
            {(proformas ? proformaTabs : tabs).map((tab) => (
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

      {incomplete && !proformas ? (
        <div className="inv-alert inv-alert--info company-note" role="status" data-testid="issued-fetch-incomplete">
          <Icon name="circle-alert" />
          <span>Ostatnie pobieranie faktur sprzedaży z KSeF nie objęło całego okresu. Lista może być niepełna.</span>
        </div>
      ) : null}

      {stateCard && hasCredential === false ? (
        <EmptyState
          icon="key-round"
          title="Brakuje tokenu KSeF"
          message="Bez tokenu nie pobierzemy faktur z KSeF ani nie wyślemy nowych. Możesz przygotować szkic."
          actions={
            <div className="ui-empty-state__actions">
              <AddToken />
            </div>
          }
        />
      ) : stateCard ? (
        <EmptyState
          icon="send"
          title="Nie masz jeszcze wystawionych faktur"
          message="Wystawiona faktura pojawi się tutaj razem ze statusem w KSeF. Sprzedaż z innych programów zobaczysz po pobraniu z KSeF."
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
          isEmpty={rows.length === 0 && (proformas || !incomplete || !unfilteredEmpty)}
          loading={<Skeleton variant="table" columns={columns} label="Wczytywanie faktur…" />}
          emptyTitle={proformas ? 'Brak proform spełniających kryteria' : 'Brak faktur spełniających kryteria'}
          emptyMessage={proformas ? 'Zmień filtry lub kliknij „Nowa proforma”.' : 'Zmień filtry lub kliknij „Nowa faktura”.'}
          onRetry={() => {
            void listQuery.refetch()
            void credentialsQuery.refetch()
          }}
        >
          {rows.length > 0 ? (
            <section className="received-list" aria-label={proformas ? 'Lista proform' : 'Lista faktur'}>
              <Table testId="issued-invoice-table" columns={columns} data={rows} onRowClick={onRowClick} />
              <nav className="ui-pagination" aria-label="Paginacja wystawionych faktur">
                <span className="ui-pagination-info">
                  {firstRow}–{firstRow + rows.length - 1} z {totalCount} {proformas ? (totalCount === 1 ? 'proformy' : 'proform') : totalCount === 1 ? 'faktury' : 'faktur'}
                </span>
                {totalCount > MAX_ROWS ? (
                  <span className="ui-pagination-info" role="status" data-testid="issued-cap-note">
                    Pokazujemy pierwsze 10 000 faktur. Zawęź wyniki miesiącem lub wyszukiwaniem.
                  </span>
                ) : null}
                <div className="ui-pagination-controls">
                  <Button variant="outline" size="sm" disabled={shownPage <= 1} onClick={() => update({ page: shownPage - 1 })}>
                    Poprzednia
                  </Button>
                  <Button variant="outline" size="sm" disabled={shownPage >= totalPages} onClick={() => update({ page: shownPage + 1 })}>
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
