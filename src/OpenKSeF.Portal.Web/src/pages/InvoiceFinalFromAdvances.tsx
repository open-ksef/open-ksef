import { useMutation, useQuery } from '@tanstack/react-query'
import { useMemo, useState, type ReactElement } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'

import {
  InvoiceValidationError,
  createFinalInvoiceFromAdvances,
  listAggregateInvoices,
} from '@/api/invoicesAggregateApi'
import { createFinalInvoiceFromAdvancesRequestSchema, type ValidationEnvelope } from '@/api/schemas/invoice'
import { listTenants } from '@/api/endpoints/tenants'
import { AsyncStateView } from '@/components/AsyncStateView'
import { AdvanceAllocationPicker, type AdvanceOption } from '@/components/invoices/AdvanceAllocationPicker'
import { ValidationMessageList } from '@/components/invoices/ValidationMessageList'

export function InvoiceFinalFromAdvancesPage(): ReactElement {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const tenantIdFromUrl = searchParams.get('tenantId') ?? ''

  const tenantsQuery = useQuery({
    queryKey: ['tenants', 'final-from-advances'],
    queryFn: () => listTenants(),
  })

  const effectiveTenantId = tenantIdFromUrl || tenantsQuery.data?.[0]?.id || ''

  const [selectedBuyerKey, setSelectedBuyerKey] = useState<string>('')
  const [selectedAdvanceIds, setSelectedAdvanceIds] = useState<string[]>([])
  const [issueDate, setIssueDate] = useState('')
  const [zodError, setZodError] = useState<string | null>(null)
  const [serverValidation, setServerValidation] = useState<ValidationEnvelope | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)

  const advancesQuery = useQuery({
    queryKey: ['invoices', 'aggregate', 'advances-for-final', { tenantId: effectiveTenantId }],
    queryFn: () =>
      listAggregateInvoices(effectiveTenantId, {
        kind: ['AdvanceInvoice'],
        status: ['Approved', 'AcceptedByKsef'],
        pageSize: 200,
      }),
    enabled: Boolean(effectiveTenantId),
    retry: false,
  })

  const allAdvances = advancesQuery.data?.items ?? []

  const buyers = useMemo(() => {
    const map = new Map<string, { name: string; nip: string | null }>()
    for (const inv of allAdvances) {
      const key = inv.buyer.nip ?? inv.buyer.name
      if (!map.has(key)) {
        map.set(key, { name: inv.buyer.name, nip: inv.buyer.nip })
      }
    }

    return [...map.entries()].map(([key, buyer]) => ({ key, ...buyer }))
  }, [allAdvances])

  const buyerAdvances: AdvanceOption[] = useMemo(() => {
    if (!selectedBuyerKey) return []
    return allAdvances
      .filter((inv) => (inv.buyer.nip ?? inv.buyer.name) === selectedBuyerKey)
      .map((inv) => ({
        id: inv.id,
        documentNumber: inv.documentNumber ?? inv.id,
        grossAmount: inv.totalGross,
      }))
  }, [allAdvances, selectedBuyerKey])

  const createMutation = useMutation({
    mutationFn: (data: { issueDate: string; advances: { advanceInvoiceId: string; advanceDocumentNumber: string; settledAmount: number }[] }) =>
      createFinalInvoiceFromAdvances(effectiveTenantId, data),
    onSuccess: (final) => {
      const detailSearch = effectiveTenantId ? `?tenantId=${encodeURIComponent(effectiveTenantId)}` : ''
      navigate(`/invoices/aggregate/${encodeURIComponent(final.id)}${detailSearch}`)
    },
    onError: (error) => {
      if (error instanceof InvoiceValidationError) {
        setServerValidation({ stage: error.stage, messages: error.messages })
        setSubmitError(null)
        return
      }

      setServerValidation(null)
      setSubmitError(error instanceof Error ? error.message : 'Nie udało się utworzyć faktury finalnej.')
    },
  })

  function handleSubmit(): void {
    setZodError(null)
    setServerValidation(null)
    setSubmitError(null)

    const selectedDetails = buyerAdvances.filter((a) => selectedAdvanceIds.includes(a.id))

    if (selectedDetails.length === 0) {
      setZodError('Zaznacz co najmniej jedną zaliczkę.')
      return
    }

    const result = createFinalInvoiceFromAdvancesRequestSchema.safeParse({
      issueDate,
      advances: selectedDetails.map((a) => ({
        advanceInvoiceId: a.id,
        advanceDocumentNumber: a.documentNumber,
        settledAmount: a.grossAmount.amount,
      })),
    })

    if (!result.success) {
      setZodError(result.error.issues[0]?.message ?? 'Nieprawidłowe dane formularza.')
      return
    }

    void createMutation.mutate(result.data)
  }

  const isLoading = tenantsQuery.isLoading || advancesQuery.isLoading

  return (
    <section>
      <Link className="back-link" to="/invoices">
        ← Powrót do faktur
      </Link>

      <header className="page-header">
        <h1>Tworzenie faktury finalnej z zaliczek</h1>
      </header>

      <AsyncStateView
        isLoading={isLoading}
        error={advancesQuery.error}
        isEmpty={!isLoading && !advancesQuery.error && allAdvances.length === 0}
        emptyTitle="Brak zaliczek"
        emptyMessage="Nie znaleziono zatwierdzonych faktur zaliczkowych."
        onRetry={() => void advancesQuery.refetch()}
      >
        <div className="invoice-final-from-advances" data-testid="final-advances-form">
          <section className="invoice-final-from-advances__buyer">
            <h2>Nabywca</h2>
            <label htmlFor="final-buyer-select">
              Wybierz nabywcę
              <select
                id="final-buyer-select"
                data-testid="buyer-select"
                value={selectedBuyerKey}
                onChange={(e) => {
                  setSelectedBuyerKey(e.target.value)
                  setSelectedAdvanceIds([])
                }}
              >
                <option value="">— wybierz —</option>
                {buyers.map((buyer) => (
                  <option key={buyer.key} value={buyer.key}>
                    {buyer.name} {buyer.nip ? `(${buyer.nip})` : ''}
                  </option>
                ))}
              </select>
            </label>
          </section>

          {selectedBuyerKey && buyerAdvances.length > 0 ? (
            <section className="invoice-final-from-advances__picker">
              <AdvanceAllocationPicker
                advances={buyerAdvances}
                selected={selectedAdvanceIds}
                onChange={setSelectedAdvanceIds}
              />
            </section>
          ) : null}

          <section className="invoice-final-from-advances__header">
            <h2>Dane faktury finalnej</h2>
            <label htmlFor="final-issue-date">
              Data wystawienia
              <input
                id="final-issue-date"
                type="date"
                value={issueDate}
                onChange={(e) => setIssueDate(e.target.value)}
              />
            </label>
          </section>

          {zodError ? <p role="alert">{zodError}</p> : null}

          {serverValidation ? (
            <ValidationMessageList stage={serverValidation.stage} messages={serverValidation.messages} />
          ) : null}

          {submitError ? <p role="alert">{submitError}</p> : null}

          <div className="invoice-final-from-advances__actions">
            <button
              type="button"
              className="ui-button ui-button--primary"
              data-testid="create-final-button"
              disabled={createMutation.isPending}
              onClick={handleSubmit}
            >
              {createMutation.isPending ? 'Tworzenie...' : 'Utwórz finalną'}
            </button>
          </div>
        </div>
      </AsyncStateView>
    </section>
  )
}
