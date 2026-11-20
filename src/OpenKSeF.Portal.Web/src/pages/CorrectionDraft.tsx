import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState, type FormEvent, type ReactElement } from 'react'
import { Link, Navigate, useNavigate, useParams } from 'react-router-dom'

import {
  deleteIssuedInvoice,
  getIssuedInvoice,
  issueIssuedInvoice,
  updateCorrection,
} from '@/api/endpoints/issuedInvoices'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { readValidationEnvelope } from '@/api/errors'
import type { IssuedInvoiceResponse, PaymentMethod, SellerProfileResponse, ValidationIssue } from '@/api/types'
import { can } from '@/company/CompanyContext'
import { useCompany } from '@/company/useCompany'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { ErrorBanner } from '@/components/ErrorBanner'
import { Icon } from '@/components/Icon'
import { Skeleton } from '@/components/Skeleton'
import { CorrectionRows } from '@/components/invoicing/CorrectionRows'
import { EnvBadge, KsefStatusBadge } from '@/components/invoicing/KsefStatusBadge'
import { LinesTable } from '@/components/invoicing/LinesTable'
import { ValidationSummary } from '@/components/invoicing/ValidationSummary'
import { VatSummary } from '@/components/invoicing/VatSummary'
import '@/components/invoicing/invoicing.css'
import { useTenants } from '@/hooks/useTenants'
import {
  afterLines,
  canCorrectCountry,
  computeCorrection,
  CORRECTION_REASONS,
  CORRECTION_TYPES,
  fromCorrection,
  isCorrection,
  stateOf,
  toCorrectionRequest,
  type CorrectionFormValue,
  type CorrectionLineFormValue,
} from '@/invoicing/correction'
import { advanceDelta, KIND_LABELS } from '@/invoicing/advance'
import { countryName } from '@/invoicing/foreign'
import { parseDecimal, toFieldErrors } from '@/invoicing/form'
import { formatDate, formatDecimal, formatMoney, formatSignedMoney } from '@/invoicing/format'
import { computeTotals, FOREIGN_VAT_RATES } from '@/invoicing/totals'

const methods: { method: PaymentMethod; label: string }[] = [
  { method: 'Transfer', label: 'Przelew' },
  { method: 'Cash', label: 'Gotówka' },
  { method: 'Card', label: 'Karta' },
]

export function CorrectionDraftPage(): ReactElement {
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
  const originalId = invoiceQuery.data?.correctedInvoice?.id
  const originalQuery = useQuery({
    queryKey: ['issued-invoice', tenantId, originalId],
    queryFn: () => getIssuedInvoice(tenantId, originalId ?? ''),
    enabled: Boolean(tenantId && originalId),
  })

  const invoice = invoiceQuery.data
  const error = tenantsQuery.error ?? profileQuery.error ?? invoiceQuery.error ?? originalQuery.error
  const detailsHref = `/issued/${encodeURIComponent(id)}?tenantId=${encodeURIComponent(tenantId)}`

  return (
    <div className="inv-stack">
      {error ? (
        <ErrorBanner
          message={error instanceof Error ? error.message : 'Nie udało się załadować danych'}
          onRetry={() => {
            void profileQuery.refetch()
            void invoiceQuery.refetch()
            void originalQuery.refetch()
          }}
        />
      ) : invoice && !isCorrection(invoice) ? (
        <Navigate to={`/issued/${encodeURIComponent(id)}/edit?tenantId=${encodeURIComponent(tenantId)}`} replace />
      ) : invoice && invoice.status !== 'Draft' && invoice.status !== 'Rejected' ? (
        <Navigate to={detailsHref} replace />
      ) : !invoice || !profileQuery.data || !originalQuery.data ? (
        <Skeleton variant="card" lines={8} label="Wczytywanie korekty…" />
      ) : (
        <CorrectionForm key={`${tenantId}:${id}`} tenantId={tenantId} profile={profileQuery.data} invoice={invoice} original={originalQuery.data} />
      )}
    </div>
  )
}

interface CorrectionFormProps {
  tenantId: string
  profile: SellerProfileResponse
  invoice: IssuedInvoiceResponse
  original: IssuedInvoiceResponse
}

function CorrectionForm({ tenantId, profile, invoice, original }: CorrectionFormProps): ReactElement {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { company, setSwitchGuard } = useCompany()
  const canSubmit = can(company, 'issuing.submit')
  const [form, setForm] = useState<CorrectionFormValue>(() => fromCorrection(invoice))
  const advanceKind = invoice.invoiceType === 'KOR_ZAL'
  const [amount, setAmount] = useState(() => String(invoice.advanceAmount ?? '').replace('.', ','))
  const baseline = useRef(JSON.stringify([form, amount]))
  const version = useRef(invoice.version)
  const [errors, setErrors] = useState<ValidationIssue[]>([])
  const [warnings, setWarnings] = useState<ValidationIssue[]>(invoice.warnings)
  const [failure, setFailure] = useState<string | null>(null)
  const [confirmFor, setConfirmFor] = useState<IssuedInvoiceResponse | null>(null)
  const [pastDate, setPastDate] = useState<{ target: IssuedInvoiceResponse; message: string } | null>(null)
  const [deleteOpen, setDeleteOpen] = useState(false)

  const state = stateOf(original)
  const buyer = invoice.buyer
  const isForeign = buyer.kind === 'Foreign'
  const lineCorrection = computeCorrection(state.lines, afterLines(form))
  const currentAmount = invoice.amountBefore ?? original.advanceAmount ?? 0
  const afterAmount = parseDecimal(amount)
  const stateNet = computeTotals(state.lines).lineNet
  const delta = advanceDelta(state.lines, afterLines(form), currentAmount, afterAmount)
  const correction = advanceKind
    ? {
        ...delta,
        deltas: delta.rows,
        rows:
          lineCorrection.rows.length || afterAmount === currentAmount
            ? lineCorrection.rows
            : state.lines.flatMap((line, index) =>
                [true, false].map((isBefore) => ({ ...line, rowNo: index * 2 + (isBefore ? 1 : 2), isBefore, baseLineNo: line.lineNo, netAmount: stateNet[index] })),
              ),
      }
    : lineCorrection
  const removed = state.lines.filter((line) => !form.lines.some((after) => after.baseLineNo === line.lineNo))
  const fieldErrors = toFieldErrors(errors)
  const tenantQuery = `tenantId=${encodeURIComponent(tenantId)}`
  const originalHref = `/issued/${encodeURIComponent(original.id)}?${tenantQuery}`
  const set = (patch: Partial<CorrectionFormValue>): void => setForm((current) => ({ ...current, ...patch }))

  async function showFailure(failed: unknown, fallback: string): Promise<void> {
    const envelope = await readValidationEnvelope(failed)
    setErrors(envelope?.errors ?? [])
    setWarnings(envelope?.warnings ?? [])
    setFailure(envelope ? null : failed instanceof Error ? failed.message : fallback)
  }

  async function saveDraft(): Promise<IssuedInvoiceResponse> {
    const snapshot = JSON.stringify([form, amount])
    const request = toCorrectionRequest(form, buyer)
    const saved = await updateCorrection(tenantId, invoice.id, advanceKind ? { ...request, advanceAmount: afterAmount } : request, version.current)
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
    onError: (saveError) => showFailure(saveError, 'Nie udało się zapisać korekty'),
  })

  const issue = useMutation({
    mutationFn: ({ target, confirmPastIssueDate }: { target: IssuedInvoiceResponse; confirmPastIssueDate?: boolean }) =>
      issueIssuedInvoice(tenantId, target.id, target.version, target.sellerVersion, confirmPastIssueDate, target.invoiceType),
    onSuccess: (issued) => {
      setConfirmFor(null)
      queryClient.setQueryData(['issued-invoice', tenantId, issued.id], issued)
      void queryClient.invalidateQueries({ queryKey: ['issued-invoice', tenantId, original.id] })
      void queryClient.invalidateQueries({ queryKey: ['issued-invoices', tenantId] })
      void queryClient.invalidateQueries({ queryKey: ['seller-profile', tenantId] })
      navigate(`/issued/${encodeURIComponent(issued.id)}?${tenantQuery}`)
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
      return showFailure(issueError, 'Nie udało się wysłać korekty do KSeF')
    },
  })

  const prepareIssue = useMutation({
    mutationFn: saveDraft,
    onSuccess: (saved) => (saved.ksef.environment === 'test' || saved.ksef.environment === 'demo' ? issue.mutate({ target: saved }) : setConfirmFor(saved)),
    onError: (saveError) => showFailure(saveError, 'Nie udało się zapisać korekty'),
  })

  const remove = useMutation({
    mutationFn: () => deleteIssuedInvoice(tenantId, invoice.id, version.current),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['issued-invoices', tenantId] })
      void queryClient.invalidateQueries({ queryKey: ['issued-invoice', tenantId, original.id] })
      navigate(originalHref, { replace: true })
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

  const expectedNumber =
    profile.correctionNextNumberPeriod && form.issueDate.startsWith(profile.correctionNextNumberPeriod) ? profile.correctionNextNumberPreview : null
  const total = correction.totalGross
  const totalLabel = total > 0 ? 'Nabywca dopłaca' : total < 0 ? 'Do zwrotu nabywcy' : 'Kwota bez zmian'
  const title = invoice.status === 'Rejected' ? `Popraw korektę ${invoice.number ?? ''}`.trim() : KIND_LABELS[invoice.invoiceType ?? 'KOR']
  const reasonError = fieldErrors.correctionReason

  return (
    <form onSubmit={onSubmit} noValidate className="inv-form" aria-labelledby="correction-title">
      <div className="inv-form__main">
        <div>
          <Link to={originalHref} className="back-link">
            <Icon name="arrow-left" size={14} /> {original.number ?? 'Faktura'}
          </Link>
          <div className="inv-form__head">
            <h1 id="correction-title">{title}</h1>
            <KsefStatusBadge status={invoice.status} />
            {invoice.status === 'Draft' ? (
              <button type="button" className="ui-button ui-button--danger-outline ui-button--sm inv-form__delete" disabled={busy} onClick={() => setDeleteOpen(true)}>
                Usuń szkic
              </button>
            ) : null}
          </div>
          <p className="inv-help" data-testid="corrected-ref">
            Koryguje fakturę <Link to={originalHref} className="mono">{original.number}</Link> z {formatDate(original.issueDate)}
            {original.ksef.ksefNumber ? (
              <>
                , numer KSeF <span className="mono">{original.ksef.ksefNumber}</span>
              </>
            ) : null}
            . Wpisz stan po korekcie.
          </p>
        </div>

        {canSubmit ? null : (
          <div className="inv-alert inv-alert--info" role="status">
            Wysłać korektę do KSeF może właściciel, administrator lub księgowy.
          </div>
        )}
        {failure ? <ErrorBanner message={failure} /> : null}
        <ValidationSummary errors={errors} warnings={warnings} />

        <section className="inv-card" aria-labelledby="reason-title">
          <h2 id="reason-title">Przyczyna korekty</h2>
          <div className="inv-chips" role="group" aria-label="Typowe przyczyny">
            {CORRECTION_REASONS.map((reason) => (
              <button key={reason} type="button" aria-pressed={form.correctionReason === reason} onClick={() => set({ correctionReason: reason })}>
                {reason}
              </button>
            ))}
          </div>
          <div className="ui-form-group">
            <label htmlFor="correction-reason">Przyczyna (na fakturze)</label>
            <input
              id="correction-reason"
              type="text"
              maxLength={256}
              aria-required="true"
              aria-invalid={reasonError ? 'true' : 'false'}
              aria-describedby={reasonError ? 'correction-reason-error' : undefined}
              value={form.correctionReason}
              onChange={(event) => set({ correctionReason: event.target.value })}
            />
            {reasonError ? (
              <span className="inv-field-error" id="correction-reason-error">
                {reasonError}
              </span>
            ) : null}
          </div>
          <div className="ui-form-group">
            <label htmlFor="correction-type">Typ korekty (opcjonalnie)</label>
            <select
              id="correction-type"
              value={form.correctionType}
              aria-describedby="correction-type-help"
              onChange={(event) => set({ correctionType: event.target.value as CorrectionFormValue['correctionType'] })}
            >
              <option value="">Nie podawaj</option>
              {CORRECTION_TYPES.map((type) => (
                <option key={type.value} value={type.value}>
                  {type.value}: {type.label}
                </option>
              ))}
            </select>
            <span className="inv-help" id="correction-type-help">
              Datę ujęcia korekty ustala księgowy. Bez wyboru pole nie trafi na fakturę.
            </span>
          </div>
        </section>

        <section className="inv-card" aria-labelledby="buyer-title">
          <h2 id="buyer-title">Nabywca</h2>
          <dl className="inv-readonly" data-testid="buyer-locked">
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
            {isForeign && !canCorrectCountry(buyer) && buyer.countryCode ? (
              <>
                <dt>Kraj</dt>
                <dd>{countryName(buyer.countryCode)}</dd>
              </>
            ) : null}
            <dt>Waluta</dt>
            <dd>
              {invoice.currency}
              {invoice.exchangeRate ? ` · kurs ${formatDecimal(invoice.exchangeRate, 2)} PLN z faktury pierwotnej` : ''}
            </dd>
          </dl>
          <p className="inv-help">
            Numeru podatkowego nabywcy i waluty nie zmienisz korektą. Błędny NIP: użyj „Błędny NIP nabywcy” na fakturze pierwotnej.
          </p>
          <div className="inv-grid">
            <TextField id="buyer-name" label="Nazwa nabywcy" value={form.buyerName} error={fieldErrors['buyer.name']} onChange={(buyerName) => set({ buyerName })} />
            <TextField
              id="buyer-address-1"
              label="Adres (linia 1)"
              value={form.buyerAddressLine1}
              error={fieldErrors['buyer.addressLine1']}
              onChange={(buyerAddressLine1) => set({ buyerAddressLine1 })}
            />
            <TextField
              id="buyer-address-2"
              label="Adres (linia 2)"
              value={form.buyerAddressLine2}
              error={fieldErrors['buyer.addressLine2']}
              onChange={(buyerAddressLine2) => set({ buyerAddressLine2 })}
            />
            {canCorrectCountry(buyer) ? (
              <TextField
                id="buyer-country"
                label="Kod kraju"
                value={form.buyerCountryCode}
                error={fieldErrors['buyer.countryCode']}
                onChange={(buyerCountryCode) => set({ buyerCountryCode: buyerCountryCode.toUpperCase().slice(0, 2) })}
              />
            ) : null}
          </div>
          {fieldErrors['buyer.nip'] ?? fieldErrors['buyer.foreignId'] ?? fieldErrors['buyer.kind'] ? (
            <span className="inv-field-error">{fieldErrors['buyer.nip'] ?? fieldErrors['buyer.foreignId'] ?? fieldErrors['buyer.kind']}</span>
          ) : null}
        </section>

        {advanceKind ? (
          <section className="inv-card" aria-labelledby="advance-amount-title">
            <h2 id="advance-amount-title">Kwota zaliczki</h2>
            <div className="ui-form-group">
              <label htmlFor="advance-amount">Otrzymana kwota po korekcie ({invoice.currency})</label>
              <input
                id="advance-amount"
                type="text"
                inputMode="decimal"
                className="num"
                aria-invalid={fieldErrors.advanceAmount ? 'true' : 'false'}
                aria-describedby={fieldErrors.advanceAmount ? 'advance-amount-error advance-before' : 'advance-before'}
                value={amount}
                onChange={(event) => setAmount(event.target.value)}
              />
              {fieldErrors.advanceAmount ? (
                <span className="inv-field-error" id="advance-amount-error">
                  {fieldErrors.advanceAmount}
                </span>
              ) : null}
              <span className="inv-help" id="advance-before" data-testid="advance-before">
                Przed korektą: {formatMoney(currentAmount, invoice.currency)}. Zwrot całej zaliczki: wpisz 0.
              </span>
            </div>
          </section>
        ) : null}

        <LinesTable
          title={advanceKind ? 'Zamówienie po korekcie' : undefined}
          lines={form.lines}
          currency={invoice.currency}
          rates={isForeign ? FOREIGN_VAT_RATES : undefined}
          newLineRate={isForeign ? (state.lines[0]?.vatRate ?? FOREIGN_VAT_RATES[0].code) : undefined}
          fieldErrors={fieldErrors}
          onChange={(lines) => set({ lines: lines as CorrectionLineFormValue[] })}
        >
          {removed.length > 0 ? (
            <ul className="inv-help" aria-label="Usunięte pozycje">
              {removed.map((line) => (
                <li key={line.lineNo}>
                  Usunięta: {line.name}{' '}
                  <button
                    type="button"
                    className="ui-button ui-button--outline ui-button--sm"
                    onClick={() =>
                      set({
                        lines: [
                          ...form.lines,
                          {
                            baseLineNo: line.lineNo,
                            name: line.name,
                            unit: line.unit,
                            quantity: String(line.quantity).replace('.', ','),
                            unitNetPrice: String(line.unitNetPrice).replace('.', ','),
                            vatRate: line.vatRate,
                          },
                        ],
                      })
                    }
                  >
                    Przywróć
                  </button>
                </li>
              ))}
            </ul>
          ) : null}
        </LinesTable>

        <section className="inv-card" aria-labelledby="rows-title">
          <h2 id="rows-title">Przed i po korekcie</h2>
          <CorrectionRows rows={correction.rows} currency={invoice.currency} />
        </section>

        <section className="inv-card" aria-labelledby="dates-title">
          <h2 id="dates-title">Data i płatność</h2>
          <div className="inv-fields">
            <TextField type="date" id="issue-date" label="Data wystawienia" value={form.issueDate} error={fieldErrors.issueDate} onChange={(issueDate) => set({ issueDate })} />
            {total > 0 ? <TextField type="date" id="due-date" label="Termin płatności" value={form.dueDate} error={fieldErrors.dueDate} onChange={(dueDate) => set({ dueDate })} /> : null}
          </div>
          {total > 0 ? (
            <div className="inv-chips" role="group" aria-label="Forma płatności">
              {methods.map((option) => (
                <button key={option.method} type="button" aria-pressed={form.paymentMethod === option.method} onClick={() => set({ paymentMethod: option.method })}>
                  {option.label}
                </button>
              ))}
            </div>
          ) : (
            <p className="inv-help">Korekta bez dopłaty nie ma sekcji płatności.</p>
          )}
        </section>
      </div>

      <aside className="inv-side" aria-label="Podsumowanie korekty">
        <div className="inv-card inv-totals">
          <div>
            <span className="inv-caption">{totalLabel}</span>
            <output className="inv-amount" data-testid="total-gross">
              {formatSignedMoney(total, invoice.currency)}
            </output>
          </div>
          {invoice.number ? (
            <p className="inv-help">
              Numer <span className="mono">{invoice.number}</span> zostaje.
            </p>
          ) : (
            <p className="inv-help">
              Numer nadamy przy wystawieniu{expectedNumber ? ': ' : '.'}
              {expectedNumber ? <span className="mono">{expectedNumber}</span> : null}
            </p>
          )}
          <div className="inv-totals__actions">
            <button
              type="button"
              className="ui-button ui-button--primary ui-button--md inv-issue-button"
              disabled={busy || !canSubmit}
              onClick={() => prepareIssue.mutate()}
            >
              {prepareIssue.isPending || issue.isPending ? 'Wysyłanie…' : 'Wystaw korektę i wyślij do KSeF'} <EnvBadge environment={profile.ksefEnvironment} />
            </button>
            <button type="submit" className="ui-button ui-button--outline ui-button--md" disabled={busy}>
              {save.isPending ? 'Zapisywanie…' : 'Zapisz szkic'}
            </button>
          </div>
        </div>
        <div data-testid="correction-deltas">
          <VatSummary
            sticky
            rows={correction.deltas}
            totalNet={correction.totalNet}
            totalVat={correction.totalVat}
            totalGross={correction.totalGross}
            currency={invoice.currency}
          />
        </div>
      </aside>

      <ConfirmDialog
        open={deleteOpen}
        title="Usunąć szkic korekty?"
        message="Szkic zostanie trwale usunięty. Faktura pierwotna się nie zmieni."
        confirmLabel="Usuń szkic"
        cancelLabel="Wróć do edycji"
        variant="danger"
        isPending={remove.isPending}
        onConfirm={() => remove.mutate()}
        onCancel={() => setDeleteOpen(false)}
      />

      <ConfirmDialog
        open={confirmFor !== null}
        title="Wystawić korektę i wysłać do KSeF?"
        message={`${
          confirmFor?.ksef.environment === 'production' ? 'Środowisko PRODUKCJA' : 'Nie znamy środowiska KSeF, więc może to być PRODUKCJA'
        }: po przyjęciu przez KSeF korekta staje się dokumentem prawnym. Nie można jej usunąć ani edytować.`}
        confirmLabel="Wystaw i wyślij"
        cancelLabel="Wróć do edycji"
        pendingLabel="Wysyłanie…"
        isPending={issue.isPending}
        onConfirm={() => confirmFor && issue.mutate({ target: confirmFor })}
        onCancel={() => setConfirmFor(null)}
      >
        {confirmFor ? (
          <dl className="inv-readonly">
            <dt>Koryguje</dt>
            <dd className="mono">{original.number}</dd>
            <dt>Nabywca</dt>
            <dd>{confirmFor.buyer.name || '—'}</dd>
            <dt>Różnica</dt>
            <dd className="num" data-testid="confirm-total">
              <b>{formatSignedMoney(confirmFor.totalGross, confirmFor.currency)}</b>
            </dd>
          </dl>
        ) : null}
      </ConfirmDialog>

      <ConfirmDialog
        open={pastDate !== null}
        title="Wystawić korektę z datą w przeszłości?"
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

interface FieldProps {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
  error?: string
}

function TextField({ id, label, value, onChange, error, type = 'text' }: FieldProps & { type?: 'text' | 'date' }): ReactElement {
  return (
    <div className={`ui-form-group${type === 'date' ? ' inv-date' : ''}`}>
      <label htmlFor={id}>{label}</label>
      <input
        id={id}
        type={type}
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
