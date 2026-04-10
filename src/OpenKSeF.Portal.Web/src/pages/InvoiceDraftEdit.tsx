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
import { IssueDatesFieldset } from '@/components/invoices/IssueDatesFieldset'
import { PartyCard } from '@/components/invoices/PartyCard'
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
    <section>
      <Link className="back-link" to={detailPath(id, tenantId)}>
        ← Powrot do szczegolow
      </Link>

      <header className="page-header">
        <h1>Edycja szkicu faktury</h1>
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
          <form className="invoice-draft-edit" onSubmit={(event) => void onSubmit(event)}>
            <div className="invoice-draft-edit__parties">
              <PartyCard party={invoice.seller} title="Sprzedawca" />
              <PartyCard party={invoice.buyer} title="Nabywca" />
            </div>

            <section className="invoice-draft-edit__section">
              <IssueDatesFieldset
                value={{ issueDate, saleDate, dueDate }}
                onChange={(next) => {
                  form.setValue('issueDate', next.issueDate, { shouldDirty: true })
                  form.setValue('saleDate', next.saleDate ?? '', { shouldDirty: true })
                  form.setValue('dueDate', next.dueDate ?? '', { shouldDirty: true })
                }}
              />
            </section>

            <section className="invoice-draft-edit__section">
              <label htmlFor="edit-document-number">
                <span>Numer dokumentu</span>
                <input
                  id="edit-document-number"
                  data-testid="edit-document-number"
                  type="text"
                  value={documentNumber}
                  onChange={(event) => form.setValue('documentNumber', event.target.value, { shouldDirty: true })}
                  onInput={(event) => form.setValue('documentNumber', event.currentTarget.value, { shouldDirty: true })}
                />
              </label>

              <label htmlFor="edit-external-reference">
                <span>Referencja zewnetrzna</span>
                <input
                  id="edit-external-reference"
                  data-testid="edit-external-reference"
                  type="text"
                  value={externalReference}
                  onChange={(event) => form.setValue('externalReference', event.target.value, { shouldDirty: true })}
                  onInput={(event) => form.setValue('externalReference', event.currentTarget.value, { shouldDirty: true })}
                />
              </label>

              <label htmlFor="edit-payment-method">
                <span>Forma platnosci</span>
                <input
                  id="edit-payment-method"
                  data-testid="edit-payment-method"
                  type="text"
                  value={paymentMethod}
                  onChange={(event) => form.setValue('paymentMethod', event.target.value, { shouldDirty: true })}
                  onInput={(event) => form.setValue('paymentMethod', event.currentTarget.value, { shouldDirty: true })}
                />
              </label>
            </section>

            <section className="invoice-draft-edit__section">
              <label htmlFor="edit-public-notes">
                <span>Uwagi publiczne</span>
                <textarea
                  id="edit-public-notes"
                  data-testid="edit-public-notes"
                  value={publicNotes}
                  onChange={(event) => form.setValue('publicNotes', event.target.value, { shouldDirty: true })}
                  onInput={(event) => form.setValue('publicNotes', event.currentTarget.value, { shouldDirty: true })}
                />
              </label>

              <label htmlFor="edit-internal-notes">
                <span>Uwagi wewnetrzne</span>
                <textarea
                  id="edit-internal-notes"
                  data-testid="edit-internal-notes"
                  value={internalNotes}
                  onChange={(event) => form.setValue('internalNotes', event.target.value, { shouldDirty: true })}
                  onInput={(event) => form.setValue('internalNotes', event.currentTarget.value, { shouldDirty: true })}
                />
              </label>
            </section>

            <section className="invoice-draft-edit__section">
              <h2>Pozycje</h2>
              <InvoiceLineEditor
                value={lines}
                onChange={setLines}
                mode="create"
                pricingMode="Net"
                allowReorder
              />
            </section>

            <TotalsSummaryCard
              net={totals.net}
              vat={totals.vat}
              gross={totals.gross}
              currency={invoice.currency}
            />

            {serverValidation ? (
              <>
                <ValidationMessageList stage={serverValidation.stage} messages={serverValidation.messages} />
                {serverValidation.messages.some((message) => message.code === 'INV-VAL-101') ? (
                  <div role="alertdialog" aria-label="Blad zmiany stanu">
                    Faktura została w międzyczasie zatwierdzona
                  </div>
                ) : null}
              </>
            ) : null}

            {submitError ? <p role="alert">{submitError}</p> : null}

            <div className="invoice-draft-edit__actions">
              <Link
                className="ui-button ui-button--secondary"
                data-testid="edit-cancel-button"
                to={detailPath(invoice.id, tenantId)}
              >
                Anuluj
              </Link>
              <button
                className="ui-button ui-button--primary"
                data-testid="edit-submit-button"
                type="submit"
                disabled={mutation.isPending}
              >
                {mutation.isPending ? 'Zapisywanie...' : 'Zapisz zmiany'}
              </button>
            </div>
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
