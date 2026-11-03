import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState, type FormEvent, type ReactElement } from 'react'
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
import { Skeleton } from '@/components/Skeleton'
import { BuyerSection } from '@/components/invoicing/BuyerSection'
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
  parseDecimal,
  toDraftRequest,
  toFieldErrors,
  type DraftFormValue,
} from '@/invoicing/form'
import { formatDate, formatMoney } from '@/invoicing/format'
import { isReissueBlocked } from '@/invoicing/status'
import { computeTotals } from '@/invoicing/totals'

const EDITABLE: IssuedInvoiceStatus[] = ['Draft', 'Rejected']

export function InvoiceDraftPage(): ReactElement {
  const { id } = useParams()
  const tenantsQuery = useTenants()
  const { tenantId } = useCompany()

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
          key={tenantId}
          tenantId={tenantId}
          profile={profileQuery.data}
          invoice={invoiceQuery.data}
        />
      )}
    </div>
  )
}

interface DraftFormProps {
  tenantId: string
  profile: SellerProfileResponse
  invoice?: IssuedInvoiceResponse
}

const timeFormat = new Intl.DateTimeFormat('pl-PL', { hour: '2-digit', minute: '2-digit' })

const failureText = (error: unknown, fallback: string): string =>
  !(error instanceof Error) || (error instanceof ApiError && (error.status >= 500 || !(error.originalError instanceof Response) || !error.originalError.headers.get('Content-Type')?.includes('application/json')))
    ? fallback
    : error.message

function DraftForm({ tenantId, profile, invoice }: DraftFormProps): ReactElement {
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
    const request = toDraftRequest(form)
    const saved = invoice ? await updateIssuedInvoice(tenantId, invoice.id, request) : await createIssuedInvoice(tenantId, request)
    baseline.current = snapshot
    setErrors([])
    setFailure(null)
    setWarnings(saved.warnings)
    queryClient.setQueryData(['issued-invoice', tenantId, saved.id], saved)
    void queryClient.invalidateQueries({ queryKey: ['issued-invoices', tenantId] })
    if (!invoice && !stay) {
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

  return (
    <form onSubmit={onSubmit} noValidate className="inv-stack" aria-labelledby="draft-title">
      <header className="inv-header">
        <Link to={`/issued?tenantId=${encodeURIComponent(tenantId)}`} className="back-link">
          ← Wystawione
        </Link>
        <h1 id="draft-title">{invoice ? 'Edycja szkicu' : 'Nowa faktura'}</h1>
        <KsefStatusBadge status={invoice?.status ?? 'Draft'} />
        <EnvBadge environment={profile.ksefEnvironment} />
        <span className="inv-help" role="status">
          {invoice ? `Zapisano ${timeFormat.format(new Date(invoice.updatedAt))}` : ''}
        </span>
        <div className="inv-header__actions">
          {invoice?.status === 'Draft' ? (
            <button
              type="button"
              className="ui-button ui-button--ghost ui-button--md"
              disabled={busy}
              onClick={() => setDeleteOpen(true)}
            >
              Usuń szkic
            </button>
          ) : null}
          <button type="submit" className="ui-button ui-button--outline ui-button--md" disabled={busy}>
            {save.isPending ? 'Zapisywanie…' : 'Zapisz szkic'}
          </button>
          <button
            type="button"
            className="ui-button ui-button--primary ui-button--md"
            disabled={busy || !profile.isComplete}
            onClick={() => prepareIssue.mutate()}
          >
            {issuing ? 'Wysyłanie…' : 'Wystaw i wyślij do KSeF'}
          </button>
        </div>
      </header>

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
        onCancel={() => setConfirmFor(null)}
      >
        <dl className="inv-readonly">
          <dt>Numer</dt>
          <dd className="mono">{confirmFor?.number ?? 'Nadamy przy wystawieniu'}</dd>
          <dt>Nabywca</dt>
          <dd>
            {form.buyer.name || '—'}
            {form.buyer.kind === 'Company' && form.buyer.nip ? <span className="mono"> · NIP {form.buyer.nip}</span> : null}
          </dd>
          <dt>Data wystawienia</dt>
          <dd>{formatDate(form.issueDate)}</dd>
          <dt>Do zapłaty</dt>
          <dd className="num">{formatMoney(totals.totalGross)}</dd>
        </dl>
        <p className="inv-help">Korekt nie wystawisz jeszcze w OpenKSeF — użyj Aplikacji Podatnika KSeF.</p>
      </ConfirmDialog>

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

      <div className="inv-columns">
        <div className="inv-columns__main">
          <BuyerSection value={form.buyer} fieldErrors={fieldErrors} onChange={(buyer) => setForm({ ...form, buyer })} />
          <DatesSection
            issueDate={form.issueDate}
            saleDate={form.saleDate}
            fieldErrors={fieldErrors}
            onChange={(dates) => setForm({ ...form, ...dates })}
          />
          <LinesTable lines={form.lines} fieldErrors={fieldErrors} onChange={(lines) => setForm({ ...form, lines })}>
            {hasExemptLine(form.lines) ? (
              <ExemptionBasisField
                choice={form.exemptionChoice}
                text={form.exemptionText}
                serverError={fieldErrors.exemptionBasis}
                onChange={({ choice, text }) => setForm({ ...form, exemptionChoice: choice, exemptionText: text })}
              />
            ) : null}
          </LinesTable>
          <PaymentSection
            paymentMethod={form.paymentMethod}
            dueDate={form.dueDate}
            issueDate={form.issueDate}
            splitPayment={form.splitPayment}
            bankAccount={profile.bankAccount}
            totalGross={totals.totalGross}
            fieldErrors={fieldErrors}
            onChange={(payment) => setForm({ ...form, ...payment })}
          />
        </div>
        <aside className="inv-columns__side" aria-label="Podsumowanie faktury">
          <SellerCard
            nip={profile.nip}
            name={profile.legalName}
            addressLine1={profile.addressLine1}
            addressLine2={profile.addressLine2}
            bankAccount={profile.bankAccount}
            editHref={profileHref}
          />
          <VatSummary
            rows={totals.vatSummary}
            totalNet={totals.totalNet}
            totalVat={totals.totalVat}
            totalGross={totals.totalGross}
          />
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
        </aside>
      </div>
    </form>
  )
}
