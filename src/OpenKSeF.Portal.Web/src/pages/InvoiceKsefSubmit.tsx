import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type ReactElement } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'

import { InvoiceValidationError, getAggregateInvoice, submitInvoiceToKsef } from '@/api/invoicesAggregateApi'
import { type ValidationEnvelope } from '@/api/schemas/invoice'
import { AsyncStateView } from '@/components/AsyncStateView'
import { ValidationMessageList } from '@/components/invoices/ValidationMessageList'

const POLLING_INTERVAL_MS = 3000
const MAX_POLLING_MS = 120_000

export function InvoiceKsefSubmitPage(): ReactElement {
  const { id = '' } = useParams()
  const [searchParams] = useSearchParams()
  const tenantId = searchParams.get('tenantId') ?? ''
  const queryClient = useQueryClient()

  const [serverValidation, setServerValidation] = useState<ValidationEnvelope | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [pollingStartedAt, setPollingStartedAt] = useState<number | null>(null)
  const [pollingTimedOut, setPollingTimedOut] = useState(false)

  const tenantQuery = tenantId ? `?tenantId=${encodeURIComponent(tenantId)}` : ''
  const base = `/invoices/aggregate/${encodeURIComponent(id)}`

  const invoiceQuery = useQuery({
    queryKey: ['invoices', 'aggregate', 'ksef-submit', { tenantId, id }],
    queryFn: () => getAggregateInvoice(tenantId, id),
    enabled: Boolean(tenantId && id),
    retry: false,
    refetchInterval: (query) => {
      const state = query.state.data?.ksefSubmissionState
      if (state === 'Accepted' || state === 'Rejected') return false
      if (!pollingStartedAt || pollingTimedOut) return false
      return POLLING_INTERVAL_MS
    },
  })

  useEffect(() => {
    if (!pollingStartedAt) return
    const remaining = MAX_POLLING_MS - (Date.now() - pollingStartedAt)
    if (remaining <= 0) {
      setPollingTimedOut(true)
      return
    }
    const timer = setTimeout(() => setPollingTimedOut(true), remaining)
    return () => clearTimeout(timer)
  }, [pollingStartedAt])

  const submitMutation = useMutation({
    mutationFn: () => submitInvoiceToKsef(tenantId, id),
    onSuccess: (updated) => {
      queryClient.setQueryData(['invoices', 'aggregate', 'ksef-submit', { tenantId, id }], updated)
      setPollingStartedAt(Date.now())
    },
    onError: (error) => {
      if (error instanceof InvoiceValidationError) {
        setServerValidation({ stage: error.stage, messages: error.messages })
        setSubmitError(null)
        return
      }
      setServerValidation(null)
      setSubmitError(error instanceof Error ? error.message : 'Nie udało się wysłać faktury do KSeF.')
    },
  })

  const invoice = invoiceQuery.data ?? null
  const isPolling = pollingStartedAt !== null

  return (
    <section>
      <Link className="back-link" to={`${base}${tenantQuery}`}>
        ← Powrót do faktury
      </Link>

      <header className="page-header">
        <h1>Wysyłka do KSeF</h1>
      </header>

      <AsyncStateView
        isLoading={invoiceQuery.isLoading}
        error={invoiceQuery.error}
        isEmpty={!invoiceQuery.isLoading && !invoiceQuery.error && !invoice}
        emptyTitle="Nie znaleziono faktury"
        emptyMessage="Faktura nie istnieje lub brak dostępu."
        onRetry={() => void invoiceQuery.refetch()}
      >
        {invoice ? (
          <div className="invoice-ksef-submit" data-testid="ksef-submit-view">
            <p className="invoice-ksef-submit__doc-number">{invoice.documentNumber ?? invoice.id}</p>

            {invoice.ksefSubmissionState === 'Accepted' ? (
              <div className="invoice-ksef-submit__accepted" data-testid="ksef-accepted">
                <p>Faktura zaakceptowana przez KSeF.</p>
                {invoice.ksefDocumentNumber ? (
                  <p data-testid="ksef-document-number">Nr KSeF: {invoice.ksefDocumentNumber}</p>
                ) : null}
                {invoice.ksefReferenceNumber ? (
                  <p>Nr referencyjny: {invoice.ksefReferenceNumber}</p>
                ) : null}
                <div className="invoice-ksef-submit__actions">
                  <Link
                    to={`${base}/print${tenantQuery}`}
                    className="ui-button ui-button--secondary"
                    data-testid="print-button"
                  >
                    Drukuj
                  </Link>
                  <Link to={`${base}${tenantQuery}`} className="ui-button ui-button--primary">
                    Wróć do faktury
                  </Link>
                </div>
              </div>
            ) : invoice.ksefSubmissionState === 'Rejected' ? (
              <div className="invoice-ksef-submit__rejected" data-testid="ksef-rejected">
                <p>Faktura odrzucona przez KSeF.</p>
                {invoice.ksefRejectionReason ? (
                  <p data-testid="ksef-rejection-reason">{invoice.ksefRejectionReason}</p>
                ) : null}
                <div className="invoice-ksef-submit__actions">
                  <Link
                    to={`${base}/approve${tenantQuery}`}
                    className="ui-button ui-button--secondary"
                    data-testid="retry-approve-button"
                  >
                    Popraw i zatwierdź ponownie
                  </Link>
                  <Link
                    to={`${base}/corrections/new${tenantQuery}`}
                    className="ui-button ui-button--secondary"
                    data-testid="create-correction-button"
                  >
                    Utwórz korektę
                  </Link>
                </div>
              </div>
            ) : isPolling ? (
              <div className="invoice-ksef-submit__polling" data-testid="ksef-polling">
                {pollingTimedOut ? (
                  <>
                    <p>Przekroczono limit czasu oczekiwania. Sprawdź status ręcznie.</p>
                    <button
                      type="button"
                      className="ui-button ui-button--secondary"
                      data-testid="manual-refresh-button"
                      onClick={() => void invoiceQuery.refetch()}
                    >
                      Odśwież
                    </button>
                  </>
                ) : (
                  <p>Oczekiwanie na odpowiedź KSeF...</p>
                )}
              </div>
            ) : (
              <div className="invoice-ksef-submit__form">
                {serverValidation ? (
                  <ValidationMessageList stage={serverValidation.stage} messages={serverValidation.messages} />
                ) : null}
                {submitError ? <p role="alert">{submitError}</p> : null}

                <div className="invoice-ksef-submit__actions">
                  <button
                    type="button"
                    className="ui-button ui-button--primary"
                    data-testid="submit-to-ksef-button"
                    disabled={submitMutation.isPending}
                    onClick={() => void submitMutation.mutate()}
                  >
                    {submitMutation.isPending ? 'Wysyłanie...' : 'Wyślij do KSeF'}
                  </button>
                </div>
              </div>
            )}
          </div>
        ) : null}
      </AsyncStateView>
    </section>
  )
}
