import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type ReactElement } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'

import { InvoiceValidationError, getAggregateInvoice, reopenInvoice } from '@/api/invoicesAggregateApi'
import type { InvoiceReadDto } from '@/api/schemas/invoice'
import { listTenants } from '@/api/endpoints/tenants'
import { AsyncStateView } from '@/components/AsyncStateView'
import { AdvanceAllocationList } from '@/components/invoices/AdvanceAllocationList'
import { CorrectionReferenceCard } from '@/components/invoices/CorrectionReferenceCard'
import { DocumentKindChip } from '@/components/invoices/DocumentKindChip'
import { DocumentStatusBadge } from '@/components/invoices/DocumentStatusBadge'
import { DuplicateIssuanceBanner } from '@/components/invoices/DuplicateIssuanceBanner'
import { InvoiceLineTable } from '@/components/invoices/InvoiceLineTable'
import { KsefIdentifiersCard } from '@/components/invoices/KsefIdentifiersCard'
import { KsefRequirementBanner } from '@/components/invoices/KsefRequirementBanner'
import { KsefSubmissionStatus } from '@/components/invoices/KsefSubmissionStatus'
import { PartyCard } from '@/components/invoices/PartyCard'
import { TotalsSummaryCard } from '@/components/invoices/TotalsSummaryCard'

const POLLING_INTERVAL_MS = 3000

export function InvoiceAggregateDetailPage(): ReactElement {
  const { id = '' } = useParams()
  const [searchParams] = useSearchParams()

  const tenantIdFromUrl = searchParams.get('tenantId') ?? ''

  const tenantsQuery = useQuery({
    queryKey: ['tenants', 'invoice-aggregate-detail'],
    queryFn: () => listTenants(),
  })

  const effectiveTenantId = tenantIdFromUrl || tenantsQuery.data?.[0]?.id || ''

  const queryClient = useQueryClient()
  const [reopenError, setReopenError] = useState<string | null>(null)

  const invoiceQuery = useQuery({
    queryKey: ['invoices', 'aggregate', 'detail', { tenantId: effectiveTenantId, id }],
    queryFn: () => getAggregateInvoice(effectiveTenantId, id),
    enabled: Boolean(effectiveTenantId && id),
    retry: false,
    refetchInterval: (query) => {
      const data = query.state.data
      if (data?.status === 'SubmittedToKsef') {
        return POLLING_INTERVAL_MS
      }

      return false
    },
  })

  const reopenMutation = useMutation({
    mutationFn: () => reopenInvoice(effectiveTenantId, id),
    onSuccess: () => {
      setReopenError(null)
      void queryClient.invalidateQueries({
        queryKey: ['invoices', 'aggregate', 'detail', { tenantId: effectiveTenantId, id }],
      })
    },
    onError: (error) => {
      if (error instanceof InvoiceValidationError) {
        setReopenError(error.messages[0]?.messagePl ?? 'Nie udało się cofnąć zatwierdzenia.')
      } else {
        setReopenError('Nie udało się cofnąć zatwierdzenia.')
      }
    },
  })

  const invoice = invoiceQuery.data
  const isLoading = tenantsQuery.isLoading || invoiceQuery.isLoading
  const hasData = Boolean(invoice)

  return (
    <section>
      <Link
        data-testid="aggregate-detail-back-link"
        to={tenantIdFromUrl ? `/invoices/sales?tenantId=${encodeURIComponent(tenantIdFromUrl)}` : '/invoices/sales'}
        className="back-link"
      >
        ← Powrót do faktur sprzedaży
      </Link>

      <header className="page-header">
        <h1>Faktura sprzedaży</h1>
      </header>

      <AsyncStateView
        isLoading={isLoading}
        error={invoiceQuery.error}
        isEmpty={!isLoading && !invoiceQuery.error && !hasData}
        loadingLines={6}
        emptyTitle="Nie znaleziono faktury"
        emptyMessage="Nie znaleziono żądanej faktury."
        onRetry={() => void invoiceQuery.refetch()}
      >
        {invoice ? (
          <div className="invoice-doc" data-testid="aggregate-invoice-detail">
            {/* Header */}
            <div className="invoice-doc-header">
              <HeaderSection invoice={invoice} />
            </div>

            {/* Parties */}
            <div className="invoice-doc-parties">
              <PartyCard party={invoice.seller} title="Sprzedawca" />
              <PartyCard party={invoice.buyer} title="Nabywca" />
            </div>

            {/* Dates */}
            <DatesSection invoice={invoice} />

            {/* Commercial / Notes */}
            {invoice.paymentMethod ? (
              <div className="invoice-doc-commercial">
                <p><strong>Forma płatności:</strong> {invoice.paymentMethod}</p>
              </div>
            ) : null}

            {invoice.publicNotes ? (
              <div className="invoice-doc-notes">
                <p><strong>Uwagi:</strong> {invoice.publicNotes}</p>
              </div>
            ) : null}

            {/* Lines */}
            <InvoiceLineTable
              lines={invoice.lines}
              showCorrectionColumns={invoice.kind === 'CorrectionInvoice'}
            />

            {/* Totals */}
            <div className="invoice-doc-totals">
              <TotalsSummaryCard
                net={invoice.totalNet}
                vat={invoice.totalVat}
                gross={invoice.totalGross}
                currency={invoice.currency}
              />
            </div>

            {/* Supplements */}
            <div className="invoice-doc-supplements">
              {invoice.correctionReference ? (
                <CorrectionReferenceCard reference={invoice.correctionReference} />
              ) : null}

              {invoice.settledAdvanceAllocations.length > 0 ? (
                <AdvanceAllocationList allocations={invoice.settledAdvanceAllocations} />
              ) : null}

              {invoice.duplicateIssuances.length > 0 ? (
                <DuplicateIssuanceBanner issuances={invoice.duplicateIssuances} />
              ) : null}

              <KsefIdentifiersCard
                ksefDocumentNumber={invoice.ksefDocumentNumber}
                ksefReferenceNumber={invoice.ksefReferenceNumber}
              />

              {invoice.ksefRejectionReason ? (
                <div className="invoice-aggregate-detail__rejection" role="alert">
                  <strong>Powód odrzucenia przez KSeF:</strong> {invoice.ksefRejectionReason}
                </div>
              ) : null}
            </div>

            {/* Footer actions */}
            <div className="invoice-doc-footer">
              {reopenError ? <p role="alert">{reopenError}</p> : null}
              <ActionButtons
                invoice={invoice}
                tenantId={effectiveTenantId}
                id={id}
                onReopen={() => {
                  setReopenError(null)
                  void reopenMutation.mutate()
                }}
                isReopening={reopenMutation.isPending}
              />
            </div>
          </div>
        ) : null}
      </AsyncStateView>
    </section>
  )
}

function HeaderSection({ invoice }: { invoice: InvoiceReadDto }): ReactElement {
  return (
    <>
      <div className="invoice-doc-header__title-row">
        <h2 className="invoice-doc-header__number">{invoice.documentNumber ?? '—'}</h2>
        <DocumentKindChip kind={invoice.kind} />
        <DocumentStatusBadge status={invoice.status} />
      </div>
      <div className="invoice-doc-header__meta">
        <KsefRequirementBanner requirement={invoice.ksefSubmissionRequirement} />
        <KsefSubmissionStatus
          state={invoice.ksefSubmissionState}
          identifiers={
            invoice.ksefDocumentNumber || invoice.ksefReferenceNumber
              ? { ksefDocumentNumber: invoice.ksefDocumentNumber, ksefReferenceNumber: invoice.ksefReferenceNumber }
              : undefined
          }
          rejectionReason={invoice.ksefRejectionReason ?? undefined}
        />
      </div>
    </>
  )
}

function DatesSection({ invoice }: { invoice: InvoiceReadDto }): ReactElement {
  return (
    <div className="invoice-doc-dates">
      <div className="invoice-doc-date-item">
        <span className="invoice-doc-date-item__label">Data wystawienia</span>
        <span className="invoice-doc-date-item__value">{new Date(invoice.issueDate).toLocaleDateString('pl-PL')}</span>
      </div>
      {invoice.saleDate ? (
        <div className="invoice-doc-date-item">
          <span className="invoice-doc-date-item__label">Data sprzedaży</span>
          <span className="invoice-doc-date-item__value">{new Date(invoice.saleDate).toLocaleDateString('pl-PL')}</span>
        </div>
      ) : null}
      {invoice.dueDate ? (
        <div className="invoice-doc-date-item">
          <span className="invoice-doc-date-item__label">Termin płatności</span>
          <span className="invoice-doc-date-item__value">{new Date(invoice.dueDate).toLocaleDateString('pl-PL')}</span>
        </div>
      ) : null}
      {invoice.approvedAt ? (
        <div className="invoice-doc-date-item">
          <span className="invoice-doc-date-item__label">Zatwierdzona</span>
          <span className="invoice-doc-date-item__value">{new Date(invoice.approvedAt).toLocaleString('pl-PL')}</span>
        </div>
      ) : null}
      {invoice.submittedToKsefAt ? (
        <div className="invoice-doc-date-item">
          <span className="invoice-doc-date-item__label">Wysłana do KSeF</span>
          <span className="invoice-doc-date-item__value">{new Date(invoice.submittedToKsefAt).toLocaleString('pl-PL')}</span>
        </div>
      ) : null}
      {invoice.acceptedByKsefAt ? (
        <div className="invoice-doc-date-item">
          <span className="invoice-doc-date-item__label">Zaakceptowana przez KSeF</span>
          <span className="invoice-doc-date-item__value">{new Date(invoice.acceptedByKsefAt).toLocaleString('pl-PL')}</span>
        </div>
      ) : null}
    </div>
  )
}

interface ActionButtonsProps {
  invoice: InvoiceReadDto
  tenantId: string
  id: string
  onReopen: () => void
  isReopening: boolean
}

function ActionButtons({ invoice, tenantId, id, onReopen, isReopening }: ActionButtonsProps): ReactElement {
  const base = `/invoices/aggregate/${encodeURIComponent(id)}`
  const tenantQuery = `tenantId=${encodeURIComponent(tenantId)}`

  if (invoice.status === 'Draft') {
    return (
      <div className="invoice-doc-footer__actions">
        <Link to={`${base}/edit?${tenantQuery}`} className="ui-button ui-button--secondary">
          Edytuj
        </Link>
        <Link to={`${base}/approve?${tenantQuery}`} className="ui-button ui-button--primary">
          Zatwierdź
        </Link>
      </div>
    )
  }

  if (invoice.status === 'Approved') {
    const canReopen = invoice.reopenAllowed === true
    return (
      <div className="invoice-doc-footer__actions">
        <Link to={`${base}/submit?${tenantQuery}`} className="ui-button ui-button--primary">
          Wyślij do KSeF
        </Link>
        <button
          type="button"
          className="ui-button ui-button--secondary"
          data-testid="reopen-button"
          disabled={!canReopen || isReopening}
          title={!canReopen ? 'INV-VAL-102' : undefined}
          onClick={onReopen}
        >
          {isReopening ? 'Cofanie...' : 'Odblokuj do edycji'}
        </button>
      </div>
    )
  }

  if (invoice.status === 'SubmittedToKsef') {
    return (
      <div className="invoice-doc-footer__actions">
        <p className="text-muted">Oczekiwanie na odpowiedź KSeF...</p>
      </div>
    )
  }

  if (invoice.status === 'AcceptedByKsef') {
    return (
      <div className="invoice-doc-footer__actions">
        <Link to={`${base}/print?${tenantQuery}`} className="ui-button ui-button--secondary">
          Drukuj
        </Link>
        {invoice.kind !== 'Proforma' ? (
          <Link to={`${base}/corrections/new?${tenantQuery}`} className="ui-button ui-button--secondary">
            Utwórz korektę
          </Link>
        ) : null}
      </div>
    )
  }

  if (invoice.status === 'RejectedByKsef') {
    return (
      <div className="invoice-doc-footer__actions">
        <Link to={`${base}/approve?${tenantQuery}`} className="ui-button ui-button--primary">
          Zatwierdź ponownie
        </Link>
        <Link to={`${base}/corrections/new?${tenantQuery}`} className="ui-button ui-button--secondary">
          Utwórz korektę
        </Link>
      </div>
    )
  }

  return <div className="invoice-doc-footer__actions" />
}
