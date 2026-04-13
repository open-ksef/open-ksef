import { useQuery } from '@tanstack/react-query'
import { useMemo, useState, type ChangeEvent, type ReactElement } from 'react'
import { Link, useSearchParams } from 'react-router-dom'

import { listInvoices } from '@/api/endpoints/invoices'
import { listTenants } from '@/api/endpoints/tenants'
import type { SyncedInvoiceResponse } from '@/api/types'
import { AsyncStateView } from '@/components/AsyncStateView'
import { FilterBar } from '@/components/FilterBar'
import { Table, type TableColumn } from '@/components/Table'

function parseNumber(value: string | null, fallback: number): number {
  if (!value) return fallback
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

export function PurchaseInvoiceListPage(): ReactElement {
  const [searchParams, setSearchParams] = useSearchParams()

  const tenantIdFromUrl = searchParams.get('tenantId') ?? ''
  const dateFromFromUrl = searchParams.get('dateFrom') ?? ''
  const dateToFromUrl = searchParams.get('dateTo') ?? ''
  const pageFromUrl = parseNumber(searchParams.get('page'), 1)
  const pageSizeFromUrl = parseNumber(searchParams.get('pageSize'), 25)

  const [draftTenantId, setDraftTenantId] = useState(tenantIdFromUrl)
  const [draftDateFrom, setDraftDateFrom] = useState(dateFromFromUrl)
  const [draftDateTo, setDraftDateTo] = useState(dateToFromUrl)
  const [draftPageSize, setDraftPageSize] = useState(pageSizeFromUrl)

  const tenantsQuery = useQuery({
    queryKey: ['tenants', 'purchase-invoice-filters'],
    queryFn: () => listTenants(),
  })

  const effectiveTenantId = draftTenantId || tenantsQuery.data?.[0]?.id || ''

  const invoicesQuery = useQuery({
    queryKey: ['invoices', 'synced', 'list', { tenantId: effectiveTenantId, dateFrom: dateFromFromUrl, dateTo: dateToFromUrl, page: pageFromUrl, pageSize: pageSizeFromUrl }],
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

  const columns = useMemo<TableColumn<SyncedInvoiceResponse>[]>(
    () => [
      {
        key: 'invoiceNumber',
        label: 'Nr faktury',
        render: (inv) => (
          <span className="mono-number" data-testid="invoice-number">
            {inv.invoiceNumber ?? inv.ksefInvoiceNumber ?? '—'}
          </span>
        ),
      },
      {
        key: 'ksefInvoiceNumber',
        label: 'Numer KSeF',
        render: (inv) => (
          <span className="token-display" data-testid="invoice-ksef-number">
            {inv.ksefInvoiceNumber ?? '—'}
          </span>
        ),
      },
      {
        key: 'vendorName',
        label: 'Sprzedawca',
        render: (inv) => <span>{inv.vendorName ?? '—'}</span>,
      },
      {
        key: 'issueDate',
        label: 'Data wystawienia',
        render: (inv) => new Date(inv.issueDate).toLocaleDateString('pl-PL'),
      },
      {
        key: 'amountGross',
        label: 'Brutto',
        render: (inv) => (
          <span style={{ fontWeight: 600 }}>
            {inv.amountGross.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} {inv.currency}
          </span>
        ),
      },
      {
        key: 'isPaid',
        label: 'Status płatności',
        render: (inv) => (
          <span data-testid="invoice-paid-status">
            <span className={`paid-indicator ${inv.isPaid ? 'paid-indicator--paid' : 'paid-indicator--unpaid'}`} />
            {inv.isPaid ? 'Opłacona' : 'Nieopłacona'}
          </span>
        ),
      },
      {
        key: 'id',
        label: '',
        render: (inv) => (
          <Link
            to={`/invoices/${encodeURIComponent(inv.ksefInvoiceNumber)}?tenantId=${effectiveTenantId}`}
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

  const updateSearch = (params: { tenantId: string; dateFrom: string; dateTo: string; page: number; pageSize: number }): void => {
    const next = new URLSearchParams()
    if (params.tenantId) next.set('tenantId', params.tenantId)
    if (params.dateFrom) next.set('dateFrom', params.dateFrom)
    if (params.dateTo) next.set('dateTo', params.dateTo)
    next.set('page', String(params.page))
    next.set('pageSize', String(params.pageSize))
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

  const isLoading = tenantsQuery.isLoading || invoicesQuery.isLoading
  const combinedError = tenantsQuery.error ?? invoicesQuery.error

  return (
    <section>
      <header className="page-header">
        <h1>Faktury zakupu</h1>
        <div className="page-header__actions">
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
        </div>
      </header>

      <FilterBar
        onApply={onApplyFilters}
        onReset={onResetFilters}
        applyButtonTestId="invoice-apply-filters"
      >
        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
          <label htmlFor="purchase-tenant-filter" className="ui-filter-label">
            Firma
          </label>
          <select
            id="purchase-tenant-filter"
            data-testid="invoice-tenant-filter"
            value={draftTenantId || effectiveTenantId}
            onChange={(e: ChangeEvent<HTMLSelectElement>) => setDraftTenantId(e.target.value)}
          >
            {(tenantsQuery.data ?? []).map((tenant) => (
              <option key={tenant.id} value={tenant.id}>
                {tenant.displayName || tenant.nip}
              </option>
            ))}
          </select>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
          <label htmlFor="purchase-date-from" className="ui-filter-label">
            Od
          </label>
          <input
            id="purchase-date-from"
            data-testid="invoice-date-from"
            type="date"
            value={draftDateFrom}
            onChange={(e) => setDraftDateFrom(e.target.value)}
          />
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
          <label htmlFor="purchase-date-to" className="ui-filter-label">
            Do
          </label>
          <input
            id="purchase-date-to"
            data-testid="invoice-date-to"
            type="date"
            value={draftDateTo}
            onChange={(e) => setDraftDateTo(e.target.value)}
          />
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
          <label htmlFor="purchase-page-size" className="ui-filter-label">
            Na stronę
          </label>
          <select
            id="purchase-page-size"
            data-testid="invoice-page-size"
            value={draftPageSize}
            onChange={(e: ChangeEvent<HTMLSelectElement>) => {
              const nextPageSize = parseNumber(e.target.value, 25)
              setDraftPageSize(nextPageSize)
            }}
          >
            <option value={10}>10</option>
            <option value={25}>25</option>
            <option value={50}>50</option>
            <option value={100}>100</option>
          </select>
        </div>
      </FilterBar>

      <AsyncStateView
        isLoading={isLoading}
        error={combinedError}
        isEmpty={rows.length === 0}
        loadingLines={6}
        emptyTitle="Nie znaleziono faktur zakupu"
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
