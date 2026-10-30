import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent, type ReactElement } from 'react'
import { Link, Navigate, useNavigate, useParams, useSearchParams } from 'react-router-dom'

import { createIssuedInvoice, getIssuedInvoice, issueIssuedInvoice, updateIssuedInvoice } from '@/api/endpoints/issuedInvoices'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { readValidationEnvelope } from '@/api/errors'
import type { IssuedInvoiceResponse, IssuedInvoiceStatus, SellerProfileResponse, ValidationIssue } from '@/api/types'
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
import { computeTotals } from '@/invoicing/totals'

const EDITABLE: IssuedInvoiceStatus[] = ['Draft', 'Rejected']

export function InvoiceDraftPage(): ReactElement {
  const { id } = useParams()
  const [searchParams] = useSearchParams()
  const tenantsQuery = useTenants()
  const tenantId = searchParams.get('tenantId') || tenantsQuery.data?.[0]?.id || ''

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
        <Navigate to={`/seller-profile?tenantId=${encodeURIComponent(tenantId)}&next=new`} replace />
      ) : invoiceQuery.data && !EDITABLE.includes(invoiceQuery.data.status) ? (
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

function DraftForm({ tenantId, profile, invoice }: DraftFormProps): ReactElement {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [form, setForm] = useState<DraftFormValue>(() => (invoice ? fromResponse(invoice) : emptyDraft()))
  const [errors, setErrors] = useState<ValidationIssue[]>([])
  const [warnings, setWarnings] = useState<ValidationIssue[]>(invoice?.warnings ?? [])
  const [failure, setFailure] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)

  const totals = computeTotals(
    form.lines.map((line) => ({
      quantity: parseDecimal(line.quantity),
      unitNetPrice: parseDecimal(line.unitNetPrice),
      vatRate: line.vatRate,
    })),
  )
  const fieldErrors = toFieldErrors(errors)

  async function saveDraft(): Promise<IssuedInvoiceResponse> {
    const request = toDraftRequest(form)
    const saved = invoice ? await updateIssuedInvoice(tenantId, invoice.id, request) : await createIssuedInvoice(tenantId, request)
    setErrors([])
    setFailure(null)
    setWarnings(saved.warnings)
    queryClient.setQueryData(['issued-invoice', tenantId, saved.id], saved)
    void queryClient.invalidateQueries({ queryKey: ['issued-invoices', tenantId] })
    if (!invoice) {
      navigate(`/issued/${encodeURIComponent(saved.id)}/edit?tenantId=${encodeURIComponent(tenantId)}`, { replace: true })
    }
    return saved
  }

  async function showFailure(failed: unknown, fallback: string): Promise<void> {
    const envelope = await readValidationEnvelope(failed)
    setErrors(envelope?.errors ?? [])
    setWarnings(envelope?.warnings ?? [])
    setFailure(envelope ? null : failed instanceof Error ? failed.message : fallback)
  }

  const save = useMutation({
    mutationFn: saveDraft,
    onError: (saveError) => showFailure(saveError, 'Nie udało się zapisać szkicu'),
  })

  const issue = useMutation({
    mutationFn: async () => issueIssuedInvoice(tenantId, (await saveDraft()).id),
    onSuccess: (issued) => {
      queryClient.setQueryData(['issued-invoice', tenantId, issued.id], issued)
      navigate(`/issued/${encodeURIComponent(issued.id)}?tenantId=${encodeURIComponent(tenantId)}`)
    },
    onError: (issueError) => {
      setConfirmOpen(false)
      return showFailure(issueError, 'Nie udało się wysłać faktury do KSeF')
    },
  })

  const busy = save.isPending || issue.isPending

  function onSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault()
    save.mutate()
  }

  const profileHref = `/seller-profile?tenantId=${encodeURIComponent(tenantId)}`

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
          <button type="submit" className="ui-button ui-button--outline ui-button--md" disabled={busy}>
            {save.isPending ? 'Zapisywanie…' : 'Zapisz szkic'}
          </button>
          <button
            type="button"
            className="ui-button ui-button--primary ui-button--md"
            disabled={busy || !profile.isComplete}
            onClick={() => (profile.ksefEnvironment === 'production' ? setConfirmOpen(true) : issue.mutate())}
          >
            {issue.isPending ? 'Wysyłanie…' : 'Wystaw i wyślij do KSeF'}
          </button>
        </div>
      </header>

      <ConfirmDialog
        open={confirmOpen}
        title="Wystawić fakturę i wysłać do KSeF?"
        message="Środowisko PRODUKCJA: po przyjęciu przez KSeF faktura staje się dokumentem prawnym. Nie można jej usunąć ani edytować."
        confirmLabel="Wystaw i wyślij"
        cancelLabel="Wróć do edycji"
        pendingLabel="Wysyłanie…"
        isPending={issue.isPending}
        onConfirm={() => issue.mutate()}
        onCancel={() => setConfirmOpen(false)}
      >
        <dl className="inv-readonly">
          <dt>Numer</dt>
          <dd className="mono">{invoice?.number ?? profile.nextNumberPreview}</dd>
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
          <p className="inv-help">
            Numer nadamy przy wystawieniu: <span className="mono">{invoice?.number ?? profile.nextNumberPreview}</span>
          </p>
        </aside>
      </div>
    </form>
  )
}
