import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type ReactElement } from 'react'
import toast from 'react-hot-toast'
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom'

import { forceCredentialSync } from '@/api/endpoints/credentials'
import { ApiError } from '@/api/errors'
import { listInvoices } from '@/api/endpoints/invoices'
import type { InvoiceResponse } from '@/api/types'
import { useCompany } from '@/company/useCompany'
import { AsyncStateView } from '@/components/AsyncStateView'
import { Button } from '@/components/Button'
import { Icon } from '@/components/Icon'
import { PaidStatus } from '@/components/PaidStatus'
import '@/components/invoicing/invoicing.css'
import { Table, type TableColumn } from '@/components/Table'
import { useDashboard } from '@/hooks/useDashboard'
import { useTenants } from '@/hooks/useTenants'
import { formatDate, formatDateTime, formatMoney } from '@/invoicing/format'

const PAGE_SIZE = 25
const SEARCH_DELAY_MS = 300
const FETCH_OUTCOME_UNKNOWN = 'Nie mamy potwierdzenia pobrania z KSeF. Część faktur mogła się pobrać. Sprawdź listę i w razie potrzeby spróbuj ponownie.'
const FETCH_FAILED = 'Pobieranie z KSeF nie powiodło się. Spróbuj ponownie później. Jeśli błąd wraca, sprawdź token KSeF.'

function parsePage(value: string | null): number {
  const parsed = Number.parseInt(value ?? '', 10)
  return parsed > 0 ? parsed : 1
}

function vendorFilter(term: string): string {
  return /^(pl)?[\d\s-]+$/i.test(term) ? term.replace(/^pl|[\s-]/gi, '') : term
}

function withFilters(current: URLSearchParams, filters: Record<string, string>): URLSearchParams {
  const next = new URLSearchParams(current)
  next.delete('page')
  for (const [name, value] of Object.entries(filters)) {
    if (value) next.set(name, value)
    else next.delete(name)
  }
  return next
}

function isMissingToken(error: Error | null): boolean {
  return error instanceof ApiError && error.status === 409
}

function fetchErrorMessage(error: Error): string {
  const syncFailed =
    error instanceof ApiError &&
    error.status === 502 &&
    error.originalError instanceof Response &&
    error.originalError.headers.get('Content-Type')?.includes('json')
  return syncFailed ? FETCH_FAILED : FETCH_OUTCOME_UNKNOWN
}

function DateFilter({ id, label, value, onCommit }: { id: string; label: string; value: string; onCommit: (value: string) => void }): ReactElement {
  const [draft, setDraft] = useState(value)
  const [seen, setSeen] = useState(value)
  if (value !== seen) {
    setSeen(value)
    setDraft(value)
  }

  return (
    <>
      <label htmlFor={id} className="visually-hidden">
        {label}
      </label>
      <input
        id={id}
        data-testid={id}
        type="date"
        value={draft}
        onChange={(event) => {
          const next = event.target.value
          setDraft(next)
          // Typing a year passes through 0002, 0020, 0202. The router commits after the event, so a URL-controlled value would reset the field mid-typing.
          if (!next || next >= '1000') onCommit(next)
        }}
      />
    </>
  )
}

export function InvoiceListPage(): ReactElement {
  const [searchParams, setSearchParams] = useSearchParams()
  const location = useLocation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const q = searchParams.get('q') ?? ''
  const dateFrom = searchParams.get('dateFrom') ?? ''
  const dateTo = searchParams.get('dateTo') ?? ''
  const page = parsePage(searchParams.get('page'))

  const [draft, setDraft] = useState(q)
  const [urlTerm, setUrlTerm] = useState(q)
  if (q !== urlTerm) {
    setUrlTerm(q)
    // Without this check our own debounced write drops the space the user just typed between words.
    if (q !== draft.trim()) setDraft(q)
  }

  useEffect(() => {
    const term = draft.trim()
    if (term === q) return
    const timer = setTimeout(() => setSearchParams(withFilters(searchParams, { q: term }), { replace: true }), SEARCH_DELAY_MS)
    return () => clearTimeout(timer)
  }, [draft, q, searchParams, setSearchParams])

  const tenantsQuery = useTenants()
  const { tenantId } = useCompany()
  const dashboardQuery = useDashboard()

  const filterByVendor = vendorFilter(q)
  const invoicesQuery = useQuery({
    queryKey: ['invoices', tenantId, filterByVendor, dateFrom, dateTo, page],
    queryFn: () =>
      listInvoices(tenantId, {
        page,
        pageSize: PAGE_SIZE,
        filterByVendor: filterByVendor || undefined,
        dateFrom: dateFrom || undefined,
        dateTo: dateTo || undefined,
      }),
    enabled: Boolean(tenantId),
    retry: false,
  })

  const fetchMutation = useMutation({
    mutationFn: (id: string) => forceCredentialSync(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      void queryClient.invalidateQueries({ queryKey: ['invoices'] })
    },
    onError: (error) => {
      if (!isMissingToken(error)) toast.error(fetchErrorMessage(error))
    },
  })

  const rows = invoicesQuery.data?.items ?? []
  const totalPages = invoicesQuery.data?.totalPages ?? 0
  const totalCount = invoicesQuery.data?.totalCount ?? 0
  const firstRow = (page - 1) * PAGE_SIZE + 1

  const summary = dashboardQuery.data?.find((row) => row.tenantId === tenantId)
  const fetched = fetchMutation.data?.tenantId === tenantId ? fetchMutation.data : null
  const missingToken = isMissingToken(fetchMutation.error) && fetchMutation.variables === tenantId

  const detailsPath = (row: InvoiceResponse): string =>
    `/invoices/${encodeURIComponent(row.ksefInvoiceNumber)}?tenantId=${encodeURIComponent(tenantId)}`
  const detailsState = { listSearch: location.search, siblings: rows.map((row) => row.ksefInvoiceNumber) }

  const onChangePage = (nextPage: number): void => {
    const next = new URLSearchParams(searchParams)
    if (nextPage > 1) next.set('page', String(nextPage))
    else next.delete('page')
    setSearchParams(next)
  }

  const onRowClick = (row: InvoiceResponse): void => {
    if (window.getSelection()?.isCollapsed === false) return
    void navigate(detailsPath(row), { state: detailsState })
  }

  const columns: TableColumn<InvoiceResponse>[] = [
    {
      key: 'vendorName',
      label: 'Sprzedawca i numer',
      render: (row) => {
        const number = row.invoiceNumber ?? row.ksefInvoiceNumber
        return (
          <>
            <Link
              to={detailsPath(row)}
              state={detailsState}
              className="rowlink"
              title={row.vendorName}
              aria-label={`${row.vendorName}, ${number}`}
              data-testid="invoice-view-details"
              onClick={(event) => event.stopPropagation()}
            >
              {row.vendorName}
            </Link>
            <span className="received-sub" title={number}>
              <span data-testid="invoice-number">{number}</span> · {formatDate(row.issueDate)}
            </span>
          </>
        )
      },
    },
    {
      key: 'amountNet',
      label: 'Netto',
      className: 'received-net received-num',
      render: (row) => formatMoney(row.amountNet, row.currency),
    },
    {
      key: 'amountGross',
      label: 'Brutto',
      className: 'received-gross received-num',
      render: (row) => (
        <>
          <span className="received-amount">{formatMoney(row.amountGross, row.currency)}</span>
          <span className="received-status-mobile">
            <PaidStatus row={row} testId="invoice-paid-status-mobile" />
          </span>
        </>
      ),
    },
    {
      key: 'isPaid',
      label: 'Płatność',
      className: 'received-status',
      render: (row) => <PaidStatus row={row} testId="invoice-paid-status" />,
    },
    {
      key: 'ksefInvoiceNumber',
      label: 'Numer KSeF',
      className: 'received-ksef',
      render: (row) => <span data-testid="invoice-ksef-number">{row.ksefInvoiceNumber}</span>,
    },
  ]

  return (
    <section>
      <header className="page-header received-head">
        <h1>Otrzymane faktury</h1>
        <div className="received-sync">
          {summary?.lastSuccessfulSync ? (
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
          <span role="status" data-testid="invoice-fetch-note" className={missingToken ? 'received-sync--stale' : undefined}>
            {missingToken ? (
              <>
                Ta firma nie ma tokenu KSeF. <Link to="/credentials">Dodaj token KSeF</Link>
              </>
            ) : fetched ? (
              `Pobrane: ${fetched.fetchedInvoices}, nowe: ${fetched.newInvoices}.`
            ) : null}
          </span>
        </div>
        <Button
          data-testid="invoice-fetch-button"
          disabled={!tenantId || fetchMutation.isPending}
          onClick={() => fetchMutation.mutate(tenantId)}
        >
          <Icon name="cloud-download" />
          {fetchMutation.isPending ? 'Pobieranie…' : 'Pobierz z KSeF'}
        </Button>
      </header>

      <div className="received-tools">
        <label htmlFor="invoice-search" className="visually-hidden">
          Szukaj sprzedawcy
        </label>
        <input
          id="invoice-search"
          data-testid="invoice-search"
          type="search"
          placeholder="Nazwa lub NIP"
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
        />
        <DateFilter
          id="invoice-date-from"
          label="Data wystawienia od"
          value={dateFrom}
          onCommit={(value) => setSearchParams(withFilters(searchParams, { dateFrom: value }), { replace: true })}
        />
        <DateFilter
          id="invoice-date-to"
          label="Data wystawienia do"
          value={dateTo}
          onCommit={(value) => setSearchParams(withFilters(searchParams, { dateTo: value }), { replace: true })}
        />
      </div>

      <AsyncStateView
        isLoading={tenantsQuery.isLoading || invoicesQuery.isLoading}
        error={tenantsQuery.error ?? invoicesQuery.error}
        isEmpty={rows.length === 0}
        loadingLines={6}
        emptyTitle="Nie znaleziono faktur"
        emptyMessage="Zmień filtry lub wybierz inną firmę."
        onRetry={() => {
          void tenantsQuery.refetch()
          void invoicesQuery.refetch()
        }}
      >
        <section className="received-list" aria-label="Lista faktur">
          <Table testId="invoice-table" columns={columns} data={rows} onRowClick={onRowClick} getRowProps={() => ({ 'data-testid': 'invoice-row' })} />

          <nav className="ui-pagination" aria-label="Paginacja faktur">
            <span className="ui-pagination-info">
              {firstRow}–{firstRow + rows.length - 1} z {totalCount} {totalCount === 1 ? 'faktury' : 'faktur'}, od najnowszej
            </span>
            <div className="ui-pagination-controls">
              <Button
                variant="outline"
                size="sm"
                data-testid="invoice-prev-page"
                disabled={page <= 1}
                onClick={() => onChangePage(page - 1)}
              >
                Poprzednia
              </Button>
              <Button
                variant="outline"
                size="sm"
                data-testid="invoice-next-page"
                disabled={page >= totalPages}
                onClick={() => onChangePage(page + 1)}
              >
                Następna
              </Button>
            </div>
          </nav>
        </section>
      </AsyncStateView>
    </section>
  )
}
