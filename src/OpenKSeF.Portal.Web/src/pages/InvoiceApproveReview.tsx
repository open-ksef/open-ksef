import { useMutation, useQuery } from '@tanstack/react-query'
import { useState, type ReactElement } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'

import { InvoiceValidationError, approveInvoice, getAggregateInvoice } from '@/api/invoicesAggregateApi'
import type { InvoiceReadDto, ValidationEnvelope } from '@/api/schemas/invoice'
import { AsyncStateView } from '@/components/AsyncStateView'
import { DocumentKindChip } from '@/components/invoices/DocumentKindChip'
import { DocumentStatusBadge } from '@/components/invoices/DocumentStatusBadge'
import { InvoiceLineTable } from '@/components/invoices/InvoiceLineTable'
import { PartyCard } from '@/components/invoices/PartyCard'
import { TotalsSummaryCard } from '@/components/invoices/TotalsSummaryCard'
import { ValidationMessageList } from '@/components/invoices/ValidationMessageList'

export function InvoiceApproveReviewPage(): ReactElement {
  const { id = '' } = useParams()
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const tenantId = searchParams.get('tenantId') ?? ''

  const [serverValidation, setServerValidation] = useState<ValidationEnvelope | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)

  const invoiceQuery = useQuery({
    queryKey: ['invoices', 'aggregate', 'approve-review', { tenantId, id }],
    queryFn: () => getAggregateInvoice(tenantId, id),
    enabled: Boolean(tenantId && id),
    retry: false,
  })

  const invoice = invoiceQuery.data ?? null

  const approveMutation = useMutation({
    mutationFn: () => approveInvoice(tenantId, id),
    onSuccess: (approved) => {
      const detailSearch = tenantId ? `?tenantId=${encodeURIComponent(tenantId)}` : ''
      navigate(`/invoices/aggregate/${encodeURIComponent(approved.id)}${detailSearch}`)
    },
    onError: (error) => {
      if (error instanceof InvoiceValidationError) {
        setServerValidation({ stage: error.stage, messages: error.messages })
        setSubmitError(null)
        return
      }

      setServerValidation(null)
      setSubmitError(error instanceof Error ? error.message : 'Nie udało się zatwierdzić faktury.')
    },
  })

  const clientPreview = invoice ? computeClientPreview(invoice) : []
  const hasClientErrors = clientPreview.some((m) => m.severity === 'Error')

  return (
    <section>
      <Link
        className="back-link"
        to={
          id
            ? `/invoices/aggregate/${encodeURIComponent(id)}${tenantId ? `?tenantId=${encodeURIComponent(tenantId)}` : ''}`
            : '/invoices'
        }
      >
        ← Powrót do faktury
      </Link>

      <header className="page-header">
        <h1>Zatwierdzanie faktury</h1>
      </header>

      <AsyncStateView
        isLoading={invoiceQuery.isLoading}
        error={invoiceQuery.error}
        isEmpty={!invoiceQuery.isLoading && !invoiceQuery.error && !invoice}
        emptyTitle="Nie znaleziono faktury"
        emptyMessage="Nie znaleziono dokumentu do zatwierdzenia."
        onRetry={() => void invoiceQuery.refetch()}
      >
        {invoice ? (
          <div className="invoice-approve-review" data-testid="invoice-approve-review">
            <div className="invoice-approve-review__header">
              <h2>{invoice.documentNumber ?? '—'}</h2>
              <DocumentKindChip kind={invoice.kind} />
              <DocumentStatusBadge status={invoice.status} />
            </div>

            <div className="invoice-approve-review__parties">
              <PartyCard party={invoice.seller} title="Sprzedawca" />
              <PartyCard party={invoice.buyer} title="Nabywca" />
            </div>

            <dl className="invoice-approve-review__dates">
              <dt>Data wystawienia</dt>
              <dd>{new Date(invoice.issueDate).toLocaleDateString('pl-PL')}</dd>
              {invoice.dueDate ? (
                <>
                  <dt>Termin płatności</dt>
                  <dd>{new Date(invoice.dueDate).toLocaleDateString('pl-PL')}</dd>
                </>
              ) : null}
            </dl>

            <InvoiceLineTable lines={invoice.lines} showCorrectionColumns={invoice.kind === 'CorrectionInvoice'} />

            <TotalsSummaryCard
              net={invoice.totalNet}
              vat={invoice.totalVat}
              gross={invoice.totalGross}
              currency={invoice.currency}
            />

            {clientPreview.length > 0 ? (
              <ValidationMessageList stage="Approve" messages={clientPreview} />
            ) : null}

            {serverValidation ? (
              <ValidationMessageList stage={serverValidation.stage} messages={serverValidation.messages} />
            ) : null}

            {submitError ? <p role="alert">{submitError}</p> : null}

            <div className="invoice-approve-review__actions">
              <button
                className="ui-button ui-button--primary"
                data-testid="approve-button"
                type="button"
                disabled={approveMutation.isPending || hasClientErrors}
                onClick={() => {
                  setServerValidation(null)
                  setSubmitError(null)
                  void approveMutation.mutate()
                }}
              >
                {approveMutation.isPending ? 'Zatwierdzanie...' : 'Zatwierdź'}
              </button>
            </div>
          </div>
        ) : null}
      </AsyncStateView>
    </section>
  )
}

function computeClientPreview(invoice: InvoiceReadDto): ValidationEnvelope['messages'] {
  const messages: ValidationEnvelope['messages'] = []

  // INV-VAL-002: fiscal document must contain at least one line
  if (invoice.lines.length === 0) {
    messages.push({
      code: 'INV-VAL-002',
      severity: 'Error',
      field: null,
      messagePl: 'Faktura musi zawierać co najmniej jedną pozycję.',
      messageTechnical: 'No line items found for fiscal document.',
    })
  }

  // INV-VAL-013: B2B buyer requires valid NIP
  if (invoice.buyerKind === 'Business' && !invoice.buyer.nip) {
    messages.push({
      code: 'INV-VAL-013',
      severity: 'Error',
      field: 'buyer.nip',
      messagePl: 'Dla nabywcy B2B wymagany jest poprawny NIP.',
      messageTechnical: 'BuyerKind=Business but BuyerSnapshot.Nip missing/invalid.',
    })
  }

  return messages
}
