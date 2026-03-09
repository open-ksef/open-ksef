import { useQuery } from '@tanstack/react-query'
import { useMemo, useState, type ChangeEvent, type ReactElement } from 'react'
import { Link, useSearchParams } from 'react-router-dom'

import { listInvoices } from '@/api/endpoints/invoices'
import { listTenants } from '@/api/endpoints/tenants'
import type { InvoiceResponse } from '@/api/types'
import { AsyncStateView } from '@/components/AsyncStateView'
import { FilterBar } from '@/components/FilterBar'
import { Table, type TableColumn } from '@/components/Table'

interface InvoiceListFilters {
  tenantId: string
  dateFrom: string
  dateTo: string
  page: number
  pageSize: number
}

function parseNumber(value: string | null, fallback: number): number {
  if (!value) return fallback
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

export function InvoiceListPage(): ReactElement {
  const [searchParams, setSearchParams] = useSearchParams()

  const tenantIdFromUrl = searchParams.get('tenantId') ?? ''
  const dateFromFromUrl = searchParams.get('dateFrom') ?? ''
  const dateToFromUrl = searchParams.get('dateTo') ?? ''
  const pageFromUrl = parseNumber(searchParams.get('page'), 1)
  const pageSizeFromUrl = parseNumber(searchParams.get('pageSize'), 10)

  const [draftTenantId, setDraftTenantId] = useState(tenantIdFromUrl)
  const [draftDateFrom, setDraftDateFrom] = useState(dateFromFromUrl)
  const [draftDateTo, setDraftDateTo] = useState(dateToFromUrl)
  const [draftPageSize, setDraftPageSize] = useState(pageSizeFromUrl)

  const tenantsQuery = useQuery({
    queryKey: ['tenants', 'invoice-filters'],
    queryFn: () => listTenants(),
  })

  const effectiveTenantId = draftTenantId || tenantsQuery.data?.[0]?.id || ''

  const invoicesQuery = useQuery({
    queryKey: ['invoices', effectiveTenantId, dateFromFromUrl, dateToFromUrl, pageFromUrl, pageSizeFromUrl],
    queryFn: () =>
      listInvoices(effectiveTenantId, {
        page: pageFromUrl,
        pageSize: pageSizeFromUrl,
        dateFrom: dateFromFromUrl || undefined,
        dateTo: dateToFromUrl || undefined,
      }),
    enabled: Boolean(effectiveTenantId),
    retry: false,
  })

  const rows = invoicesQuery.data?.items ?? []
  const combinedError = tenantsQuery.error ?? invoicesQuery.error

  const columns = useMemo<TableColumn<InvoiceResponse>[]>(
    () => [
      {
        key: 'ksefInvoiceNumber',
        label: 'Numer KSeF',
        render: (row) => (
          <span className="token-display" data-testid="invoice-ksef-number">
            {row.ksefInvoiceNumber}
          </span>
        ),
      },
      {
        key: 'invoiceNumber',
        label: 'Nr faktury',
        render: (row) => (
          <span style={{ fontFamily: 'ui-monospace, monospace', fontSize: '13px' }}>
            {row.invoiceNumber ?? '—'}
          </span>
        ),
      },
      { key: 'vendorName', label: 'Sprzedawca' },
      {
        key: 'vendorNip',
        label: 'NIP sprzedawcy',
        render: (row) => (
          <span style={{ fontFamily: 'ui-monospace, monospace', fontSize: '13px' }}>
            {row.vendorNip}
          </span>
        ),
      },
      {
        key: 'issueDate',
        label: 'Data wystawienia',
        render: (row) => new Date(row.issueDate).toLocaleDateString('pl-PL'),
      },
      {
        key: 'amountGross',
        label: 'Brutto',
        render: (row) => (
          <span style={{ fontWeight: 600 }}>
            {row.amountGross.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
          </span>
        ),
      },
      {
        key: 'amountNet',
        label: 'Netto',
        render: (row) => row.amountNet.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }),
      },
      {
        key: 'currency',
        label: 'Waluta',
        render: (row) => (
          <span
            className="ui-status-badge ui-status-badge--success"
            style={{ fontSize: '11px', fontFamily: 'ui-monospace, monospace' }}
          >
            {row.currency}
          </span>
        ),
      },
      {
        key: 'isPaid',
        label: 'Status',
        render: (row) => (
          <span data-testid="invoice-paid-status">
            <span className={`paid-indicator ${row.isPaid ? 'paid-indicator--paid' : 'paid-indicator--unpaid'}`} />
            {row.isPaid ? 'Opłacona' : 'Nieopłacona'}
          </span>
        ),
      },
      {
        key: 'id',
        label: '',
        render: (row) => (
          <Link
            to={`/invoices/${encodeURIComponent(row.ksefInvoiceNumber)}?tenantId=${effectiveTenantId}`}
            data-testid="invoice-view-details"
            className="btn-action btn-action--edit"
          >
            Otwórz →
          </Link>
        ),
      },
    ],
    [effectiveTenantId],
  )

  const totalPages = invoicesQuery.data?.totalPages ?? 0
  const totalCount = invoicesQuery.data?.totalCount ?? 0

  const updateSearch = (filters: InvoiceListFilters): void => {
    const next = new URLSearchParams()
    if (filters.tenantId) next.set('tenantId', filters.tenantId)
    if (filters.dateFrom) next.set('dateFrom', filters.dateFrom)
    if (filters.dateTo) next.set('dateTo', filters.dateTo)
    next.set('page', String(filters.page))
    next.set('pageSize', String(filters.pageSize))
    setSearchParams(next)
  }

  const onApplyFilters = () => {
    updateSearch({
      tenantId: draftTenantId || effectiveTenantId,
      dateFrom: draftDateFrom,
      dateTo: draftDateTo,
      page: 1,
      pageSize: draftPageSize,
    })
  }

  const onResetFilters = () => {
    setDraftDateFrom('')
    setDraftDateTo('')
    updateSearch({
      tenantId: effectiveTenantId,
      dateFrom: '',
      dateTo: '',
      page: 1,
      pageSize: draftPageSize,
    })
  }

  const onChangePage = (nextPage: number): void => {
    updateSearch({
      tenantId: tenantIdFromUrl || effectiveTenantId,
      dateFrom: dateFromFromUrl,
      dateTo: dateToFromUrl,
      page: nextPage,
      pageSize: pageSizeFromUrl,
    })
  }

  const onChangeTenant = (event: ChangeEvent<HTMLSelectElement>): void => {
    setDraftTenantId(event.target.value)
  }

  const onChangePageSize = (event: ChangeEvent<HTMLSelectElement>): void => {
    const nextPageSize = parseNumber(event.target.value, 10)
    setDraftPageSize(nextPageSize)
  }

  return (
    <section>
      <header className="page-header">
        <h1>Faktury</h1>
        <button
          data-testid="invoice-refresh-button"
          type="button"
          onClick={() => {
            void tenantsQuery.refetch()
            void invoicesQuery.refetch()
          }}
        >
          ↺ Odśwież
        </button>
      </header>

      <FilterBar
        onApply={onApplyFilters}
        onReset={onResetFilters}
        applyButtonTestId="invoice-apply-filters"
      >
        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
          <label htmlFor="invoice-tenant-filter" style={{ fontSize: '11px', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--ui-text-muted)' }}>
            Firma
          </label>
          <select
            id="invoice-tenant-filter"
            data-testid="invoice-tenant-filter"
            value={draftTenantId || effectiveTenantId}
            onChange={onChangeTenant}
          >
            {(tenantsQuery.data ?? []).map((tenant) => (
              <option key={tenant.id} value={tenant.id}>
                {tenant.displayName || tenant.nip}
              </option>
            ))}
          </select>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
          <label htmlFor="invoice-date-from" style={{ fontSize: '11px', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--ui-text-muted)' }}>
            Od
          </label>
          <input
            id="invoice-date-from"
            data-testid="invoice-date-from"
            type="date"
            value={draftDateFrom}
            onChange={(event) => setDraftDateFrom(event.target.value)}
          />
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
          <label htmlFor="invoice-date-to" style={{ fontSize: '11px', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--ui-text-muted)' }}>
            Do
          </label>
          <input
            id="invoice-date-to"
            data-testid="invoice-date-to"
            type="date"
            value={draftDateTo}
            onChange={(event) => setDraftDateTo(event.target.value)}
          />
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
          <label htmlFor="invoice-page-size" style={{ fontSize: '11px', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--ui-text-muted)' }}>
            Na stronę
          </label>
          <select
            id="invoice-page-size"
            data-testid="invoice-page-size"
            value={draftPageSize}
            onChange={onChangePageSize}
          >
            <option value={10}>10</option>
            <option value={25}>25</option>
            <option value={50}>50</option>
            <option value={100}>100</option>
          </select>
        </div>
      </FilterBar>

      <AsyncStateView
        isLoading={tenantsQuery.isLoading || invoicesQuery.isLoading}
        error={combinedError}
        isEmpty={rows.length === 0}
        loadingLines={6}
        emptyTitle="Nie znaleziono faktur"
        emptyMessage="Zmień filtry lub wybierz inną firmę."
        onRetry={() => {
          void tenantsQuery.refetch()
          void invoicesQuery.refetch()
        }}
      >
        <>
            <Table
              testId="invoice-table"
              columns={columns}
              data={rows}
              getRowProps={() => ({ 'data-testid': 'invoice-row' })}
            />

            <nav className="ui-pagination" aria-label="Paginacja faktur">
              <span className="ui-pagination-info">
                {totalCount > 0
                  ? `Strona ${pageFromUrl} z ${Math.max(totalPages, 1)} · ${totalCount} ${totalCount === 1 ? 'faktura' : totalCount >= 2 && totalCount <= 4 ? 'faktury' : 'faktur'} łącznie`
                  : 'Brak wyników'}
              </span>
              <div className="ui-pagination-controls">
                <button
                  data-testid="invoice-prev-page"
                  type="button"
                  disabled={pageFromUrl <= 1}
                  onClick={() => onChangePage(pageFromUrl - 1)}
                >
                  ← Wstecz
                </button>
                <button
                  data-testid="invoice-next-page"
                  type="button"
                  disabled={totalPages === 0 || pageFromUrl >= totalPages}
                  onClick={() => onChangePage(pageFromUrl + 1)}
                >
                  Dalej →
                </button>
              </div>
            </nav>
        </>
      </AsyncStateView>
    </section>
  )
}
