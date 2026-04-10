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
        to={tenantIdFromUrl ? `/invoices?tenantId=${encodeURIComponent(tenantIdFromUrl)}` : '/invoices'}
        className="back-link"
      >
        ← Powrót do faktur
      </Link>

      <header className="page-header">
        <h1>Szczegóły faktury</h1>
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
          <div className="invoice-aggregate-detail" data-testid="aggregate-invoice-detail">
            <HeaderSection invoice={invoice} />

            <div className="invoice-aggregate-detail__parties">
              <PartyCard party={invoice.seller} title="Sprzedawca" />
              <PartyCard party={invoice.buyer} title="Nabywca" />
            </div>

            <DatesSection invoice={invoice} />

            {invoice.paymentMethod ? (
              <div className="invoice-aggregate-detail__commercial">
                <p><strong>Forma płatności:</strong> {invoice.paymentMethod}</p>
              </div>
            ) : null}

            {invoice.publicNotes ? (
              <div className="invoice-aggregate-detail__notes">
                <p><strong>Uwagi:</strong> {invoice.publicNotes}</p>
              </div>
            ) : null}

            <InvoiceLineTable
              lines={invoice.lines}
              showCorrectionColumns={invoice.kind === 'CorrectionInvoice'}
            />

            <TotalsSummaryCard
              net={invoice.totalNet}
              vat={invoice.totalVat}
              gross={invoice.totalGross}
              currency={invoice.currency}
            />

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
        ) : null}
      </AsyncStateView>
    </section>
  )
}

function HeaderSection({ invoice }: { invoice: InvoiceReadDto }): ReactElement {
  return (
    <div className="invoice-aggregate-detail__header">
      <div className="invoice-aggregate-detail__header-top">
        <h2 className="invoice-aggregate-detail__number">{invoice.documentNumber ?? '—'}</h2>
        <DocumentKindChip kind={invoice.kind} />
        <DocumentStatusBadge status={invoice.status} />
      </div>
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
  )
}

function DatesSection({ invoice }: { invoice: InvoiceReadDto }): ReactElement {
  return (
    <dl className="invoice-aggregate-detail__dates">
      <dt>Data wystawienia</dt>
      <dd>{new Date(invoice.issueDate).toLocaleDateString('pl-PL')}</dd>
      {invoice.saleDate ? (
        <>
          <dt>Data sprzedaży</dt>
          <dd>{new Date(invoice.saleDate).toLocaleDateString('pl-PL')}</dd>
        </>
      ) : null}
      {invoice.dueDate ? (
        <>
          <dt>Termin płatności</dt>
          <dd>{new Date(invoice.dueDate).toLocaleDateString('pl-PL')}</dd>
        </>
      ) : null}
      {invoice.approvedAt ? (
        <>
          <dt>Zatwierdzona</dt>
          <dd>{new Date(invoice.approvedAt).toLocaleString('pl-PL')}</dd>
        </>
      ) : null}
      {invoice.submittedToKsefAt ? (
        <>
          <dt>Wysłana do KSeF</dt>
          <dd>{new Date(invoice.submittedToKsefAt).toLocaleString('pl-PL')}</dd>
        </>
      ) : null}
      {invoice.acceptedByKsefAt ? (
        <>
          <dt>Zaakceptowana przez KSeF</dt>
          <dd>{new Date(invoice.acceptedByKsefAt).toLocaleString('pl-PL')}</dd>
        </>
      ) : null}
    </dl>
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
      <div className="invoice-aggregate-detail__actions">
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
      <div className="invoice-aggregate-detail__actions">
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
      <div className="invoice-aggregate-detail__actions">
        <p className="invoice-aggregate-detail__polling-notice">Oczekiwanie na odpowiedź KSeF...</p>
      </div>
    )
  }

  if (invoice.status === 'AcceptedByKsef') {
    return (
      <div className="invoice-aggregate-detail__actions">
        <Link to={`${base}/print?${tenantQuery}`} className="ui-button ui-button--secondary">
          Drukuj
        </Link>
        <Link to={`${base}/corrections/new?${tenantQuery}`} className="ui-button ui-button--secondary">
          Utwórz korektę
        </Link>
      </div>
    )
  }

  if (invoice.status === 'RejectedByKsef') {
    return (
      <div className="invoice-aggregate-detail__actions">
        <Link to={`${base}/approve?${tenantQuery}`} className="ui-button ui-button--primary">
          Zatwierdź ponownie
        </Link>
        <Link to={`${base}/corrections/new?${tenantQuery}`} className="ui-button ui-button--secondary">
          Utwórz korektę
        </Link>
      </div>
    )
  }

  return <div className="invoice-aggregate-detail__actions" />
}
