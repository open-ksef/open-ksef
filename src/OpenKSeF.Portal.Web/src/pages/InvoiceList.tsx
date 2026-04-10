import { useQuery } from '@tanstack/react-query'
import { useMemo, useState, type ChangeEvent, type ReactElement } from 'react'
import { Link, useSearchParams } from 'react-router-dom'

import { listAggregateInvoices } from '@/api/invoicesAggregateApi'
import type { InvoiceReadDto } from '@/api/schemas/invoice'
import { listInvoices } from '@/api/endpoints/invoices'
import { listTenants } from '@/api/endpoints/tenants'
import type { SyncedInvoiceResponse } from '@/api/types'
import { AsyncStateView } from '@/components/AsyncStateView'
import { FilterBar } from '@/components/FilterBar'
import { Table, type TableColumn } from '@/components/Table'
import { DocumentStatusBadge } from '@/components/invoices/DocumentStatusBadge'
import { SourceChip } from '@/components/invoices/SourceChip'

type MergedRow =
  | { source: 'Aggregate'; invoice: InvoiceReadDto }
  | { source: 'Synced'; invoice: SyncedInvoiceResponse }

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
  const pageSizeFromUrl = parseNumber(searchParams.get('pageSize'), 25)

  const [draftTenantId, setDraftTenantId] = useState(tenantIdFromUrl)
  const [draftDateFrom, setDraftDateFrom] = useState(dateFromFromUrl)
  const [draftDateTo, setDraftDateTo] = useState(dateToFromUrl)
  const [draftPageSize, setDraftPageSize] = useState(pageSizeFromUrl)

  const tenantsQuery = useQuery({
    queryKey: ['tenants', 'invoice-filters'],
    queryFn: () => listTenants(),
  })

  const effectiveTenantId = draftTenantId || tenantsQuery.data?.[0]?.id || ''

  const syncedQuery = useQuery({
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

  const aggregateQuery = useQuery({
    queryKey: ['invoices', 'aggregate', 'list', { tenantId: effectiveTenantId, dateFrom: dateFromFromUrl, dateTo: dateToFromUrl, page: pageFromUrl, pageSize: pageSizeFromUrl }],
    queryFn: () =>
      listAggregateInvoices(effectiveTenantId, {
        from: dateFromFromUrl || undefined,
        to: dateToFromUrl || undefined,
        page: pageFromUrl,
        pageSize: pageSizeFromUrl,
      }),
    enabled: Boolean(effectiveTenantId),
    retry: false,
  })

  const mergedRows = useMemo<MergedRow[]>(() => {
    const aggregateRows: MergedRow[] = (aggregateQuery.data?.items ?? []).map((inv) => ({
      source: 'Aggregate',
      invoice: inv,
    }))
    const syncedRows: MergedRow[] = (syncedQuery.data?.items ?? []).map((inv) => ({
      source: 'Synced',
      invoice: inv,
    }))
    return [...aggregateRows, ...syncedRows]
  }, [aggregateQuery.data, syncedQuery.data])

  const combinedError = tenantsQuery.error ?? syncedQuery.error ?? aggregateQuery.error
  const isLoading = tenantsQuery.isLoading || syncedQuery.isLoading || aggregateQuery.isLoading

  const columns = useMemo<TableColumn<MergedRow>[]>(
    () => [
      {
        key: 'source',
        label: 'Źródło',
        render: (row) => <SourceChip source={row.source === 'Aggregate' ? 'Aggregate' : 'Synced'} />,
      },
      {
        key: 'invoiceNumber',
        label: 'Nr faktury',
        render: (row) => {
          const number = row.source === 'Aggregate'
            ? (row.invoice as InvoiceReadDto).documentNumber
            : ((row.invoice as SyncedInvoiceResponse).invoiceNumber ?? (row.invoice as SyncedInvoiceResponse).ksefInvoiceNumber)
          return (
            <span
              data-testid="invoice-number"
              style={{ fontWeight: 700, fontFamily: 'ui-monospace, monospace', fontSize: '13px' }}
            >
              {number ?? '—'}
            </span>
          )
        },
      },
      {
        key: 'ksefInvoiceNumber',
        label: 'Numer KSeF',
        render: (row) => {
          const ksefNum = row.source === 'Aggregate'
            ? (row.invoice as InvoiceReadDto).ksefDocumentNumber
            : (row.invoice as SyncedInvoiceResponse).ksefInvoiceNumber
          return (
            <span className="token-display" data-testid="invoice-ksef-number">
              {ksefNum ?? '—'}
            </span>
          )
        },
      },
      {
        key: 'status',
        label: 'Status',
        render: (row) => {
          if (row.source === 'Aggregate') {
            return <DocumentStatusBadge status={(row.invoice as InvoiceReadDto).status} />
          }
          const inv = row.invoice as SyncedInvoiceResponse
          return (
            <span data-testid="invoice-paid-status">
              <span className={`paid-indicator ${inv.isPaid ? 'paid-indicator--paid' : 'paid-indicator--unpaid'}`} />
              {inv.isPaid ? 'Opłacona' : 'Nieopłacona'}
            </span>
          )
        },
      },
      {
        key: 'buyer',
        label: 'Nabywca / Sprzedawca',
        render: (row) => {
          if (row.source === 'Aggregate') {
            return <span>{(row.invoice as InvoiceReadDto).buyer.name}</span>
          }
          const inv = row.invoice as SyncedInvoiceResponse
          return <span>{inv.buyerName ?? inv.vendorName}</span>
        },
      },
      {
        key: 'issueDate',
        label: 'Data wystawienia',
        render: (row) => {
          const date = row.source === 'Aggregate'
            ? (row.invoice as InvoiceReadDto).issueDate
            : (row.invoice as SyncedInvoiceResponse).issueDate
          return new Date(date).toLocaleDateString('pl-PL')
        },
      },
      {
        key: 'gross',
        label: 'Brutto',
        render: (row) => {
          if (row.source === 'Aggregate') {
            const inv = row.invoice as InvoiceReadDto
            return (
              <span style={{ fontWeight: 600 }}>
                {inv.totalGross.amount.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} {inv.currency}
              </span>
            )
          }
          const inv = row.invoice as SyncedInvoiceResponse
          return (
            <span style={{ fontWeight: 600 }}>
              {inv.amountGross.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} {inv.currency}
            </span>
          )
        },
      },
      {
        key: 'id',
        label: '',
        render: (row) => {
          const href = row.source === 'Aggregate'
            ? `/invoices/aggregate/${encodeURIComponent((row.invoice as InvoiceReadDto).id)}?tenantId=${effectiveTenantId}`
            : `/invoices/${encodeURIComponent((row.invoice as SyncedInvoiceResponse).ksefInvoiceNumber)}?tenantId=${effectiveTenantId}`
          return (
            <Link
              to={href}
              data-testid="invoice-view-details"
              className="btn-action btn-action--edit"
            >
              Otwórz →
            </Link>
          )
        },
      },
    ],
    [effectiveTenantId],
  )

  const totalPages = syncedQuery.data?.totalPages ?? 0
  const totalCount = (syncedQuery.data?.totalCount ?? 0) + (aggregateQuery.data?.totalCount ?? 0)

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

  const onChangeTenant = (event: ChangeEvent<HTMLSelectElement>): void => {
    setDraftTenantId(event.target.value)
  }

  const onChangePageSize = (event: ChangeEvent<HTMLSelectElement>): void => {
    const nextPageSize = parseNumber(event.target.value, 25)
    setDraftPageSize(nextPageSize)
  }

  return (
    <section>
      <header className="page-header">
        <h1>Faktury</h1>
        <div className="page-header__actions">
          <Link to="/invoices/new" className="ui-button ui-button--primary">
            Nowa faktura
          </Link>
          <Link to="/invoices/final-from-advances" className="ui-button ui-button--secondary">
            Finalna z zaliczek
          </Link>
          <button
            data-testid="invoice-refresh-button"
            type="button"
            onClick={() => {
              void tenantsQuery.refetch()
              void syncedQuery.refetch()
              void aggregateQuery.refetch()
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
        isLoading={isLoading}
        error={combinedError}
        isEmpty={mergedRows.length === 0}
        loadingLines={6}
        emptyTitle="Nie znaleziono faktur"
        emptyMessage="Zmień filtry lub wybierz inną firmę."
        onRetry={() => {
          void tenantsQuery.refetch()
          void syncedQuery.refetch()
          void aggregateQuery.refetch()
        }}
      >
        <>
          <Table
            testId="invoice-table"
            columns={columns}
            data={mergedRows}
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
