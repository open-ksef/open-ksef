import { useMutation, useQuery } from '@tanstack/react-query'
import { useState, type ReactElement } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'

import { InvoiceValidationError, createCorrectionFromOriginal, getAggregateInvoice } from '@/api/invoicesAggregateApi'
import { correctionReasonKindSchema, type InvoiceReadDto, type ValidationEnvelope } from '@/api/schemas/invoice'
import { AsyncStateView } from '@/components/AsyncStateView'
import { InvoiceLineEditor, type InvoiceLineFormValue } from '@/components/invoices/InvoiceLineEditor'
import { PartyCard } from '@/components/invoices/PartyCard'
import { TotalsSummaryCard } from '@/components/invoices/TotalsSummaryCard'
import { ValidationMessageList } from '@/components/invoices/ValidationMessageList'

const CORRECTION_REASON_KINDS = correctionReasonKindSchema.options

export function InvoiceCorrectionCreatePage(): ReactElement {
  const { id = '' } = useParams()
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const tenantId = searchParams.get('tenantId') ?? ''

  const [issueDate, setIssueDate] = useState('')
  const [reasonKind, setReasonKind] = useState<string>('ValueChange')
  const [reasonDescription, setReasonDescription] = useState('')
  const [serverValidation, setServerValidation] = useState<ValidationEnvelope | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)

  const originalQuery = useQuery({
    queryKey: ['invoices', 'aggregate', 'correction-source', { tenantId, id }],
    queryFn: () => getAggregateInvoice(tenantId, id),
    enabled: Boolean(tenantId && id),
    retry: false,
  })

  const createMutation = useMutation({
    mutationFn: () =>
      createCorrectionFromOriginal(tenantId, id, {
        issueDate,
        reasonKind,
        reasonDescription,
      }),
    onSuccess: (correction) => {
      const detailSearch = tenantId ? `?tenantId=${encodeURIComponent(tenantId)}` : ''
      navigate(`/invoices/aggregate/${encodeURIComponent(correction.id)}${detailSearch}`)
    },
    onError: (error) => {
      if (error instanceof InvoiceValidationError) {
        setServerValidation({ stage: error.stage, messages: error.messages })
        setSubmitError(null)
        return
      }

      setServerValidation(null)
      setSubmitError(error instanceof Error ? error.message : 'Nie udało się utworzyć korekty.')
    },
  })

  const original = originalQuery.data ?? null

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
        <h1>Tworzenie korekty</h1>
      </header>

      <AsyncStateView
        isLoading={originalQuery.isLoading}
        error={originalQuery.error}
        isEmpty={!originalQuery.isLoading && !originalQuery.error && !original}
        emptyTitle="Nie znaleziono faktury"
        emptyMessage="Nie znaleziono dokumentu do korekty."
        onRetry={() => void originalQuery.refetch()}
      >
        {original ? (
          <div className="invoice-correction-create" data-testid="correction-create-form">
            <section className="invoice-correction-create__original">
              <h2>Oryginalna faktura</h2>
              <p className="invoice-correction-create__original-number">{original.documentNumber ?? '—'}</p>
              <div className="invoice-correction-create__parties">
                <PartyCard party={original.seller} title="Sprzedawca" />
                <PartyCard party={original.buyer} title="Nabywca" />
              </div>
              <TotalsSummaryCard
                net={original.totalNet}
                vat={original.totalVat}
                gross={original.totalGross}
                currency={original.currency}
              />
            </section>

            <section className="invoice-correction-create__lines">
              <h2>Pozycje (przed/po korekcie)</h2>
              <InvoiceLineEditor
                value={originalLinesToCorrectionFormValues(original)}
                onChange={() => {}}
                mode="correction"
                pricingMode="Net"
              />
            </section>

            <form
              className="invoice-correction-create__form"
              noValidate
              onSubmit={(event) => {
                event.preventDefault()
                setServerValidation(null)
                setSubmitError(null)
                void createMutation.mutate()
              }}
            >
              <section className="invoice-correction-create__header">
                <h2>Dane korekty</h2>

                <label htmlFor="correction-issue-date">
                  Data wystawienia korekty
                  <input
                    id="correction-issue-date"
                    type="date"
                    value={issueDate}
                    onChange={(e) => setIssueDate(e.target.value)}
                    required
                  />
                </label>

                <label htmlFor="correction-reason-kind">
                  Powód korekty
                  <select
                    id="correction-reason-kind"
                    data-testid="reason-kind-select"
                    value={reasonKind}
                    onChange={(e) => setReasonKind(e.target.value)}
                  >
                    {CORRECTION_REASON_KINDS.map((kind) => (
                      <option key={kind} value={kind}>
                        {reasonKindLabel(kind)}
                      </option>
                    ))}
                  </select>
                </label>

                <label htmlFor="correction-reason-description">
                  Opis powodu
                  <textarea
                    id="correction-reason-description"
                    value={reasonDescription}
                    onChange={(e) => setReasonDescription(e.target.value)}
                    required
                  />
                </label>
              </section>

              {serverValidation ? (
                <ValidationMessageList stage={serverValidation.stage} messages={serverValidation.messages} />
              ) : null}

              {submitError ? <p role="alert">{submitError}</p> : null}

              <div className="invoice-correction-create__actions">
                <button
                  type="submit"
                  className="ui-button ui-button--primary"
                  data-testid="create-correction-button"
                  disabled={createMutation.isPending}
                >
                  {createMutation.isPending ? 'Tworzenie...' : 'Utwórz korektę'}
                </button>
              </div>
            </form>
          </div>
        ) : null}
      </AsyncStateView>
    </section>
  )
}

function originalLinesToCorrectionFormValues(original: InvoiceReadDto): InvoiceLineFormValue[] {
  return original.lines.map((line) => {
    const lineValue: InvoiceLineFormValue = {
      description: line.description,
      quantity: line.quantity,
      unitOfMeasure: line.unitOfMeasure ?? '',
      pricingMode: line.pricingMode,
      unitPrice: line.unitPrice.amount,
      discountPercent: line.discountPercent,
      vatRate: line.vatRate,
    }

    return {
      ...lineValue,
      correctionBefore: lineValue,
    }
  })
}

function reasonKindLabel(kind: string): string {
  switch (kind) {
    case 'Formal':
      return 'Formalna'
    case 'ValueChange':
      return 'Zmiana wartości'
    case 'QuantityChange':
      return 'Zmiana ilości'
    case 'VatChange':
      return 'Zmiana VAT'
    case 'BuyerDataChange':
      return 'Zmiana danych nabywcy'
    case 'Other':
      return 'Inna'
    default:
      return kind
  }
}
