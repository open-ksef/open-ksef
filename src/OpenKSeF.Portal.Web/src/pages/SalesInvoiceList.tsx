import { useQuery } from '@tanstack/react-query'
import { useMemo, useState, type ChangeEvent, type ReactElement } from 'react'
import { Link, useSearchParams } from 'react-router-dom'

import { listAggregateInvoices } from '@/api/invoicesAggregateApi'
import type { DocumentKind, InvoiceReadDto } from '@/api/schemas/invoice'
import { listTenants } from '@/api/endpoints/tenants'
import { AsyncStateView } from '@/components/AsyncStateView'
import { FilterBar } from '@/components/FilterBar'
import { Table, type TableColumn } from '@/components/Table'
import { DocumentKindChip } from '@/components/invoices/DocumentKindChip'
import { DocumentStatusBadge } from '@/components/invoices/DocumentStatusBadge'

function parseNumber(value: string | null, fallback: number): number {
  if (!value) return fallback
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

type KindFilter = DocumentKind | 'All'

const kindTabs: { label: string; value: KindFilter }[] = [
  { label: 'Wszystkie', value: 'All' },
  { label: 'Faktury VAT', value: 'VatInvoice' },
  { label: 'Zaliczkowe', value: 'AdvanceInvoice' },
  { label: 'Finalne', value: 'FinalInvoice' },
  { label: 'Pro forma', value: 'Proforma' },
  { label: 'Korekty', value: 'CorrectionInvoice' },
]

export function SalesInvoiceListPage(): ReactElement {
  const [searchParams, setSearchParams] = useSearchParams()

  const tenantIdFromUrl = searchParams.get('tenantId') ?? ''
  const dateFromFromUrl = searchParams.get('dateFrom') ?? ''
  const dateToFromUrl = searchParams.get('dateTo') ?? ''
  const pageFromUrl = parseNumber(searchParams.get('page'), 1)
  const pageSizeFromUrl = parseNumber(searchParams.get('pageSize'), 25)
  const kindFromUrl = (searchParams.get('kind') ?? 'All') as KindFilter

  const [draftTenantId, setDraftTenantId] = useState(tenantIdFromUrl)
  const [draftDateFrom, setDraftDateFrom] = useState(dateFromFromUrl)
  const [draftDateTo, setDraftDateTo] = useState(dateToFromUrl)
  const [draftPageSize, setDraftPageSize] = useState(pageSizeFromUrl)

  const tenantsQuery = useQuery({
    queryKey: ['tenants', 'sales-invoice-filters'],
    queryFn: () => listTenants(),
  })

  const effectiveTenantId = draftTenantId || tenantsQuery.data?.[0]?.id || ''

  const kindFilter: DocumentKind[] | undefined = kindFromUrl === 'All' ? undefined : [kindFromUrl]

  const invoicesQuery = useQuery({
    queryKey: ['invoices', 'aggregate', 'list', { tenantId: effectiveTenantId, dateFrom: dateFromFromUrl, dateTo: dateToFromUrl, page: pageFromUrl, pageSize: pageSizeFromUrl, kind: kindFromUrl }],
    queryFn: () =>
      listAggregateInvoices(effectiveTenantId, {
        from: dateFromFromUrl || undefined,
        to: dateToFromUrl || undefined,
        page: pageFromUrl,
        pageSize: pageSizeFromUrl,
        kind: kindFilter,
      }),
    enabled: Boolean(effectiveTenantId),
    retry: false,
  })

  const rows = invoicesQuery.data?.items ?? []

  const columns = useMemo<TableColumn<InvoiceReadDto>[]>(
    () => [
      {
        key: 'documentNumber',
        label: 'Nr faktury',
        render: (inv) => (
          <span className="mono-number" data-testid="invoice-number">
            {inv.documentNumber ?? '—'}
          </span>
        ),
      },
      {
        key: 'ksefDocumentNumber',
        label: 'Numer KSeF',
        render: (inv) => (
          <span className="token-display" data-testid="invoice-ksef-number">
            {inv.ksefDocumentNumber ?? '—'}
          </span>
        ),
      },
      {
        key: 'kind',
        label: 'Typ',
        render: (inv) => <DocumentKindChip kind={inv.kind} />,
      },
      {
        key: 'status',
        label: 'Status',
        render: (inv) => <DocumentStatusBadge status={inv.status} />,
      },
      {
        key: 'buyer',
        label: 'Nabywca',
        render: (inv) => <span>{inv.buyer.name}</span>,
      },
      {
        key: 'issueDate',
        label: 'Data wystawienia',
        render: (inv) => new Date(inv.issueDate).toLocaleDateString('pl-PL'),
      },
      {
        key: 'totalGross',
        label: 'Brutto',
        render: (inv) => (
          <span style={{ fontWeight: 600 }}>
            {inv.totalGross.amount.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} {inv.currency}
          </span>
        ),
      },
      {
        key: 'id',
        label: '',
        render: (inv) => (
          <Link
            to={`/invoices/aggregate/${encodeURIComponent(inv.id)}?tenantId=${effectiveTenantId}`}
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

  const updateSearch = (params: { tenantId: string; dateFrom: string; dateTo: string; page: number; pageSize: number; kind: KindFilter }): void => {
    const next = new URLSearchParams()
    if (params.tenantId) next.set('tenantId', params.tenantId)
    if (params.dateFrom) next.set('dateFrom', params.dateFrom)
    if (params.dateTo) next.set('dateTo', params.dateTo)
    next.set('page', String(params.page))
    next.set('pageSize', String(params.pageSize))
    if (params.kind !== 'All') next.set('kind', params.kind)
    setSearchParams(next)
  }

  const onApplyFilters = () => {
    updateSearch({
      tenantId: draftTenantId || effectiveTenantId,
      dateFrom: draftDateFrom,
      dateTo: draftDateTo,
      page: 1,
      pageSize: draftPageSize,
      kind: kindFromUrl,
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
      kind: 'All',
    })
  }

  const onChangePage = (nextPage: number): void => {
    updateSearch({
      tenantId: tenantIdFromUrl || effectiveTenantId,
      dateFrom: dateFromFromUrl,
      dateTo: dateToFromUrl,
      page: nextPage,
      pageSize: pageSizeFromUrl,
      kind: kindFromUrl,
    })
  }

  const onChangeKind = (kind: KindFilter): void => {
    updateSearch({
      tenantId: tenantIdFromUrl || effectiveTenantId,
      dateFrom: dateFromFromUrl,
      dateTo: dateToFromUrl,
      page: 1,
      pageSize: pageSizeFromUrl,
      kind,
    })
  }

  const isLoading = tenantsQuery.isLoading || invoicesQuery.isLoading
  const combinedError = tenantsQuery.error ?? invoicesQuery.error

  return (
    <section>
      <header className="page-header">
        <h1>Faktury sprzedaży</h1>
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
              void invoicesQuery.refetch()
            }}
          >
            ↺ Odśwież
          </button>
        </div>
      </header>

      <div className="kind-tab-bar" role="tablist" aria-label="Typ faktury" data-testid="kind-tab-bar">
        {kindTabs.map((tab) => (
          <button
            key={tab.value}
            type="button"
            role="tab"
            aria-selected={kindFromUrl === tab.value}
            data-testid={`kind-tab-${tab.value}`}
            className={`kind-tab${kindFromUrl === tab.value ? ' kind-tab--active' : ''}`}
            onClick={() => onChangeKind(tab.value)}
          >
            {tab.label}
          </button>
        ))}
      </div>

      <FilterBar
        onApply={onApplyFilters}
        onReset={onResetFilters}
        applyButtonTestId="invoice-apply-filters"
      >
        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
          <label htmlFor="sales-tenant-filter" className="ui-filter-label">
            Firma
          </label>
          <select
            id="sales-tenant-filter"
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
          <label htmlFor="sales-date-from" className="ui-filter-label">
            Od
          </label>
          <input
            id="sales-date-from"
            data-testid="invoice-date-from"
            type="date"
            value={draftDateFrom}
            onChange={(e) => setDraftDateFrom(e.target.value)}
          />
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
          <label htmlFor="sales-date-to" className="ui-filter-label">
            Do
          </label>
          <input
            id="sales-date-to"
            data-testid="invoice-date-to"
            type="date"
            value={draftDateTo}
            onChange={(e) => setDraftDateTo(e.target.value)}
          />
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
          <label htmlFor="sales-page-size" className="ui-filter-label">
            Na stronę
          </label>
          <select
            id="sales-page-size"
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
        emptyTitle="Nie znaleziono faktur sprzedaży"
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
