import { useQuery } from '@tanstack/react-query'
import type { ReactElement } from 'react'
import { Link, useSearchParams } from 'react-router-dom'

import { listIssuedInvoices } from '@/api/endpoints/issuedInvoices'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import type { IssuedInvoiceListItemResponse, IssuedInvoiceStatus } from '@/api/types'
import { AsyncStateView } from '@/components/AsyncStateView'
import { EnvBadge, KsefStatusBadge } from '@/components/invoicing/KsefStatusBadge'
import '@/components/invoicing/invoicing.css'
import { Table, type TableColumn } from '@/components/Table'
import { useTenants } from '@/hooks/useTenants'
import { formatDate, formatMoney } from '@/invoicing/format'

const PAGE_SIZE = 20

const tabs: { status: IssuedInvoiceStatus | ''; label: string }[] = [
  { status: '', label: 'Wszystkie' },
  { status: 'Draft', label: 'Szkice' },
  { status: 'Sending', label: 'Wysyłane' },
  { status: 'Accepted', label: 'Przyjęte' },
  { status: 'Rejected', label: 'Odrzucone' },
]

export function IssuedInvoiceListPage(): ReactElement {
  const [searchParams, setSearchParams] = useSearchParams()
  const tenantsQuery = useTenants()
  const tenantId = searchParams.get('tenantId') || tenantsQuery.data?.[0]?.id || ''
  const status = (searchParams.get('status') ?? '') as IssuedInvoiceStatus | ''
  const page = Math.max(1, Number(searchParams.get('page')) || 1)

  const profileQuery = useQuery({
    queryKey: ['seller-profile', tenantId],
    queryFn: () => getSellerProfile(tenantId),
    enabled: Boolean(tenantId),
  })
  const listQuery = useQuery({
    queryKey: ['issued-invoices', tenantId, status, page],
    queryFn: () => listIssuedInvoices(tenantId, { page, pageSize: PAGE_SIZE, status: status || undefined }),
    enabled: Boolean(tenantId),
  })

  const update = (next: { tenantId?: string; status?: string; page?: number }): void => {
    const params = new URLSearchParams()
    const values = { tenantId, status, page, ...next }
    if (values.tenantId) params.set('tenantId', values.tenantId)
    if (values.status) params.set('status', values.status)
    if (values.page > 1) params.set('page', String(values.page))
    setSearchParams(params)
  }

  const tenantQuery = `tenantId=${encodeURIComponent(tenantId)}`
  const rows = listQuery.data?.items ?? []
  const totalPages = Math.max(listQuery.data?.totalPages ?? 1, 1)

  const columns: TableColumn<IssuedInvoiceListItemResponse>[] = [
    {
      key: 'number',
      label: 'Numer',
      render: (row) => <span className="mono">{row.number ?? '— (szkic)'}</span>,
    },
    {
      key: 'buyerName',
      label: 'Nabywca',
      render: (row) => (
        <>
          {row.buyerName ?? '—'}
          {row.buyerNip ? <div className="inv-help mono">{row.buyerNip}</div> : null}
        </>
      ),
    },
    { key: 'issueDate', label: 'Wystawiona', render: (row) => formatDate(row.issueDate) },
    { key: 'totalGross', label: 'Brutto', render: (row) => <span className="num">{formatMoney(row.totalGross, row.currency)}</span> },
    { key: 'status', label: 'Status', render: (row) => <KsefStatusBadge status={row.status} /> },
    {
      key: 'ksefNumber',
      label: 'Numer KSeF',
      render: (row) => <span className="mono inv-help">{row.ksefNumber ?? '—'}</span>,
    },
    {
      key: 'id',
      label: '',
      render: (row) =>
        row.status === 'Draft' || row.status === 'Rejected' ? (
          <Link
            to={`/issued/${encodeURIComponent(row.id)}/edit?${tenantQuery}`}
            className="btn-action btn-action--edit"
            aria-label={`Otwórz ${row.number ?? 'szkic'}`}
          >
            Otwórz →
          </Link>
        ) : null,
    },
  ]

  return (
    <div className="invoicing">
      <header className="inv-header">
        <h1>Wystawione faktury</h1>
        {profileQuery.data ? <EnvBadge environment={profileQuery.data.ksefEnvironment} /> : null}
        {(tenantsQuery.data?.length ?? 0) > 1 ? (
          <div className="ui-form-group">
            <label htmlFor="issued-tenant" className="visually-hidden">
              Firma
            </label>
            <select id="issued-tenant" value={tenantId} onChange={(event) => update({ tenantId: event.target.value, page: 1 })}>
              {tenantsQuery.data?.map((tenant) => (
                <option key={tenant.id} value={tenant.id}>
                  {tenant.displayName || tenant.nip}
                </option>
              ))}
            </select>
          </div>
        ) : null}
        <div className="inv-header__actions">
          <Link to={`/issued/new?${tenantQuery}`} className="ui-button ui-button--primary ui-button--md">
            + Nowa faktura
          </Link>
        </div>
      </header>

      <div className="inv-tabs" role="group" aria-label="Filtr statusu">
        {tabs.map((tab) => (
          <button
            key={tab.label}
            type="button"
            aria-pressed={status === tab.status}
            onClick={() => update({ status: tab.status, page: 1 })}
          >
            {tab.label}
          </button>
        ))}
      </div>

      <AsyncStateView
        isLoading={tenantsQuery.isLoading || listQuery.isLoading}
        error={tenantsQuery.error ?? listQuery.error}
        isEmpty={rows.length === 0}
        emptyTitle={status ? 'Brak faktur o tym statusie' : 'Nie wystawiono jeszcze faktur'}
        emptyMessage="Kliknij „Nowa faktura”, aby wystawić pierwszą."
        onRetry={() => void listQuery.refetch()}
      >
        <Table testId="issued-invoice-table" columns={columns} data={rows} />
        <nav className="ui-pagination" aria-label="Paginacja wystawionych faktur">
          <span className="ui-pagination-info">
            Strona {page} z {totalPages} · {listQuery.data?.totalCount ?? 0} łącznie
          </span>
          <div className="ui-pagination-controls">
            <button type="button" disabled={page <= 1} onClick={() => update({ page: page - 1 })}>
              ← Poprzednia
            </button>
            <button type="button" disabled={page >= totalPages} onClick={() => update({ page: page + 1 })}>
              Następna →
            </button>
          </div>
        </nav>
      </AsyncStateView>
    </div>
  )
}
