import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useRef, useState, type FormEvent, type ReactElement } from 'react'
import { Link, Navigate, useLocation, useNavigate, useParams } from 'react-router-dom'

import {
  createIssuedInvoice,
  deleteIssuedInvoice,
  getIssuedInvoice,
  issueIssuedInvoice,
  updateIssuedInvoice,
} from '@/api/endpoints/issuedInvoices'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { ApiError, readValidationEnvelope } from '@/api/errors'
import type { IssuedInvoiceResponse, IssuedInvoiceStatus, SellerProfileResponse, ValidationIssue } from '@/api/types'
import { useCompany } from '@/company/useCompany'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { ErrorBanner } from '@/components/ErrorBanner'
import { Icon } from '@/components/Icon'
import { Skeleton } from '@/components/Skeleton'
import { BuyerSection } from '@/components/invoicing/BuyerSection'
import { CurrencySection } from '@/components/invoicing/CurrencySection'
import { DatesSection } from '@/components/invoicing/DatesSection'
import { ExemptionBasisField } from '@/components/invoicing/ExemptionBasisField'
import { EnvBadge, KsefStatusBadge } from '@/components/invoicing/KsefStatusBadge'
import { LinesTable } from '@/components/invoicing/LinesTable'
import { PaymentSection } from '@/components/invoicing/PaymentSection'
import { SellerCard } from '@/components/invoicing/SellerCard'
import { ValidationSummary } from '@/components/invoicing/ValidationSummary'
import { VatSummary } from '@/components/invoicing/VatSummary'
import '@/components/invoicing/invoicing.css'
import { useTenants } from '@/hooks/useTenants'
import {
  emptyDraft,
  fromResponse,
  hasExemptLine,
  missingTaxIdError,
  parseDecimal,
  rateToInput,
  toDraftRequest,
  toFieldErrors,
  withBuyer,
  type DraftFormValue,
} from '@/invoicing/form'
import { countryName, foreignAccountOf, foreignRateFor } from '@/invoicing/foreign'
import { formatDate, formatDecimal, formatMoney } from '@/invoicing/format'
import { isReissueBlocked } from '@/invoicing/status'
import { computeTotals, FOREIGN_VAT_RATES } from '@/invoicing/totals'

const EDITABLE: IssuedInvoiceStatus[] = ['Draft', 'Rejected']

export function InvoiceDraftPage(): ReactElement {
  const { id } = useParams()
  const tenantsQuery = useTenants()
  const { tenantId } = useCompany()
  const [formSlot, setFormSlot] = useState<{ id?: string; created?: string; generation: number }>({ id, generation: 0 })

  // A new form stays mounted through the replace navigation to the invoice it created, because the issue flow continues there. Any other id change must remount, or the form saves one invoice's content into another.
  if (formSlot.id !== id) {
    setFormSlot({ id, generation: id !== undefined && formSlot.created === id ? formSlot.generation : formSlot.generation + 1 })
  }

  const profileQuery = useQuery({
    queryKey: ['seller-profile', tenantId],
    queryFn: () => getSellerProfile(tenantId),
    enabled: Boolean(tenantId),
  })
  const invoiceQuery = useQuery({
    queryKey: ['issued-invoice', tenantId, id],
    queryFn: () => getIssuedInvoice(tenantId, id ?? ''),
    enabled: Boolean(tenantId && id),
  })

  const error = tenantsQuery.error ?? profileQuery.error ?? invoiceQuery.error
  const loading = tenantsQuery.isLoading || profileQuery.isLoading || (Boolean(id) && invoiceQuery.isLoading)

  return (
    <div className="invoicing">
      {error ? (
        <ErrorBanner
          message={error instanceof Error ? error.message : 'Nie udało się załadować danych'}
          onRetry={() => {
            void profileQuery.refetch()
            void invoiceQuery.refetch()
          }}
        />
      ) : loading || !profileQuery.data ? (
        <Skeleton lines={8} />
      ) : !id && !profileQuery.data.isComplete ? (
        <Navigate to={`/company/invoice-data?tenantId=${encodeURIComponent(tenantId)}&next=new`} replace />
      ) : invoiceQuery.data && (!EDITABLE.includes(invoiceQuery.data.status) || isReissueBlocked(invoiceQuery.data)) ? (
        <Navigate to={`/issued/${encodeURIComponent(invoiceQuery.data.id)}?tenantId=${encodeURIComponent(tenantId)}`} replace />
      ) : (
        <DraftForm
          key={`${tenantId}:${formSlot.generation}`}
          tenantId={tenantId}
          profile={profileQuery.data}
          invoice={invoiceQuery.data}
          onCreated={(created) => setFormSlot((slot) => ({ ...slot, created }))}
        />
      )}
    </div>
  )
}

interface DraftFormProps {
  tenantId: string
  profile: SellerProfileResponse
  invoice?: IssuedInvoiceResponse
  onCreated: (id: string) => void
}

const timeFormat = new Intl.DateTimeFormat('pl-PL', { hour: '2-digit', minute: '2-digit' })

const failureText = (error: unknown, fallback: string): string =>
  !(error instanceof Error) || (error instanceof ApiError && (error.status >= 500 || !(error.originalError instanceof Response) || !error.originalError.headers.get('Content-Type')?.includes('json')))
    ? fallback
    : error.message

function DraftForm({ tenantId, profile, invoice, onCreated }: DraftFormProps): ReactElement {
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const { setSwitchGuard } = useCompany()
  const [form, setForm] = useState<DraftFormValue>(() => (invoice ? fromResponse(invoice) : emptyDraft()))
  const baseline = useRef(JSON.stringify(form))
  const [errors, setErrors] = useState<ValidationIssue[]>([])
  const [warnings, setWarnings] = useState<ValidationIssue[]>(invoice?.warnings ?? [])
  const [failure, setFailure] = useState<string | null>(null)
  const [confirmFor, setConfirmFor] = useState<IssuedInvoiceResponse | null>(null)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const issueOpener = useRef<HTMLButtonElement | null>(null)
  const mounted = useRef(true)

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  const totals = computeTotals(
    form.lines.map((line) => ({
      quantity: parseDecimal(line.quantity),
      unitNetPrice: parseDecimal(line.unitNetPrice),
      vatRate: line.vatRate,
    })),
  )
  const fieldErrors = toFieldErrors(errors)

  async function saveDraft(stay = false): Promise<IssuedInvoiceResponse> {
    const snapshot = JSON.stringify(form)
    const request = toDraftRequest(form, invoice?.exchangeRate)
    const saved = invoice ? await updateIssuedInvoice(tenantId, invoice.id, request) : await createIssuedInvoice(tenantId, request)
    if (form.currency !== 'PLN' && !form.exchangeRate.trim() && saved.exchangeRate) {
      const withServerRate = { ...form, exchangeRate: rateToInput(saved.exchangeRate), exchangeRateDate: saved.exchangeRateDate ?? null }
      baseline.current = JSON.stringify(withServerRate)
      setForm(withServerRate)
    } else {
      baseline.current = snapshot
    }
    setErrors([])
    setFailure(null)
    setWarnings(saved.warnings)
    queryClient.setQueryData(['issued-invoice', tenantId, saved.id], saved)
    void queryClient.invalidateQueries({ queryKey: ['issued-invoices', tenantId] })
    if (!invoice && !stay) {
      if (mounted.current) onCreated(saved.id)
      navigate(`/issued/${encodeURIComponent(saved.id)}/edit?tenantId=${encodeURIComponent(tenantId)}`, { replace: true })
    }
    return saved
  }

  async function showFailure(failed: unknown, fallback: string): Promise<void> {
    const envelope = await readValidationEnvelope(failed)
    setErrors(envelope?.errors ?? [])
    setWarnings(envelope?.warnings ?? [])
    setFailure(envelope ? null : failureText(failed, fallback))
  }

  async function recoverIssue(target: IssuedInvoiceResponse, failed: unknown): Promise<void> {
    setConfirmFor(null)
    if (await readValidationEnvelope(failed)) {
      return showFailure(failed, 'Nie udało się wysłać faktury do KSeF')
    }

    setErrors([])
    let fresh: IssuedInvoiceResponse
    try {
      fresh = await getIssuedInvoice(tenantId, target.id)
    } catch {
      setFailure('Brak połączenia z serwerem. Nie wiemy, czy faktura została wysłana. Sprawdź połączenie i odśwież stronę.')
      return
    }

    queryClient.setQueryData(['issued-invoice', tenantId, fresh.id], fresh)
    if (fresh.status !== target.status || fresh.issuedAt !== target.issuedAt) {
      navigate(`/issued/${encodeURIComponent(fresh.id)}?tenantId=${encodeURIComponent(tenantId)}`, { replace: true })
      return
    }
    const statusLabel = fresh.status === 'Draft' ? 'Szkic' : 'Odrzucona'
    setFailure(failureText(failed, `Nie udało się połączyć z serwerem. Faktura nadal ma status: ${statusLabel}. Możesz spróbować ponownie.`))
  }

  const save = useMutation({
    mutationFn: (stay: boolean) => saveDraft(stay),
    onError: (saveError) => showFailure(saveError, 'Nie udało się zapisać szkicu'),
  })

  const issue = useMutation({
    mutationFn: (target: IssuedInvoiceResponse) => issueIssuedInvoice(tenantId, target.id),
    onSuccess: (issued) => {
      queryClient.setQueryData(['issued-invoice', tenantId, issued.id], issued)
      void queryClient.invalidateQueries({ queryKey: ['seller-profile', tenantId] })
      navigate(`/issued/${encodeURIComponent(issued.id)}?tenantId=${encodeURIComponent(tenantId)}`)
    },
    onError: (issueError, target) => recoverIssue(target, issueError),
  })

  const prepareIssue = useMutation({
    mutationFn: () => saveDraft(),
    onSuccess: (saved) => {
      if (saved.ksef.environment === 'test' || saved.ksef.environment === 'demo') {
        issue.mutate(saved)
      } else {
        setConfirmFor(saved)
      }
    },
    onError: (saveError) => (invoice ? recoverIssue(invoice, saveError) : showFailure(saveError, 'Nie udało się zapisać szkicu')),
  })

  const remove = useMutation({
    mutationFn: () => deleteIssuedInvoice(tenantId, invoice?.id ?? ''),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['issued-invoices', tenantId] })
      navigate(`/issued?tenantId=${encodeURIComponent(tenantId)}`, { replace: true })
      queryClient.removeQueries({ queryKey: ['issued-invoice', tenantId, invoice?.id] })
    },
    onError: (removeError) => {
      setDeleteOpen(false)
      return showFailure(removeError, 'Nie udało się usunąć szkicu')
    },
  })

  const issuing = prepareIssue.isPending || issue.isPending
  const busy = save.isPending || issuing || remove.isPending
  const expectedNumber = form.issueDate.startsWith(profile.nextNumberPeriod) ? profile.nextNumberPreview : null
  const noticeState = location.state as { companyNotice?: string; companyId?: string } | null
  const companyNotice = noticeState?.companyId === tenantId ? noticeState.companyNotice : undefined
  const totalText = formatMoney(totals.totalGross, form.currency)
  const isForeign = form.buyer.kind === 'Foreign'
  const title = invoice?.status === 'Rejected' ? `Popraw fakturę ${invoice.number ?? ''}`.trim() : invoice ? 'Szkic faktury' : 'Nowa faktura'
  const issueButton = (
    <button
      type="button"
      className="ui-button ui-button--primary ui-button--md inv-issue-button"
      disabled={busy || !profile.isComplete}
      onClick={(event) => {
        issueOpener.current = event.currentTarget
        const missingTaxId = missingTaxIdError(form)
        if (missingTaxId) {
          setErrors([missingTaxId])
          return
        }
        prepareIssue.mutate()
      }}
    >
      {issuing ? 'Wysyłanie…' : 'Wystaw i wyślij do KSeF'} <EnvBadge environment={profile.ksefEnvironment} />
    </button>
  )

  useEffect(() => {
    setSwitchGuard({
      dirty: JSON.stringify(form) !== baseline.current,
      busy,
      environment: profile.ksefEnvironment,
      save: () => save.mutateAsync(true),
    })
    return () => setSwitchGuard(null)
  })

  function onSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault()
    save.mutate(false)
  }

  const profileHref = `/company/invoice-data?tenantId=${encodeURIComponent(tenantId)}`
  const updateForm = useCallback((update: (current: DraftFormValue) => DraftFormValue) => setForm(update), [])

  return (
    <form onSubmit={onSubmit} noValidate className="inv-form" aria-labelledby="draft-title">
      <div className="inv-form__main">
        <div>
          <Link to={`/issued?tenantId=${encodeURIComponent(tenantId)}`} className="back-link">
            <Icon name="arrow-left" size={14} /> Wystawione
          </Link>
          <div className="inv-form__head">
            <h1 id="draft-title">{title}</h1>
            <KsefStatusBadge status={invoice?.status ?? 'Draft'} />
            <span className="inv-help" role="status">
              {invoice ? `Zapisano ${timeFormat.format(new Date(invoice.updatedAt))}` : ''}
            </span>
            {invoice?.status === 'Draft' ? (
              <button type="button" className="ui-button ui-button--danger-outline ui-button--sm inv-form__delete" disabled={busy} onClick={() => setDeleteOpen(true)}>
                Usuń szkic
              </button>
            ) : null}
          </div>
        </div>

        {companyNotice ? (
          <div className="inv-alert inv-alert--info" role="status">
            {companyNotice}
          </div>
        ) : null}
        {!profile.isComplete ? (
          <div className="inv-alert inv-alert--info" role="status">
            Uzupełnij dane firmy, aby wystawiać faktury. <Link to={profileHref}>Dane do faktur</Link>
          </div>
        ) : null}
        {failure ? <ErrorBanner message={failure} /> : null}
        <ValidationSummary errors={errors} warnings={warnings} />

        <BuyerSection value={form.buyer} fieldErrors={fieldErrors} onChange={(buyer) => setForm(withBuyer(form, buyer))} />
        <LinesTable
          lines={form.lines}
          currency={form.currency}
          rates={isForeign ? FOREIGN_VAT_RATES : undefined}
          newLineRate={isForeign ? foreignRateFor(form.buyer.countryCode) : undefined}
          fieldErrors={fieldErrors}
          onChange={(lines) => setForm({ ...form, lines })}
        >
          {isForeign ? <p className="inv-help">np I: nabywca spoza UE. np II: nabywca z UE (art. 100 ust. 1 pkt 4).</p> : null}
          {hasExemptLine(form.lines) ? (
            <ExemptionBasisField
              choice={form.exemptionChoice}
              text={form.exemptionText}
              serverError={fieldErrors.exemptionBasis}
              onChange={({ choice, text }) => setForm({ ...form, exemptionChoice: choice, exemptionText: text })}
            />
          ) : null}
        </LinesTable>
        <section className="inv-card" aria-labelledby="dates-title">
          <h2 id="dates-title">Daty i płatność</h2>
          <DatesSection
            issueDate={form.issueDate}
            saleDate={form.saleDate}
            dueDate={form.dueDate}
            fieldErrors={fieldErrors}
            onChange={(dates) => setForm({ ...form, ...dates })}
          />
          {isForeign || form.currency !== 'PLN' ? (
            <CurrencySection
              tenantId={tenantId}
              currency={form.currency}
              exchangeRate={form.exchangeRate}
              exchangeRateDate={form.exchangeRateDate}
              issueDate={form.issueDate}
              saleDate={form.saleDate}
              fieldErrors={fieldErrors}
              onUpdate={updateForm}
            />
          ) : null}
          <PaymentSection
            paymentMethod={form.paymentMethod}
            splitPayment={form.splitPayment}
            bankAccount={profile.bankAccount}
            foreignInvoice={isForeign}
            foreignAccount={isForeign ? foreignAccountOf(profile) : null}
            totalGross={totals.totalGross}
            profileHref={profileHref}
            onChange={(payment) => setForm({ ...form, ...payment })}
          />
        </section>
      </div>

      <aside className="inv-side" aria-label="Podsumowanie faktury">
        <SellerCard nip={profile.nip} name={profile.legalName} environment={profile.ksefEnvironment} />
        <div className="inv-card inv-totals">
          <div>
            <span className="inv-caption">Do zapłaty</span>
            <output className="inv-amount" data-testid="total-gross">
              {totalText}
            </output>
          </div>
          {invoice?.number ? (
            <p className="inv-help">
              Numer <span className="mono">{invoice.number}</span> zostaje — po poprawce wyślemy fakturę z tym samym numerem.
            </p>
          ) : (
            <p className="inv-help">
              Numer nadamy przy wystawieniu{expectedNumber ? ': ' : '.'}
              {expectedNumber ? <span className="mono">{expectedNumber}</span> : null}
            </p>
          )}
          <div className="inv-totals__actions">
            {issueButton}
            <button type="submit" className="ui-button ui-button--outline ui-button--md" disabled={busy}>
              {save.isPending ? 'Zapisywanie…' : 'Zapisz szkic'}
            </button>
          </div>
        </div>
        <VatSummary sticky rows={totals.vatSummary} totalNet={totals.totalNet} totalVat={totals.totalVat} totalGross={totals.totalGross} currency={form.currency} />
      </aside>

      <div className="inv-dock">
        <span className="inv-dock__seller">
          <b>{profile.legalName ?? '—'}</b> · NIP {profile.nip}
        </span>
        <span>
          <span className="inv-caption">Do zapłaty</span> <b>{totalText}</b>
        </span>
        {issueButton}
      </div>

      <ConfirmDialog
        open={deleteOpen}
        title="Usunąć szkic?"
        message="Szkic zostanie trwale usunięty. Tej operacji nie można cofnąć."
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
        onConfirm={() => confirmFor && issue.mutate(confirmFor)}
        onCancel={() => {
          setConfirmFor(null)
          issueOpener.current?.focus()
        }}
      >
        {confirmFor ? (
          <>
            <div className="company-switch-seller" data-testid="confirm-seller">
              <span className="company-switch-seller__caption">Sprzedawca</span>
              <b>{confirmFor.seller.name ?? '—'}</b>
              <span className="company-nip">NIP {confirmFor.seller.nip}</span> <EnvBadge environment={confirmFor.ksef.environment} />
              {confirmFor.seller.addressLine1 || confirmFor.seller.addressLine2 ? (
                <span className="inv-help company-switch-seller__address">{[confirmFor.seller.addressLine1, confirmFor.seller.addressLine2].filter(Boolean).join(', ')}</span>
              ) : null}
            </div>
            <dl className="inv-readonly">
              <dt>Numer</dt>
              <dd className="mono">{confirmFor.number ?? 'Nadamy przy wystawieniu'}</dd>
              <dt>Nabywca</dt>
              <dd>
                {confirmFor.buyer.name || '—'}
                {confirmFor.buyer.kind === 'Company' && confirmFor.buyer.nip ? <span className="mono"> · NIP {confirmFor.buyer.nip}</span> : null}
                {confirmFor.buyer.countryCode ? ` · ${countryName(confirmFor.buyer.countryCode)}` : null}
                {confirmFor.buyer.foreignId ? <span className="mono"> · {confirmFor.buyer.foreignId}</span> : null}
              </dd>
              <dt>Data wystawienia</dt>
              <dd>{formatDate(confirmFor.issueDate)}</dd>
              <dt>Do zapłaty</dt>
              <dd className="num" data-testid="confirm-total">
                <b>{formatMoney(confirmFor.totalGross, confirmFor.currency)}</b>
              </dd>
              {confirmFor.exchangeRate ? (
                <>
                  <dt>Kurs</dt>
                  <dd className="num">
                    1 {confirmFor.currency} = {formatDecimal(confirmFor.exchangeRate, 2)} PLN
                  </dd>
                </>
              ) : null}
            </dl>
          </>
        ) : null}
        <p className="inv-help">Korekt nie wystawisz jeszcze w OpenKSeF — użyj Aplikacji Podatnika KSeF.</p>
      </ConfirmDialog>
    </form>
  )
}
