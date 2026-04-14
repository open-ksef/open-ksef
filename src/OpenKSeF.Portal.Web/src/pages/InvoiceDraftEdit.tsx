import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery } from '@tanstack/react-query'
import { startTransition, useEffect, useMemo, useState, type ReactElement } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import toast from 'react-hot-toast'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { z } from 'zod'

import { InvoiceValidationError, getAggregateInvoice, updateInvoiceDraft } from '@/api/invoicesAggregateApi'
import { updateInvoiceDraftRequestSchema, type InvoiceReadDto, type ValidationEnvelope } from '@/api/schemas/invoice'
import { AsyncStateView } from '@/components/AsyncStateView'
import { InvoiceLineEditor, type InvoiceLineFormValue } from '@/components/invoices/InvoiceLineEditor'
import { TotalsSummaryCard } from '@/components/invoices/TotalsSummaryCard'
import { ValidationMessageList } from '@/components/invoices/ValidationMessageList'

const editDraftFormSchema = z.object({
  issueDate: z.string(),
  saleDate: z.string(),
  dueDate: z.string(),
  documentNumber: z.string(),
  externalReference: z.string(),
  paymentMethod: z.string(),
  publicNotes: z.string(),
  internalNotes: z.string(),
})

type EditDraftFormValues = z.infer<typeof editDraftFormSchema>

export function InvoiceDraftEditPage(): ReactElement {
  const navigate = useNavigate()
  const { id = '' } = useParams()
  const [searchParams] = useSearchParams()
  const [lines, setLines] = useState<InvoiceLineFormValue[]>([])
  const [serverValidation, setServerValidation] = useState<ValidationEnvelope | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)

  const tenantId = searchParams.get('tenantId') ?? ''

  const invoiceQuery = useQuery({
    queryKey: ['invoices', 'aggregate', 'edit', { tenantId, id }],
    queryFn: () => getAggregateInvoice(tenantId, id),
    enabled: Boolean(tenantId && id),
    retry: false,
  })
  const invoice = invoiceQuery.data ?? null

  const form = useForm<EditDraftFormValues>({
    resolver: zodResolver(editDraftFormSchema),
    defaultValues: {
      issueDate: '',
      saleDate: '',
      dueDate: '',
      documentNumber: '',
      externalReference: '',
      paymentMethod: '',
      publicNotes: '',
      internalNotes: '',
    },
  })

  const issueDate = useWatch({ control: form.control, name: 'issueDate' }) ?? ''
  const saleDate = useWatch({ control: form.control, name: 'saleDate' }) ?? ''
  const dueDate = useWatch({ control: form.control, name: 'dueDate' }) ?? ''
  const documentNumber = useWatch({ control: form.control, name: 'documentNumber' }) ?? ''
  const externalReference = useWatch({ control: form.control, name: 'externalReference' }) ?? ''
  const paymentMethod = useWatch({ control: form.control, name: 'paymentMethod' }) ?? ''
  const publicNotes = useWatch({ control: form.control, name: 'publicNotes' }) ?? ''
  const internalNotes = useWatch({ control: form.control, name: 'internalNotes' }) ?? ''

  useEffect(() => {
    if (!invoice) {
      return
    }

    if (invoice.status !== 'Draft') {
      toast.error('Faktura nie jest w stanie roboczym.')
      navigate(detailPath(invoice.id, tenantId), { replace: true })
      return
    }

    startTransition(() => {
      setLines(invoice.lines.map(mapReadLineToFormLine))
    })
    form.reset({
      issueDate: toDateInput(invoice.issueDate),
      saleDate: toDateInput(invoice.saleDate),
      dueDate: toDateInput(invoice.dueDate),
      documentNumber: invoice.documentNumber ?? '',
      externalReference: invoice.externalReference ?? '',
      paymentMethod: invoice.paymentMethod ?? '',
      publicNotes: invoice.publicNotes ?? '',
      internalNotes: invoice.internalNotes ?? '',
    })
  }, [form, invoice, navigate, tenantId])

  const totals = useMemo(
    () => calculateTotals(lines, invoice?.currency ?? 'PLN'),
    [invoice?.currency, lines],
  )

  const mutation = useMutation({
    mutationFn: ({
      invoiceId,
      request,
    }: {
      invoiceId: string
      request: z.input<typeof updateInvoiceDraftRequestSchema>
    }) => updateInvoiceDraft(tenantId, invoiceId, request),
    onSuccess: (invoice) => navigate(detailPath(invoice.id, tenantId)),
    onError: (error) => {
      if (error instanceof InvoiceValidationError) {
        setServerValidation({ stage: error.stage, messages: error.messages })
        setSubmitError(null)
        return
      }

      setServerValidation(null)
      setSubmitError(error instanceof Error ? error.message : 'Nie udalo sie zapisac zmian.')
    },
  })

  const onSubmit = form.handleSubmit(async (values) => {
    if (!invoice) {
      return
    }

    const payload = buildPatchPayload(invoice, values, lines)
    setServerValidation(null)
    setSubmitError(null)

    try {
      await mutation.mutateAsync({ invoiceId: invoice.id, request: payload })
    } catch {
      // handled by mutation state
    }
  })

  return (
    <section className="ide-page">
      <header className="ide-page-header">
        <Link className="ide-back-link" to={detailPath(id, tenantId)}>
          ← Powrót
        </Link>
        <h1 className="ide-page-title">Edycja szkicu faktury</h1>
      </header>

      <AsyncStateView
        isLoading={invoiceQuery.isLoading}
        error={invoiceQuery.error}
        isEmpty={!invoiceQuery.isLoading && !invoiceQuery.error && !invoice}
        emptyTitle="Nie znaleziono szkicu"
        emptyMessage="Nie znaleziono dokumentu do edycji."
        onRetry={() => void invoiceQuery.refetch()}
      >
        {invoice ? (
          <form className="ide-form" onSubmit={(event) => void onSubmit(event)}>
            {/* ── Main content column ────────────────────────── */}
            <div className="ide-main">

              {/* Parties */}
              <section className="ide-card ide-parties">
                <div className="ide-party">
                  <span className="ide-party__role">Sprzedawca</span>
                  <span className="ide-party__name">{invoice.seller.name}</span>
                  <span className="ide-party__nip">NIP: {invoice.seller.nip ?? '—'}</span>
                </div>
                <div className="ide-party-divider" aria-hidden="true" />
                <div className="ide-party">
                  <span className="ide-party__role">Nabywca</span>
                  <span className="ide-party__name">{invoice.buyer.name}</span>
                  <span className="ide-party__nip">NIP: {invoice.buyer.nip ?? '—'}</span>
                </div>
              </section>

              {/* Dates + document details */}
              <section className="ide-card">
                <h2 className="ide-section-title">Daty i szczegóły dokumentu</h2>
                <div className="ide-meta-grid">
                  <div className="ide-field">
                    <label className="ide-label" htmlFor="edit-issue-date">Data wystawienia</label>
                    <input
                      id="edit-issue-date"
                      type="date"
                      value={issueDate}
                      onChange={(e) => form.setValue('issueDate', e.target.value, { shouldDirty: true })}
                    />
                  </div>
                  <div className="ide-field">
                    <label className="ide-label" htmlFor="edit-sale-date">Data sprzedaży</label>
                    <input
                      id="edit-sale-date"
                      type="date"
                      value={saleDate}
                      onChange={(e) => form.setValue('saleDate', e.target.value, { shouldDirty: true })}
                    />
                  </div>
                  <div className="ide-field">
                    <label className="ide-label" htmlFor="edit-due-date">Termin płatności</label>
                    <input
                      id="edit-due-date"
                      type="date"
                      value={dueDate}
                      onChange={(e) => form.setValue('dueDate', e.target.value, { shouldDirty: true })}
                    />
                  </div>
                  <div className="ide-field">
                    <label className="ide-label" htmlFor="edit-document-number">Numer dokumentu</label>
                    <input
                      id="edit-document-number"
                      data-testid="edit-document-number"
                      type="text"
                      value={documentNumber}
                      onChange={(e) => form.setValue('documentNumber', e.target.value, { shouldDirty: true })}
                      onInput={(e) => form.setValue('documentNumber', e.currentTarget.value, { shouldDirty: true })}
                    />
                  </div>
                  <div className="ide-field">
                    <label className="ide-label" htmlFor="edit-payment-method">Forma płatności</label>
                    <input
                      id="edit-payment-method"
                      data-testid="edit-payment-method"
                      type="text"
                      value={paymentMethod}
                      onChange={(e) => form.setValue('paymentMethod', e.target.value, { shouldDirty: true })}
                      onInput={(e) => form.setValue('paymentMethod', e.currentTarget.value, { shouldDirty: true })}
                    />
                  </div>
                  <div className="ide-field">
                    <label className="ide-label" htmlFor="edit-external-reference">Referencja zewnętrzna</label>
                    <input
                      id="edit-external-reference"
                      data-testid="edit-external-reference"
                      type="text"
                      value={externalReference}
                      onChange={(e) => form.setValue('externalReference', e.target.value, { shouldDirty: true })}
                      onInput={(e) => form.setValue('externalReference', e.currentTarget.value, { shouldDirty: true })}
                    />
                  </div>
                </div>
              </section>

              {/* Line items */}
              <section className="ide-card">
                <h2 className="ide-section-title">Pozycje</h2>
                <InvoiceLineEditor
                  value={lines}
                  onChange={setLines}
                  mode="create"
                  pricingMode="Net"
                  allowReorder
                />
              </section>

              {/* Notes */}
              <section className="ide-card ide-notes-grid">
                <div className="ide-field">
                  <label className="ide-label" htmlFor="edit-public-notes">Uwagi publiczne</label>
                  <textarea
                    id="edit-public-notes"
                    data-testid="edit-public-notes"
                    rows={3}
                    value={publicNotes}
                    onChange={(e) => form.setValue('publicNotes', e.target.value, { shouldDirty: true })}
                    onInput={(e) => form.setValue('publicNotes', e.currentTarget.value, { shouldDirty: true })}
                  />
                </div>
                <div className="ide-field">
                  <label className="ide-label" htmlFor="edit-internal-notes">Uwagi wewnętrzne</label>
                  <textarea
                    id="edit-internal-notes"
                    data-testid="edit-internal-notes"
                    rows={3}
                    value={internalNotes}
                    onChange={(e) => form.setValue('internalNotes', e.target.value, { shouldDirty: true })}
                    onInput={(e) => form.setValue('internalNotes', e.currentTarget.value, { shouldDirty: true })}
                  />
                </div>
              </section>

            </div>

            {/* ── Sticky sidebar ─────────────────────────────── */}
            <aside className="ide-sidebar">
              <TotalsSummaryCard
                net={totals.net}
                vat={totals.vat}
                gross={totals.gross}
                currency={invoice.currency}
              />

              {serverValidation ? (
                <div className="ide-validation">
                  <ValidationMessageList stage={serverValidation.stage} messages={serverValidation.messages} />
                  {serverValidation.messages.some((m) => m.code === 'INV-VAL-101') ? (
                    <div role="alertdialog" aria-label="Blad zmiany stanu" className="ide-validation__alert">
                      Faktura została w międzyczasie zatwierdzona
                    </div>
                  ) : null}
                </div>
              ) : null}

              {submitError ? <p role="alert" className="ide-submit-error">{submitError}</p> : null}

              <div className="ide-sidebar-actions">
                <button
                  className="ui-button ui-button--primary ui-button--lg"
                  data-testid="edit-submit-button"
                  type="submit"
                  disabled={mutation.isPending}
                >
                  {mutation.isPending ? 'Zapisywanie…' : '✓ Zapisz zmiany'}
                </button>
                <Link
                  className="ui-button ui-button--secondary"
                  data-testid="edit-cancel-button"
                  to={detailPath(invoice.id, tenantId)}
                >
                  Anuluj
                </Link>
              </div>
            </aside>
          </form>
        ) : null}
      </AsyncStateView>
    </section>
  )
}

function detailPath(invoiceId: string, tenantId: string): string {
  return tenantId
    ? `/invoices/aggregate/${encodeURIComponent(invoiceId)}?tenantId=${encodeURIComponent(tenantId)}`
    : `/invoices/aggregate/${encodeURIComponent(invoiceId)}`
}

function toDateInput(value: string | null): string {
  return value ? value.slice(0, 10) : ''
}

function mapReadLineToFormLine(line: InvoiceReadDto['lines'][number]): InvoiceLineFormValue {
  return {
    description: line.description,
    quantity: line.quantity,
    unitOfMeasure: line.unitOfMeasure ?? '',
    pricingMode: line.pricingMode,
    unitPrice: line.unitPrice.amount,
    discountPercent: line.discountPercent,
    vatRate: line.vatRate,
  }
}

function buildPatchPayload(
  initialInvoice: InvoiceReadDto,
  values: EditDraftFormValues,
  lines: InvoiceLineFormValue[],
): z.input<typeof updateInvoiceDraftRequestSchema> {
  const payload: z.input<typeof updateInvoiceDraftRequestSchema> = {}

  if (toDateInput(initialInvoice.issueDate) !== values.issueDate) {
    payload.issueDate = values.issueDate || undefined
  }

  if (toDateInput(initialInvoice.saleDate) !== values.saleDate) {
    payload.saleDate = values.saleDate || undefined
  }

  if (toDateInput(initialInvoice.dueDate) !== values.dueDate) {
    payload.dueDate = values.dueDate || undefined
  }

  if ((initialInvoice.documentNumber ?? '') !== values.documentNumber) {
    payload.documentNumber = values.documentNumber || undefined
  }

  if ((initialInvoice.externalReference ?? '') !== values.externalReference) {
    payload.externalReference = values.externalReference || undefined
  }

  if ((initialInvoice.paymentMethod ?? '') !== values.paymentMethod) {
    payload.paymentMethod = values.paymentMethod || undefined
  }

  if ((initialInvoice.publicNotes ?? '') !== values.publicNotes) {
    payload.publicNotes = values.publicNotes || undefined
  }

  if ((initialInvoice.internalNotes ?? '') !== values.internalNotes) {
    payload.internalNotes = values.internalNotes || undefined
  }

  const initialLines = initialInvoice.lines.map((line) => ({
    lineNumber: line.lineNumber,
    description: line.description,
    quantity: line.quantity,
    unitOfMeasure: line.unitOfMeasure ?? undefined,
    pricingMode: line.pricingMode,
    unitPrice: line.unitPrice.amount,
    discountPercent: line.discountPercent ?? undefined,
    vatRate: line.vatRate,
  }))

  const nextLines = lines.map((line, index) => ({
    lineNumber: index + 1,
    description: line.description,
    quantity: line.quantity,
    unitOfMeasure: line.unitOfMeasure || undefined,
    pricingMode: line.pricingMode,
    unitPrice: line.unitPrice,
    discountPercent: line.discountPercent ?? undefined,
    vatRate: line.vatRate,
  }))

  if (JSON.stringify(initialLines) !== JSON.stringify(nextLines)) {
    payload.lines = nextLines
  }

  return payload
}

function calculateTotals(lines: InvoiceLineFormValue[], currency: string) {
  const totals = lines.reduce(
    (accumulator, line) => {
      const lineNet = roundMoney(line.quantity * line.unitPrice)
      const vatRate = parseVatRate(line.vatRate)
      const lineVat = roundMoney(lineNet * vatRate)
      return {
        netAmount: roundMoney(accumulator.netAmount + lineNet),
        vatAmount: roundMoney(accumulator.vatAmount + lineVat),
        grossAmount: roundMoney(accumulator.grossAmount + lineNet + lineVat),
      }
    },
    { netAmount: 0, vatAmount: 0, grossAmount: 0 },
  )

  return {
    net: { amount: totals.netAmount, currency },
    vat: { amount: totals.vatAmount, currency },
    gross: { amount: totals.grossAmount, currency },
  }
}

function parseVatRate(value: string): number {
  if (value.endsWith('%')) {
    const parsed = Number.parseFloat(value.slice(0, -1))
    return Number.isFinite(parsed) ? parsed / 100 : 0
  }

  return 0
}

function roundMoney(value: number): number {
  return Math.round(value * 100) / 100
}
