import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useEffect, useMemo, useState, type ReactElement } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { z } from 'zod'

import { InvoiceValidationError, createInvoiceDraft } from '@/api/invoicesAggregateApi'
import {
  createInvoiceRequestSchema,
  documentKindSchema,
  type BuyerKind,
  type CreateInvoiceRequest,
  type DocumentKind,
  type KsefSubmissionRequirement,
  type ValidationEnvelope,
} from '@/api/schemas/invoice'
import { listTenants } from '@/api/endpoints/tenants'
import { AsyncStateView } from '@/components/AsyncStateView'
import { BuyerSelector } from '@/components/invoices/BuyerSelector'
import { CurrencySelect } from '@/components/invoices/CurrencySelect'
import { DocumentNumberPreview } from '@/components/invoices/DocumentNumberPreview'
import { InvoiceLineEditor, type InvoiceLineFormValue } from '@/components/invoices/InvoiceLineEditor'
import { IssueDatesFieldset } from '@/components/invoices/IssueDatesFieldset'
import { KsefRequirementBanner } from '@/components/invoices/KsefRequirementBanner'
import { TotalsSummaryCard } from '@/components/invoices/TotalsSummaryCard'
import { ValidationMessageList } from '@/components/invoices/ValidationMessageList'

const createInvoiceFormSchema = createInvoiceRequestSchema.safeExtend({
  buyerNip: z.union([createInvoiceRequestSchema.shape.buyerNip, z.literal('')]),
  documentNumber: z.union([createInvoiceRequestSchema.shape.documentNumber, z.literal('')]),
  externalReference: z.union([createInvoiceRequestSchema.shape.externalReference, z.literal('')]),
})

type CreateInvoiceFormValues = z.input<typeof createInvoiceFormSchema>

const defaultValues: CreateInvoiceFormValues = {
  kind: 'VatInvoice',
  sellerName: '',
  sellerNip: '',
  buyerName: '',
  buyerKind: 'Business',
  buyerNip: '',
  currency: 'PLN',
  issueDate: formatDateInput(new Date()),
  ksefSubmissionRequirement: 'Optional',
  documentNumber: '',
  externalReference: '',
}

export function InvoiceDraftCreatePage(): ReactElement {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const [lines, setLines] = useState<InvoiceLineFormValue[]>([])
  const [lineError, setLineError] = useState<string | null>(null)
  const [serverValidation, setServerValidation] = useState<ValidationEnvelope | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)

  const tenantIdFromUrl = searchParams.get('tenantId') ?? ''

  const tenantsQuery = useQuery({
    queryKey: ['tenants', 'invoice-draft-create'],
    queryFn: () => listTenants(),
    retry: false,
  })

  const effectiveTenantId = tenantIdFromUrl || tenantsQuery.data?.[0]?.id || ''
  const selectedTenant = tenantsQuery.data?.find((tenant) => tenant.id === effectiveTenantId) ?? tenantsQuery.data?.[0] ?? null

  const form = useForm<CreateInvoiceFormValues>({
    resolver: zodResolver(createInvoiceFormSchema),
    defaultValues,
  })

  const kind = useWatch({ control: form.control, name: 'kind' }) ?? defaultValues.kind
  const sellerName = useWatch({ control: form.control, name: 'sellerName' }) ?? defaultValues.sellerName
  const sellerNip = useWatch({ control: form.control, name: 'sellerNip' }) ?? defaultValues.sellerNip
  const buyerName = useWatch({ control: form.control, name: 'buyerName' }) ?? defaultValues.buyerName
  const buyerKind = useWatch({ control: form.control, name: 'buyerKind' }) ?? defaultValues.buyerKind
  const buyerNip = useWatch({ control: form.control, name: 'buyerNip' }) ?? defaultValues.buyerNip
  const issueDate = useWatch({ control: form.control, name: 'issueDate' }) ?? defaultValues.issueDate
  const currency = useWatch({ control: form.control, name: 'currency' }) ?? defaultValues.currency
  const externalReference = (useWatch({ control: form.control, name: 'externalReference' }) ?? defaultValues.externalReference) as string

  const derivedRequirement = useMemo<KsefSubmissionRequirement>(
    () => deriveKsefRequirement(kind, buyerKind, buyerNip),
    [buyerKind, buyerNip, kind],
  )

  useEffect(() => {
    form.setValue('ksefSubmissionRequirement', derivedRequirement, { shouldValidate: true, shouldDirty: false })
  }, [derivedRequirement, form])

  useEffect(() => {
    if (!selectedTenant) {
      return
    }

    form.setValue('sellerName', selectedTenant.displayName || selectedTenant.nip, { shouldDirty: false, shouldValidate: true })
    form.setValue('sellerNip', selectedTenant.nip, { shouldDirty: false, shouldValidate: true })
  }, [form, selectedTenant])

  const totals = useMemo(() => calculateTotals(lines, currency), [currency, lines])

  const createDraftMutation = useMutation({
    mutationFn: ({ tenantId, request }: { tenantId: string; request: CreateInvoiceRequest }) => createInvoiceDraft(tenantId, request),
    onSuccess: (invoice) => {
      const detailSearch = effectiveTenantId ? `?tenantId=${encodeURIComponent(effectiveTenantId)}` : ''
      navigate(`/invoices/aggregate/${encodeURIComponent(invoice.id)}${detailSearch}`)
    },
    onError: (error) => {
      if (error instanceof InvoiceValidationError) {
        setServerValidation({
          stage: error.stage,
          messages: error.messages,
        })
        setSubmitError(null)
        return
      }

      setServerValidation(null)
      setSubmitError(error instanceof Error ? error.message : 'Nie udało się zapisać szkicu faktury.')
    },
  })

  const onSubmit = form.handleSubmit(async (values) => {
    if (lines.length === 0) {
      setLineError('Dodaj przynajmniej jedną pozycję.')
      return
    }

    setLineError(null)
    setServerValidation(null)
    setSubmitError(null)

    try {
      await createDraftMutation.mutateAsync({
        tenantId: effectiveTenantId,
        request: {
          kind: values.kind,
          sellerName: values.sellerName.trim(),
          sellerNip: values.sellerNip.trim(),
          buyerName: values.buyerName.trim(),
          buyerKind: values.buyerKind,
          buyerNip: values.buyerNip?.trim() || undefined,
          currency: values.currency,
          issueDate: values.issueDate,
          ksefSubmissionRequirement: derivedRequirement,
          documentNumber: values.documentNumber?.trim() || undefined,
          externalReference: values.externalReference?.trim() || undefined,
        },
      })
    } catch {
      // Mutation state already drives the validation and generic error banners.
    }
  })

  return (
    <section>
      <Link
        className="back-link"
        to={effectiveTenantId ? `/invoices?tenantId=${encodeURIComponent(effectiveTenantId)}` : '/invoices'}
      >
        ← Powrót do faktur
      </Link>

      <header className="page-header">
        <h1>Nowa faktura</h1>
      </header>

      <AsyncStateView
        isLoading={tenantsQuery.isLoading}
        error={tenantsQuery.error}
        isEmpty={!tenantsQuery.isLoading && !tenantsQuery.error && !selectedTenant}
        emptyTitle="Brak firm"
        emptyMessage="Dodaj firmę, aby wystawić pierwszą fakturę."
        onRetry={() => void tenantsQuery.refetch()}
      >
        <form className="idc-page" onSubmit={(event) => void onSubmit(event)}>

          {/* HEADER CARD: doc type + number | date + currency */}
          <div className="ide-card idc-header">
            <div className="idc-header__left">
              <div className="ide-field">
                <label className="ide-label" htmlFor="invoice-kind">Rodzaj dokumentu</label>
                <select
                  id="invoice-kind"
                  data-testid="kind-select"
                  value={kind}
                  onChange={(event) => form.setValue('kind', event.target.value as DocumentKind, { shouldDirty: true, shouldValidate: true })}
                >
                  {documentKindSchema.options.map((option) => (
                    <option key={option} value={option}>
                      {documentKindLabel(option)}
                    </option>
                  ))}
                </select>
              </div>

              <DocumentNumberPreview
                policyResolved={buildDocumentNumberPreview(kind, issueDate)}
                externalReference={externalReference}
              />

              <div className="ide-field">
                <label className="ide-label" htmlFor="invoice-external-reference">Referencja zewnętrzna</label>
                <input
                  id="invoice-external-reference"
                  type="text"
                  {...form.register('externalReference')}
                  value={externalReference}
                  onChange={(event) => form.setValue('externalReference', event.target.value, { shouldDirty: true, shouldValidate: false })}
                  onInput={(event) => form.setValue('externalReference', event.currentTarget.value, { shouldDirty: true, shouldValidate: false })}
                />
              </div>
            </div>

            <div className="idc-header__right">
              <div className="ide-field">
                <IssueDatesFieldset
                  value={{ issueDate }}
                  onChange={(next) => {
                    form.setValue('issueDate', next.issueDate, { shouldDirty: true, shouldValidate: true })
                  }}
                  mode="compact"
                />
                <FieldError message={getIssueDateErrorMessage(form.formState.errors.issueDate)} />
              </div>

              <div className="ide-field">
                <CurrencySelect
                  value={currency}
                  onChange={(nextValue) => form.setValue('currency', nextValue, { shouldDirty: true, shouldValidate: true })}
                />
                <FieldError message={getCurrencyErrorMessage(form.formState.errors.currency)} />
              </div>
            </div>
          </div>

          {/* PARTIES CARD: seller | divider | buyer */}
          <div className="ide-card ide-parties idc-parties">
            <div className="ide-party">
              <span className="ide-party__role">Sprzedawca</span>
              <span className="ide-party__name">{sellerName || '—'}</span>
              <span className="ide-party__nip">NIP: {sellerNip || '—'}</span>
            </div>
            <div className="ide-party-divider" aria-hidden="true" />
            <div className="idc-buyer">
              <span className="ide-party__role">Nabywca</span>

              <div className="ide-field">
                <label className="ide-label" htmlFor="buyer-kind">Typ nabywcy</label>
                <select
                  id="buyer-kind"
                  data-testid="buyer-kind-select"
                  value={buyerKind}
                  onChange={(event) => form.setValue('buyerKind', event.target.value as BuyerKind, { shouldDirty: true, shouldValidate: true })}
                >
                  <option value="Business">Firma</option>
                  <option value="Consumer">Konsument</option>
                  <option value="Unknown">Nieustalony</option>
                </select>
              </div>

              <div className="ide-field">
                <label className="ide-label" htmlFor="buyer-name">Nazwa</label>
                <BuyerSelector
                  value={buyerName}
                  onChange={(val) => form.setValue('buyerName', val, { shouldDirty: true })}
                  onSelect={(suggestion) => {
                    form.setValue('buyerName', suggestion.name, { shouldDirty: true })
                    form.setValue('buyerNip', suggestion.nip ?? '', { shouldDirty: true })
                    form.setValue('buyerKind', suggestion.buyerKind, { shouldDirty: true, shouldValidate: true })
                  }}
                  tenantId={effectiveTenantId}
                />
                <FieldError message={getNameErrorMessage(form.formState.errors.buyerName)} />
              </div>

              <div className="ide-field">
                <label className="ide-label" htmlFor="buyer-nip">NIP</label>
                <input
                  id="buyer-nip"
                  data-testid="buyer-nip"
                  type="text"
                  inputMode="numeric"
                  {...form.register('buyerNip')}
                  value={buyerNip ?? ''}
                  onChange={(event) => form.setValue('buyerNip', event.target.value, { shouldDirty: true, shouldValidate: false })}
                  onInput={(event) => form.setValue('buyerNip', event.currentTarget.value, { shouldDirty: true, shouldValidate: false })}
                />
                <FieldError message={getBuyerNipErrorMessage(form.formState.errors.buyerNip, buyerKind, buyerNip ?? '')} />
              </div>
            </div>
          </div>

          <KsefRequirementBanner requirement={derivedRequirement} />

          {/* LINE ITEMS */}
          <section className="ide-card idc-lines">
            <p className="ide-section-title">Pozycje</p>
            <InvoiceLineEditor
              value={lines}
              onChange={(nextLines) => {
                setLines(nextLines)
                if (nextLines.length > 0) {
                  setLineError(null)
                }
              }}
              mode="create"
              pricingMode="Net"
            />
            <FieldError message={lineError} />
          </section>

          {/* FOOTER: errors left, totals + submit right */}
          <div className="idc-footer">
            <div className="idc-footer__messages">
              {serverValidation ? (
                <ValidationMessageList stage={serverValidation.stage} messages={serverValidation.messages} />
              ) : null}
              {submitError ? (
                <p role="alert">{submitError}</p>
              ) : null}
            </div>
            <div className="idc-footer__actions">
              <TotalsSummaryCard
                net={totals.net}
                vat={totals.vat}
                gross={totals.gross}
                currency={currency}
              />
              <button
                className="ui-button ui-button--primary"
                data-testid="submit-button"
                type="submit"
                disabled={createDraftMutation.isPending || !effectiveTenantId}
              >
                {createDraftMutation.isPending ? 'Zapisywanie...' : 'Zapisz'}
              </button>
            </div>
          </div>
        </form>
      </AsyncStateView>
    </section>
  )
}

function deriveKsefRequirement(
  kind: DocumentKind,
  buyerKind: BuyerKind,
  buyerNip: string | null | undefined,
): KsefSubmissionRequirement {
  if (kind === 'Proforma') {
    return 'NotApplicable'
  }

  if (buyerKind === 'Business' && Boolean(buyerNip?.trim())) {
    return 'Required'
  }

  return 'Optional'
}

function calculateTotals(lines: InvoiceLineFormValue[], currency: string) {
  const totals = lines.reduce(
    (accumulator, line) => {
      const quantity = Number.isFinite(line.quantity) ? line.quantity : 0
      const unitPrice = Number.isFinite(line.unitPrice) ? line.unitPrice : 0
      const lineNet = roundMoney(quantity * unitPrice)
      const vatRate = parseVatRate(line.vatRate)
      const lineVat = roundMoney(lineNet * vatRate)
      const lineGross = roundMoney(lineNet + lineVat)

      return {
        netAmount: roundMoney(accumulator.netAmount + lineNet),
        vatAmount: roundMoney(accumulator.vatAmount + lineVat),
        grossAmount: roundMoney(accumulator.grossAmount + lineGross),
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

function buildDocumentNumberPreview(kind: DocumentKind, issueDate: string): string {
  const [year = 'RRRR', month = 'MM'] = issueDate.split('-')
  const prefix = kind === 'Proforma' ? 'PRO' : kind === 'AdvanceInvoice' ? 'ZAL' : kind === 'FinalInvoice' ? 'FIN' : kind === 'CorrectionInvoice' ? 'KOR' : 'FV'
  return `${prefix}/${year}/${month}/001`
}

function documentKindLabel(kind: DocumentKind): string {
  switch (kind) {
    case 'VatInvoice':
      return 'Faktura VAT'
    case 'AdvanceInvoice':
      return 'Faktura zaliczkowa'
    case 'FinalInvoice':
      return 'Faktura finalna'
    case 'Proforma':
      return 'Proforma'
    case 'CorrectionInvoice':
      return 'Faktura korygująca'
  }
}

function formatDateInput(value: Date): string {
  return value.toISOString().slice(0, 10)
}

function getNameErrorMessage(error: { message?: string } | undefined): string | null {
  return error ? 'Pole jest wymagane.' : null
}

function getBuyerNipErrorMessage(
  error: { message?: string } | undefined,
  buyerKind: BuyerKind,
  value: string | undefined,
): string | null {
  if (!error) {
    return null
  }

  if (!value?.trim() && buyerKind !== 'Business') {
    return null
  }

  return value?.trim() ? 'Nieprawidłowy numer NIP.' : 'NIP jest wymagany dla firmy.'
}

function getIssueDateErrorMessage(error: { message?: string } | undefined): string | null {
  return error ? 'Podaj poprawną datę wystawienia.' : null
}

function getCurrencyErrorMessage(error: { message?: string } | undefined): string | null {
  return error ? 'Podaj poprawną walutę.' : null
}

function FieldError({ message }: { message: string | null }): ReactElement | null {
  if (!message) {
    return null
  }

  return (
    <p role="alert" className="field-error">
      {message}
    </p>
  )
}
