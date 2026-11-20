import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useRef, useState, type FormEvent, type ReactElement } from 'react'
import { Link, Navigate, useNavigate, useParams } from 'react-router-dom'

import { deleteIssuedInvoice, getIssuedInvoice, issueIssuedInvoice, updateAdvance, updateFinal } from '@/api/endpoints/issuedInvoices'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { readValidationEnvelope } from '@/api/errors'
import type { IssuedInvoiceResponse, PaymentMethod, SellerProfileResponse, ValidationIssue } from '@/api/types'
import { can } from '@/company/CompanyContext'
import { useCompany } from '@/company/useCompany'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { ErrorBanner } from '@/components/ErrorBanner'
import { Icon } from '@/components/Icon'
import { Skeleton } from '@/components/Skeleton'
import { BuyerSection } from '@/components/invoicing/BuyerSection'
import { ChainCard } from '@/components/invoicing/ChainCard'
import { CurrencySection } from '@/components/invoicing/CurrencySection'
import { DatesSection } from '@/components/invoicing/DatesSection'
import { ExemptionBasisField } from '@/components/invoicing/ExemptionBasisField'
import { EnvBadge, KsefStatusBadge } from '@/components/invoicing/KsefStatusBadge'
import { LinesTable } from '@/components/invoicing/LinesTable'
import { PaymentSection } from '@/components/invoicing/PaymentSection'
import { ValidationSummary } from '@/components/invoicing/ValidationSummary'
import { VatSummary } from '@/components/invoicing/VatSummary'
import '@/components/invoicing/invoicing.css'
import { useTenants } from '@/hooks/useTenants'
import {
  advancesOf,
  exceedsAdvances,
  formLines,
  isChainDraft,
  isFirstAdvance,
  KIND_LABELS,
  orderValue,
  paidBefore,
  remainingAfterAdvances,
  splitAdvance,
  toAdvanceRequest,
  toFinalRequest,
} from '@/invoicing/advance'
import { isCorrection } from '@/invoicing/correction'
import { countryName, foreignAccountOf, foreignRateFor } from '@/invoicing/foreign'
import { fromResponse, hasExemptLine, parseDecimal, toFieldErrors, withBuyer, type DraftFormValue } from '@/invoicing/form'
import { formatDecimal, formatMoney } from '@/invoicing/format'
import { FOREIGN_VAT_RATES } from '@/invoicing/totals'

const methods: { method: PaymentMethod; label: string }[] = [
  { method: 'Transfer', label: 'Przelew' },
  { method: 'Cash', label: 'Gotówka' },
  { method: 'Card', label: 'Karta' },
]

export const EXCEEDS_MESSAGE = 'Zaliczki przekraczają wartość rozliczenia. Najpierw skoryguj fakturę zaliczkową.'

export function ChainDraftPage(): ReactElement {
  const { id = '' } = useParams()
  const tenantsQuery = useTenants()
  const { tenantId } = useCompany()

  const profileQuery = useQuery({
    queryKey: ['seller-profile', tenantId],
    queryFn: () => getSellerProfile(tenantId),
    enabled: Boolean(tenantId),
  })
  const invoiceQuery = useQuery({
    queryKey: ['issued-invoice', tenantId, id],
    queryFn: () => getIssuedInvoice(tenantId, id),
    enabled: Boolean(tenantId && id),
  })

  const invoice = invoiceQuery.data
  const error = tenantsQuery.error ?? profileQuery.error ?? invoiceQuery.error
  const href = (suffix: string): string => `/issued/${encodeURIComponent(id)}${suffix}?tenantId=${encodeURIComponent(tenantId)}`

  return (
    <div className="inv-stack">
      {error ? (
        <ErrorBanner
          message={error instanceof Error ? error.message : 'Nie udało się załadować danych'}
          onRetry={() => {
            void profileQuery.refetch()
            void invoiceQuery.refetch()
          }}
        />
      ) : invoice && !isChainDraft(invoice) ? (
        <Navigate to={href(isCorrection(invoice) ? '/correct' : '/edit')} replace />
      ) : invoice && invoice.status !== 'Draft' && invoice.status !== 'Rejected' ? (
        <Navigate to={href('')} replace />
      ) : !invoice || !profileQuery.data ? (
        <Skeleton variant="card" lines={8} label="Wczytywanie faktury…" />
      ) : (
        <ChainForm key={`${tenantId}:${id}`} tenantId={tenantId} profile={profileQuery.data} invoice={invoice} />
      )}
    </div>
  )
}

interface ChainFormProps {
  tenantId: string
  profile: SellerProfileResponse
  invoice: IssuedInvoiceResponse
}

function ChainForm({ tenantId, profile, invoice }: ChainFormProps): ReactElement {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { company, setSwitchGuard } = useCompany()
  const canSubmit = can(company, 'issuing.submit')
  const advance = invoice.invoiceType === 'ZAL'
  const first = isFirstAdvance(invoice)
  const [form, setForm] = useState<DraftFormValue>(() => fromResponse(invoice))
  const [amount, setAmount] = useState(() => (invoice.advanceAmount ? formatDecimal(invoice.advanceAmount, 2).replace(/\s/g, '') : ''))
  const baseline = useRef(JSON.stringify([form, amount]))
  const version = useRef(invoice.version)
  const [errors, setErrors] = useState<ValidationIssue[]>([])
  const [warnings, setWarnings] = useState<ValidationIssue[]>(invoice.warnings)
  const [failure, setFailure] = useState<string | null>(null)
  const [confirmFor, setConfirmFor] = useState<IssuedInvoiceResponse | null>(null)
  const [pastDate, setPastDate] = useState<{ target: IssuedInvoiceResponse; message: string } | null>(null)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [advances] = useState(() => advancesOf(invoice))

  const tenantQuery = `tenantId=${encodeURIComponent(tenantId)}`
  const itemHref = (target: string): string => `/issued/${encodeURIComponent(target)}?${tenantQuery}`
  const backHref = invoice.orderChainId && invoice.orderChainId !== invoice.id ? itemHref(invoice.orderChainId) : `/issued?${tenantQuery}`
  const fieldErrors = toFieldErrors(errors)
  const isForeign = form.buyer.kind === 'Foreign'
  const lines = formLines(form)
  const received = parseDecimal(amount)
  const value = orderValue(lines)
  const paid = paidBefore(invoice)
  const open = Math.round((value - paid) * 100) / 100
  const left = Math.round((open - received) * 100) / 100
  const summary = advance ? splitAdvance(lines, received) : remainingAfterAdvances(lines, advances)
  const exceeds = !advance && exceedsAdvances(summary, invoice.chain?.advances.filter((item) => item.status === 'Accepted').length ?? 0)
  const set = (patch: Partial<DraftFormValue>): void => setForm((current) => ({ ...current, ...patch }))
  const updateForm = useCallback((update: (current: DraftFormValue) => DraftFormValue) => setForm(update), [])

  async function showFailure(failed: unknown, fallback: string): Promise<void> {
    const envelope = await readValidationEnvelope(failed)
    setErrors(envelope?.errors ?? [])
    setWarnings(envelope?.warnings ?? [])
    setFailure(envelope ? null : failed instanceof Error ? failed.message : fallback)
  }

  async function saveDraft(): Promise<IssuedInvoiceResponse> {
    const snapshot = JSON.stringify([form, amount])
    const saved = advance
      ? await updateAdvance(tenantId, invoice.id, toAdvanceRequest(form, amount, first, invoice.exchangeRate), version.current)
      : await updateFinal(tenantId, invoice.id, toFinalRequest(form, invoice.exchangeRate), version.current)
    version.current = saved.version
    baseline.current = snapshot
    setErrors([])
    setFailure(null)
    setWarnings(saved.warnings)
    queryClient.setQueryData(['issued-invoice', tenantId, saved.id], saved)
    void queryClient.invalidateQueries({ queryKey: ['issued-invoices', tenantId] })
    return saved
  }

  const save = useMutation({
    mutationFn: saveDraft,
    onError: (saveError) => showFailure(saveError, 'Nie udało się zapisać szkicu'),
  })

  const issue = useMutation({
    mutationFn: ({ target, confirmPastIssueDate }: { target: IssuedInvoiceResponse; confirmPastIssueDate?: boolean }) =>
      issueIssuedInvoice(tenantId, target.id, target.version, target.sellerVersion, confirmPastIssueDate, target.invoiceType),
    onSuccess: (issued) => {
      setConfirmFor(null)
      queryClient.setQueryData(['issued-invoice', tenantId, issued.id], issued)
      if (invoice.orderChainId) void queryClient.invalidateQueries({ queryKey: ['issued-invoice', tenantId, invoice.orderChainId] })
      void queryClient.invalidateQueries({ queryKey: ['issued-invoices', tenantId] })
      void queryClient.invalidateQueries({ queryKey: ['seller-profile', tenantId] })
      navigate(itemHref(issued.id))
    },
    onError: async (issueError, { target, confirmPastIssueDate }) => {
      setConfirmFor(null)
      setPastDate(null)
      const envelope = await readValidationEnvelope(issueError)
      const only = envelope?.errors.length === 1 ? envelope.errors[0] : null
      if (!confirmPastIssueDate && only?.code === 'INV-W-PAST-DATE') {
        setPastDate({ target, message: only.message })
        return
      }
      void queryClient.invalidateQueries({ queryKey: ['issued-invoice', tenantId, invoice.id] })
      return showFailure(issueError, 'Nie udało się wysłać faktury do KSeF')
    },
  })

  const prepareIssue = useMutation({
    mutationFn: saveDraft,
    onSuccess: (saved) => (saved.ksef.environment === 'test' || saved.ksef.environment === 'demo' ? issue.mutate({ target: saved }) : setConfirmFor(saved)),
    onError: (saveError) => showFailure(saveError, 'Nie udało się zapisać szkicu'),
  })

  const remove = useMutation({
    mutationFn: () => deleteIssuedInvoice(tenantId, invoice.id, version.current),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['issued-invoices', tenantId] })
      if (invoice.orderChainId) void queryClient.invalidateQueries({ queryKey: ['issued-invoice', tenantId, invoice.orderChainId] })
      navigate(backHref, { replace: true })
      queryClient.removeQueries({ queryKey: ['issued-invoice', tenantId, invoice.id] })
    },
    onError: (removeError) => {
      setDeleteOpen(false)
      return showFailure(removeError, 'Nie udało się usunąć szkicu')
    },
  })

  const busy = save.isPending || prepareIssue.isPending || issue.isPending || remove.isPending

  useEffect(() => {
    setSwitchGuard({
      dirty: JSON.stringify([form, amount]) !== baseline.current,
      busy,
      environment: profile.ksefEnvironment,
      save: () => save.mutateAsync(),
    })
    return () => setSwitchGuard(null)
  })

  function onSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault()
    save.mutate()
  }

  const preview = advance
    ? profile.advanceNextNumberPeriod && form.issueDate.startsWith(profile.advanceNextNumberPeriod)
      ? profile.advanceNextNumberPreview
      : null
    : form.issueDate.startsWith(profile.nextNumberPeriod)
      ? profile.nextNumberPreview
      : null
  const kindLabel = KIND_LABELS[invoice.invoiceType ?? 'ZAL']
  const title = invoice.status === 'Rejected' ? `Popraw: ${kindLabel.toLowerCase()} ${invoice.number ?? ''}`.trim() : kindLabel
  const profileHref = `/company/invoice-data?${tenantQuery}`
  const buyer = invoice.buyer

  return (
    <form onSubmit={onSubmit} noValidate className="inv-form" aria-labelledby="chain-draft-title">
      <div className="inv-form__main">
        <div>
          <Link to={backHref} className="back-link">
            <Icon name="arrow-left" size={14} /> {backHref.startsWith('/issued?') ? 'Wystawione' : 'Pierwsza zaliczka'}
          </Link>
          <div className="inv-form__head">
            <h1 id="chain-draft-title">{title}</h1>
            <KsefStatusBadge status={invoice.status} />
            {invoice.status === 'Draft' ? (
              <button type="button" className="ui-button ui-button--danger-outline ui-button--sm inv-form__delete" disabled={busy} onClick={() => setDeleteOpen(true)}>
                Usuń szkic
              </button>
            ) : null}
          </div>
        </div>

        {canSubmit ? null : (
          <div className="inv-alert inv-alert--info" role="status">
            Wysłać fakturę do KSeF może właściciel, administrator lub księgowy.
          </div>
        )}
        {failure ? <ErrorBanner message={failure} /> : null}
        <ValidationSummary errors={errors} warnings={warnings} />

        {invoice.chain ? <ChainCard chain={invoice.chain} currency={invoice.currency} currentId={invoice.id} itemHref={itemHref} /> : null}

        {first ? (
          <BuyerSection value={form.buyer} fieldErrors={fieldErrors} onChange={(next) => setForm(withBuyer(form, next))} />
        ) : (
          <section className="inv-card" aria-labelledby="buyer-title">
            <h2 id="buyer-title">Nabywca</h2>
            <dl className="inv-readonly" data-testid="buyer-locked">
              <dt>Nazwa</dt>
              <dd>{buyer.name ?? '—'}</dd>
              {buyer.nip ? (
                <>
                  <dt>NIP</dt>
                  <dd className="mono">{buyer.nip}</dd>
                </>
              ) : null}
              {buyer.foreignId ? (
                <>
                  <dt>{buyer.idType === 'VatUe' ? 'Numer VAT UE' : 'Numer podatkowy'}</dt>
                  <dd className="mono">{buyer.foreignId}</dd>
                </>
              ) : null}
              {buyer.countryCode ? (
                <>
                  <dt>Kraj</dt>
                  <dd>{countryName(buyer.countryCode)}</dd>
                </>
              ) : null}
              <dt>Waluta</dt>
              <dd>{invoice.currency}</dd>
            </dl>
            <p className="inv-help">Nabywcę i walutę ustala pierwsza faktura zaliczkowa zamówienia.</p>
          </section>
        )}

        <LinesTable
          title={advance ? 'Zamówienie' : 'Pozycje'}
          lines={form.lines}
          currency={form.currency}
          rates={isForeign ? FOREIGN_VAT_RATES : undefined}
          newLineRate={isForeign ? foreignRateFor(form.buyer.countryCode) : undefined}
          fieldErrors={fieldErrors}
          onChange={(next) => set({ lines: next })}
        >
          {advance ? (
            <p className="inv-help">
              Wartość zamówienia: <b data-testid="order-value">{formatMoney(value, form.currency)}</b>
            </p>
          ) : (
            <p className="inv-help">Pozycje z pełnymi wartościami. Zaliczki odejmiemy w podsumowaniu.</p>
          )}
          {hasExemptLine(form.lines) ? (
            <ExemptionBasisField
              choice={form.exemptionChoice}
              text={form.exemptionText}
              serverError={fieldErrors.exemptionBasis}
              onChange={({ choice, text }) => set({ exemptionChoice: choice, exemptionText: text })}
            />
          ) : null}
        </LinesTable>

        {advance ? (
          <section className="inv-card" aria-labelledby="advance-title">
            <h2 id="advance-title">Zaliczka</h2>
            <div className="inv-fields">
              <DateField id="issue-date" label="Data wystawienia" value={form.issueDate} error={fieldErrors.issueDate} onChange={(issueDate) => set({ issueDate })} />
              <DateField
                id="payment-date"
                label="Data otrzymania zapłaty"
                value={form.saleDate}
                error={fieldErrors.paymentDate ?? fieldErrors.saleDate}
                onChange={(saleDate) => set({ saleDate })}
              />
              <div className="ui-form-group">
                <label htmlFor="advance-amount">Otrzymana kwota brutto ({form.currency})</label>
                <input
                  id="advance-amount"
                  type="text"
                  inputMode="decimal"
                  className="num"
                  aria-required="true"
                  aria-invalid={fieldErrors.advanceAmount ? 'true' : 'false'}
                  aria-describedby={fieldErrors.advanceAmount ? 'advance-amount-error advance-open' : 'advance-open'}
                  value={amount}
                  onChange={(event) => setAmount(event.target.value)}
                />
                {fieldErrors.advanceAmount ? (
                  <span className="inv-field-error" id="advance-amount-error">
                    {fieldErrors.advanceAmount}
                  </span>
                ) : null}
                <span className="inv-help" id="advance-open" data-testid="advance-open">
                  Wcześniejsze zaliczki: {formatMoney(paid, form.currency)}. Do zapłaty przed tą zaliczką: {formatMoney(open, form.currency)}.
                </span>
              </div>
            </div>
            {received > open ? (
              <div className="inv-alert inv-alert--warning" role="alert">
                Kwota przekracza pozostałą wartość zamówienia ({formatMoney(open, form.currency)}).
              </div>
            ) : received > 0 && left === 0 ? (
              <p className="inv-help" role="status">
                To ostatnia zaliczka. Faktura wymieni wszystkie wcześniejsze faktury zaliczkowe zamówienia, a faktura rozliczająca będzie opcjonalna.
              </p>
            ) : null}
            <div className="inv-chips" role="group" aria-label="Forma płatności">
              {methods.map((option) => (
                <button key={option.method} type="button" aria-pressed={form.paymentMethod === option.method} onClick={() => set({ paymentMethod: option.method })}>
                  {option.label}
                </button>
              ))}
            </div>
            {isForeign || form.currency !== 'PLN' ? (
              <CurrencySection
                tenantId={tenantId}
                currency={form.currency}
                exchangeRate={form.exchangeRate}
                exchangeRateDate={form.exchangeRateDate}
                issueDate={form.issueDate}
                saleDate={form.saleDate}
                fieldErrors={fieldErrors}
                lockCurrency={!first}
                onUpdate={updateForm}
              />
            ) : null}
          </section>
        ) : (
          <section className="inv-card" aria-labelledby="dates-title">
            <h2 id="dates-title">Daty i płatność</h2>
            <DatesSection issueDate={form.issueDate} saleDate={form.saleDate} dueDate={form.dueDate} fieldErrors={fieldErrors} onChange={(dates) => set(dates)} />
            {form.currency !== 'PLN' ? (
              <CurrencySection
                tenantId={tenantId}
                currency={form.currency}
                exchangeRate={form.exchangeRate}
                exchangeRateDate={form.exchangeRateDate}
                issueDate={form.issueDate}
                saleDate={form.saleDate}
                fieldErrors={fieldErrors}
                lockCurrency
                onUpdate={updateForm}
              />
            ) : null}
            {summary.totalGross > 0 ? (
              <PaymentSection
                paymentMethod={form.paymentMethod}
                splitPayment={form.splitPayment}
                bankAccount={profile.bankAccount}
                foreignInvoice={isForeign}
                foreignAccount={isForeign ? foreignAccountOf(profile) : null}
                totalGross={summary.totalGross}
                profileHref={profileHref}
                onChange={(payment) => set(payment)}
              />
            ) : (
              <p className="inv-help">Zaliczki pokrywają całość. Faktura rozliczająca na 0,00 nie ma sekcji płatności.</p>
            )}
          </section>
        )}
      </div>

      <aside className="inv-side" aria-label="Podsumowanie faktury">
        <div className="inv-card inv-totals">
          <div>
            <span className="inv-caption">{advance ? 'Zaliczka' : 'Pozostało do zapłaty'}</span>
            <output className="inv-amount" data-testid="total-gross">
              {formatMoney(summary.totalGross, form.currency)}
            </output>
          </div>
          {advance ? (
            <p className="inv-help" data-testid="left-after">
              Po tej zaliczce do zapłaty zostanie {formatMoney(left, form.currency)}.
            </p>
          ) : (
            <p className="inv-help" data-testid="advances-deducted">
              Wartość brutto {formatMoney(value, form.currency)}, zaliczki {formatMoney(Math.round((value - summary.totalGross) * 100) / 100, form.currency)}.
            </p>
          )}
          {exceeds ? (
            <div className="inv-alert inv-alert--warning" role="alert" data-testid="exceeds">
              {EXCEEDS_MESSAGE}
            </div>
          ) : null}
          {invoice.number ? (
            <p className="inv-help">
              Numer <span className="mono">{invoice.number}</span> zostaje.
            </p>
          ) : (
            <p className="inv-help">
              Numer nadamy przy wystawieniu{preview ? ': ' : '.'}
              {preview ? <span className="mono">{preview}</span> : null}
            </p>
          )}
          <div className="inv-totals__actions">
            <button
              type="button"
              className="ui-button ui-button--primary ui-button--md inv-issue-button"
              disabled={busy || !canSubmit || !profile.isComplete}
              onClick={() => prepareIssue.mutate()}
            >
              {prepareIssue.isPending || issue.isPending ? 'Wysyłanie…' : 'Wystaw i wyślij do KSeF'} <EnvBadge environment={profile.ksefEnvironment} />
            </button>
            <button type="submit" className="ui-button ui-button--outline ui-button--md" disabled={busy}>
              {save.isPending ? 'Zapisywanie…' : 'Zapisz szkic'}
            </button>
          </div>
        </div>
        <div data-testid="chain-summary">
          <VatSummary sticky rows={summary.rows} totalNet={summary.totalNet} totalVat={summary.totalVat} totalGross={summary.totalGross} currency={form.currency} />
        </div>
      </aside>

      <ConfirmDialog
        open={deleteOpen}
        title="Usunąć szkic?"
        message="Szkic zostanie trwale usunięty. Wcześniejsze dokumenty zamówienia się nie zmienią."
        confirmLabel="Usuń szkic"
        cancelLabel="Wróć do edycji"
        variant="danger"
        isPending={remove.isPending}
        onConfirm={() => remove.mutate()}
        onCancel={() => setDeleteOpen(false)}
      />

      <ConfirmDialog
        open={confirmFor !== null}
        title="Wystawić fakturę i wysłać do KSeF?"
        message={`${
          confirmFor?.ksef.environment === 'production' ? 'Środowisko PRODUKCJA' : 'Nie znamy środowiska KSeF, więc może to być PRODUKCJA'
        }: po przyjęciu przez KSeF faktura staje się dokumentem prawnym. Nie można jej usunąć ani edytować.`}
        confirmLabel="Wystaw i wyślij"
        cancelLabel="Wróć do edycji"
        pendingLabel="Wysyłanie…"
        isPending={issue.isPending}
        onConfirm={() => confirmFor && issue.mutate({ target: confirmFor })}
        onCancel={() => setConfirmFor(null)}
      >
        {confirmFor ? (
          <dl className="inv-readonly">
            <dt>Rodzaj</dt>
            <dd>{kindLabel}</dd>
            <dt>Nabywca</dt>
            <dd>{confirmFor.buyer.name || '—'}</dd>
            <dt>{advance ? 'Zaliczka' : 'Do zapłaty'}</dt>
            <dd className="num" data-testid="confirm-total">
              <b>{formatMoney(confirmFor.totalGross, confirmFor.currency)}</b>
            </dd>
          </dl>
        ) : null}
      </ConfirmDialog>

      <ConfirmDialog
        open={pastDate !== null}
        title="Wystawić fakturę z datą w przeszłości?"
        message={pastDate?.message ?? ''}
        confirmLabel="Potwierdź"
        cancelLabel="Anuluj"
        pendingLabel="Wysyłanie…"
        isPending={issue.isPending}
        onConfirm={() => pastDate && issue.mutate({ target: pastDate.target, confirmPastIssueDate: true })}
        onCancel={() => setPastDate(null)}
      />
    </form>
  )
}

interface DateFieldProps {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
  error?: string
}

function DateField({ id, label, value, onChange, error }: DateFieldProps): ReactElement {
  return (
    <div className="ui-form-group inv-date">
      <label htmlFor={id}>{label}</label>
      <input
        id={id}
        type="date"
        value={value}
        aria-invalid={error ? 'true' : 'false'}
        aria-describedby={error ? `${id}-error` : undefined}
        onChange={(event) => onChange(event.target.value)}
      />
      {error ? (
        <span className="inv-field-error" id={`${id}-error`}>
          {error}
        </span>
      ) : null}
    </div>
  )
}
