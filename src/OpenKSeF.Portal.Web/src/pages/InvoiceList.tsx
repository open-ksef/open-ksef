import { useQuery } from '@tanstack/react-query'
import { useEffect, useState, type ReactElement } from 'react'
import { Link, Navigate, useLocation, useSearchParams } from 'react-router-dom'

import { listInvoices } from '@/api/endpoints/invoices'
import type { InvoiceResponse } from '@/api/types'
import { useCompany } from '@/company/useCompany'
import { AsyncStateView } from '@/components/AsyncStateView'
import { Button } from '@/components/Button'
import { CompanyStateCard } from '@/components/CompanyStateCard'
import { DueDate } from '@/components/DueDate'
import { KsefSync } from '@/components/KsefSync'
import { PaidStatus } from '@/components/PaidStatus'
import '@/components/invoicing/invoicing.css'
import { Table, type TableColumn } from '@/components/Table'
import { FIRST_FETCH_POLL_MS, useCompanyState } from '@/hooks/useCompanyState'
import { useKsefFetch } from '@/hooks/useKsefFetch'
import { selectWordAt, useRowOpen } from '@/hooks/useRowOpen'
import { useTenants } from '@/hooks/useTenants'
import { formatDate, formatMoney } from '@/invoicing/format'

const PAGE_SIZE = 25
const SEARCH_DELAY_MS = 300

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

function withPage(current: URLSearchParams, page: number): URLSearchParams {
  const next = new URLSearchParams(current)
  if (page > 1) next.set('page', String(page))
  else next.delete('page')
  return next
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
        onBlur={() => setDraft(value)}
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

  const q = searchParams.get('q') ?? ''
  const dateFrom = searchParams.get('dateFrom') ?? ''
  const dateTo = searchParams.get('dateTo') ?? ''
  const unpaid = searchParams.get('unpaid') === '1'
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
  const { state, summary, credential, isLoading: stateLoading, error: stateError, refetch: refetchState } = useCompanyState()
  const ksef = useKsefFetch(tenantId)

  const filterByVendor = vendorFilter(q)
  const invoicesQuery = useQuery({
    queryKey: ['invoices', tenantId, filterByVendor, dateFrom, dateTo, unpaid, page],
    queryFn: () =>
      listInvoices(tenantId, {
        page,
        pageSize: PAGE_SIZE,
        filterByVendor: filterByVendor || undefined,
        dateFrom: dateFrom || undefined,
        dateTo: dateTo || undefined,
        isPaid: unpaid ? false : undefined,
      }),
    enabled: Boolean(tenantId),
    retry: false,
    refetchInterval: state === 'first-fetch' ? FIRST_FETCH_POLL_MS : false,
  })

  const rows = invoicesQuery.data?.items ?? []
  const totalPages = invoicesQuery.data?.totalPages ?? 0
  const totalCount = invoicesQuery.data?.totalCount ?? 0
  const firstRow = (page - 1) * PAGE_SIZE + 1

  const unfilteredEmpty = invoicesQuery.isSuccess && rows.length === 0 && !q && !dateFrom && !dateTo && !unpaid && page === 1
  const stateCard = unfilteredEmpty && state && state !== 'full' ? state : null

  const detailsPath = (row: InvoiceResponse): string =>
    `/invoices/${encodeURIComponent(row.ksefInvoiceNumber)}?tenantId=${encodeURIComponent(tenantId)}`
  const detailsState = { listSearch: location.search, siblings: rows.map((row) => row.ksefInvoiceNumber) }

  const onChangePage = (nextPage: number): void => setSearchParams(withPage(searchParams, nextPage))

  const onRowClick = useRowOpen(detailsPath, detailsState)
  const pastLastPage = invoicesQuery.isSuccess && rows.length === 0 && totalCount > 0 && page > totalPages

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
              onClick={(event) => event.preventDefault()}
              onDoubleClick={selectWordAt}
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
      key: 'dueDate',
      label: 'Termin płatności',
      className: 'received-due',
      render: (row) => <DueDate row={row} testId="invoice-due-date" />,
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
          <span className="received-amount" title={formatMoney(row.amountGross, row.currency)}>
            {formatMoney(row.amountGross, row.currency)}
          </span>
          <span className="received-status-mobile">
            {row.isOverdue ? <DueDate row={row} testId="invoice-due-date-mobile" /> : <PaidStatus row={row} testId="invoice-paid-status-mobile" />}
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
        <KsefSync state={state} summary={summary} ksef={ksef} disabled={!tenantId} variant="primary" />
      </header>

      {pastLastPage ? <Navigate to={{ search: withPage(searchParams, totalPages).toString() }} replace /> : null}

      {stateCard ? null : (
        <div className="received-tools">
          <div className="inv-chips" role="group" aria-label="Filtr płatności">
            {[
              { label: 'Wszystkie', value: '' },
              { label: 'Nieopłacone', value: '1' },
            ].map((chip) => (
              <button
                key={chip.label}
                type="button"
                aria-pressed={unpaid === (chip.value === '1')}
                onClick={() => setSearchParams(withFilters(searchParams, { unpaid: chip.value }), { replace: true })}
              >
                {chip.label}
              </button>
            ))}
          </div>
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
      )}

      {stateCard ? (
        <CompanyStateCard state={stateCard} place="list" summary={summary} credential={credential} />
      ) : (
        <AsyncStateView
          isLoading={tenantsQuery.isLoading || invoicesQuery.isLoading || pastLastPage || (unfilteredEmpty && stateLoading)}
          error={tenantsQuery.error ?? invoicesQuery.error ?? (unfilteredEmpty ? stateError : null)}
          isEmpty={rows.length === 0}
          loadingLines={6}
          emptyTitle={unpaid ? 'Nie ma nieopłaconych faktur' : 'Nie znaleziono faktur'}
          emptyMessage="Zmień filtry lub wybierz inną firmę."
          onRetry={() => {
            void tenantsQuery.refetch()
            void invoicesQuery.refetch()
            if (unfilteredEmpty) refetchState()
          }}
        >
          <section className="received-list" aria-label="Lista faktur">
            <Table testId="invoice-table" columns={columns} data={rows} onRowClick={onRowClick} getRowProps={() => ({ 'data-testid': 'invoice-row' })} />

            <nav className="ui-pagination" aria-label="Paginacja faktur">
              <span className="ui-pagination-info">
                {firstRow}–{firstRow + rows.length - 1} z {totalCount}{' '}
                {unpaid
                  ? `${totalCount === 1 ? 'nieopłaconej' : 'nieopłaconych'}, od najbliższego terminu, bez terminu na końcu`
                  : `${totalCount === 1 ? 'faktury' : 'faktur'}, od najnowszej`}
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
      )}
    </section>
  )
}
