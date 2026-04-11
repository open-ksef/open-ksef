import { useQuery } from '@tanstack/react-query'
import { useState, type ReactElement } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'

import { getInvoicePrint, type PrintVariant } from '@/api/invoicesAggregateApi'
import { AsyncStateView } from '@/components/AsyncStateView'
import { PrintVariantSwitcher } from '@/components/invoices/PrintVariantSwitcher'
import { TotalsSummaryCard } from '@/components/invoices/TotalsSummaryCard'
import { InvoiceLineTable } from '@/components/invoices/InvoiceLineTable'

export function InvoicePrintViewPage(): ReactElement {
  const { id = '' } = useParams()
  const [searchParams] = useSearchParams()
  const tenantId = searchParams.get('tenantId') ?? ''

  const [variant, setVariant] = useState<PrintVariant>('Standard')

  const tenantQuery = tenantId ? `?tenantId=${encodeURIComponent(tenantId)}` : ''
  const base = `/invoices/aggregate/${encodeURIComponent(id)}`

  const printQuery = useQuery({
    queryKey: ['invoices', 'aggregate', 'print', { tenantId, id, variant }],
    queryFn: () => getInvoicePrint(tenantId, id, variant),
    enabled: Boolean(tenantId && id),
    retry: false,
  })

  const model = printQuery.data ?? null
  const invoice = model?.invoiceData ?? null
  const labels = model?.labels
  const isDraft = invoice?.status === 'Draft'
  const disabledVariants: PrintVariant[] = isDraft ? ['Duplicate'] : []

  return (
    <section className="invoice-print-view__wrapper">
      <div className="invoice-print-view__controls no-print">
        <Link className="back-link" to={`${base}${tenantQuery}`}>
          ← Powrót do faktury
        </Link>
        <PrintVariantSwitcher
          variant={variant}
          onChange={(v) => setVariant(v)}
          disabledVariants={disabledVariants}
        />
        <button
          type="button"
          className="ui-button ui-button--primary no-print"
          onClick={() => window.print()}
        >
          Drukuj
        </button>
      </div>

      <AsyncStateView
        isLoading={printQuery.isLoading}
        error={printQuery.error}
        isEmpty={!printQuery.isLoading && !printQuery.error && !model}
        emptyTitle="Nie znaleziono dokumentu"
        emptyMessage="Brak danych do wydruku."
        onRetry={() => void printQuery.refetch()}
      >
        {model && invoice && labels ? (
          <article className="invoice-print-view" data-testid="print-view">
            {model.variant === 'Duplicate' && model.duplicateInfo ? (
              <p className="invoice-print-view__duplicate-stamp">{labels.duplicateLabel}</p>
            ) : null}

            <header className="invoice-print-view__header">
              <h1 className="invoice-print-view__title">{labels.invoiceTitle}</h1>
              <dl className="invoice-print-view__meta">
                <dt>{labels.documentNumberLabel}</dt>
                <dd data-testid="print-document-number">{invoice.documentNumber ?? '—'}</dd>
                <dt>{labels.issueDateLabel}</dt>
                <dd>{invoice.issueDate}</dd>
                {invoice.saleDate ? (
                  <>
                    <dt>{labels.saleDateLabel}</dt>
                    <dd>{invoice.saleDate}</dd>
                  </>
                ) : null}
                {invoice.dueDate ? (
                  <>
                    <dt>{labels.dueDateLabel}</dt>
                    <dd>{invoice.dueDate}</dd>
                  </>
                ) : null}
                <dt>{labels.currencyLabel}</dt>
                <dd>{invoice.currency}</dd>
              </dl>
            </header>

            <div className="invoice-print-view__parties">
              <section className="invoice-print-view__party">
                <h2 className="invoice-print-view__party-title">{labels.sellerLabel}</h2>
                <p>{invoice.seller.name}</p>
                {invoice.seller.nip ? <p>NIP: {invoice.seller.nip}</p> : null}
              </section>
              <section className="invoice-print-view__party">
                <h2 className="invoice-print-view__party-title">{labels.buyerLabel}</h2>
                <p>{invoice.buyer.name}</p>
                {invoice.buyer.nip ? <p>NIP: {invoice.buyer.nip}</p> : null}
              </section>
            </div>

            <InvoiceLineTable lines={invoice.lines} />

            <TotalsSummaryCard
              net={invoice.totalNet}
              vat={invoice.totalVat}
              gross={invoice.totalGross}
              currency={invoice.currency}
            />
          </article>
        ) : null}
      </AsyncStateView>
    </section>
  )
}
