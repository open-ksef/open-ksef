import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type FormEvent, type ReactElement } from 'react'
import toast from 'react-hot-toast'
import { useNavigate, useSearchParams } from 'react-router-dom'

import { listIssuedFeed } from '@/api/endpoints/issuedInvoices'
import { createTenant, deleteTenant, updateTenant } from '@/api/endpoints/tenants'
import { ApiError } from '@/api/errors'
import type { TenantResponse } from '@/api/types'
import { CompanyBox } from '@/company/CompanyBox'
import { useCompany } from '@/company/useCompany'
import { Button } from '@/components/Button'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { Skeleton } from '@/components/Skeleton'
import { formatWarsawDate } from '@/invoicing/format'
import { notifyMutationError, notifyMutationSuccess } from '@/notifications'

const NIP_ERROR = 'NIP musi zawierać dokładnie 10 cyfr'
const EMAIL_ERROR = 'Adres e-mail do powiadomień jest nieprawidłowy'
const isBadEmail = (email: string): boolean => Boolean(email) && !/^\S+@\S+\.\S+$/.test(email)

function issuedInvoicesNotice(count: number | undefined, countFailed: boolean): string {
  if (count === 0) {
    return 'Firma nie ma wystawionych faktur ani szkiców.'
  }

  const refusal = 'Firmy nie usuniemy, jeśli faktury wystawione w OpenKSeF są w trakcie wysyłki do KSeF lub zostały przyjęte przez produkcyjny KSeF. Faktury spoza OpenKSeF usuniemy tylko z tej aplikacji: w KSeF zostają.'
  if (count !== undefined) {
    return `Usuniemy też wystawione faktury i szkice tej firmy (liczba: ${count}). Nie będzie można ich przywrócić. ${refusal}`
  }

  const check = countFailed ? 'Nie udało się sprawdzić liczby wystawionych faktur i szkiców.' : 'Sprawdzamy liczbę wystawionych faktur i szkiców.'
  return `${check} Jeśli firma je ma, też je usuniemy i nie będzie można ich przywrócić. ${refusal}`
}

export function CompanyDetailsPage(): ReactElement {
  const { company } = useCompany()
  const [searchParams, setSearchParams] = useSearchParams()
  const adding = searchParams.get('add') === '1'

  const closeAdd = (): void => {
    const next = new URLSearchParams(searchParams)
    next.delete('add')
    setSearchParams(next, { replace: true })
  }

  return (
    <>
      {company ? <DetailsTab key={company.id} company={company} /> : <Skeleton lines={6} />}
      {adding ? <AddCompanyDialog onClose={closeAdd} /> : null}
    </>
  )
}

function DetailsTab({ company }: { company: TenantResponse }): ReactElement {
  const queryClient = useQueryClient()
  const { setSwitchGuard } = useCompany()
  const [displayName, setDisplayName] = useState(company.displayName ?? '')
  const [email, setEmail] = useState(company.notificationEmail ?? '')
  const [formError, setFormError] = useState<string | null>(null)
  const [deleteOpen, setDeleteOpen] = useState(false)

  const countQuery = useQuery({
    queryKey: ['tenant-delete-count', company.id],
    queryFn: () => listIssuedFeed(company.id, { pageSize: 1 }),
    enabled: deleteOpen,
    retry: false,
    gcTime: 0,
  })

  const update = useMutation({
    mutationFn: () => updateTenant(company.id, { displayName: displayName || null, notificationEmail: email || null }),
    onSuccess: async () => {
      notifyMutationSuccess('updated', 'Tenant')
      await queryClient.invalidateQueries({ queryKey: ['tenants'] })
    },
    onError: (error) => notifyMutationError('update', 'Tenant', error),
  })

  const remove = useMutation({
    mutationFn: () => deleteTenant(company.id),
    onSuccess: () => notifyMutationSuccess('deleted', 'Tenant'),
    onError: (error) => {
      if (error instanceof ApiError && error.status === 409) {
        toast.error(error.message, { duration: 10_000 })
        return
      }
      notifyMutationError('delete', 'Tenant', error instanceof ApiError && error.status === 404 ? 'Firma już nie istnieje.' : error)
    },
    onSettled: () => {
      setDeleteOpen(false)
      void queryClient.invalidateQueries({ queryKey: ['tenants'] })
      void queryClient.invalidateQueries({ queryKey: ['credentials'] })
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      void queryClient.invalidateQueries({ queryKey: ['onboarding-status'] })
    },
  })

  const dirty = displayName !== (company.displayName ?? '') || email !== (company.notificationEmail ?? '')

  useEffect(() => {
    setSwitchGuard({
      dirty,
      busy: update.isPending || remove.isPending,
      environment: null,
      save: async () => {
        if (isBadEmail(email)) {
          setFormError(EMAIL_ERROR)
          throw new Error(EMAIL_ERROR)
        }
        await update.mutateAsync()
      },
    })
    return () => setSwitchGuard(null)
  })

  function onSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault()
    setFormError(isBadEmail(email) ? EMAIL_ERROR : null)
    if (!isBadEmail(email)) update.mutate()
  }

  return (
    <div className="company-grid">
      <section className="ui-card" aria-labelledby="company-details-title">
        <h2 id="company-details-title">Dane firmy</h2>
        <form onSubmit={onSubmit} noValidate>
          <div className="ui-form-group">
            <label htmlFor="company-nip">NIP</label>
            <input id="company-nip" className="mono nip-input" type="text" value={company.nip} readOnly />
          </div>
          <div className="ui-form-group">
            <label htmlFor="company-display-name">Nazwa wyświetlana</label>
            <input
              id="company-display-name"
              data-testid="company-display-name"
              type="text"
              value={displayName}
              onChange={(event) => setDisplayName(event.target.value)}
            />
          </div>
          <div className="ui-form-group">
            <label htmlFor="company-email">E-mail do powiadomień</label>
            <input
              id="company-email"
              aria-describedby="company-email-hint"
              data-testid="company-notification-email"
              type="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
            <span className="ui-form-hint" id="company-email-hint">E-mail wysyłamy tylko wtedy, gdy administrator serwera skonfigurował pocztę.</span>
          </div>
          <p className="ui-form-hint">Data dodania: {formatWarsawDate(company.createdAt)}</p>
          {formError ? (
            <div className="ui-form-error" role="alert">
              {formError}
            </div>
          ) : null}
          <Button type="submit" data-testid="company-save" disabled={update.isPending}>
            {update.isPending ? 'Zapisywanie…' : 'Zapisz firmę'}
          </Button>
        </form>
      </section>

      <section className="ui-card ui-card--danger" aria-labelledby="company-danger-title">
        <h2 id="company-danger-title">Działania ryzykowne</h2>
        <ul className="danger-list">
          <li>
            <span>
              <b>Usuń firmę</b>
              <span>Operacja nieodwracalna. Usuwa token, faktury otrzymane i wystawione oraz dane do faktur.</span>
            </span>
            <button
              type="button"
              className="ui-button ui-button--danger-outline ui-button--md"
              data-testid="tenant-delete-button"
              onClick={() => setDeleteOpen(true)}
            >
              Usuń firmę
            </button>
          </li>
        </ul>
      </section>

      <ConfirmDialog
        open={deleteOpen}
        title="Usunąć firmę?"
        message="Ta operacja jest nieodwracalna. Usuniemy token KSeF, faktury otrzymane z oznaczeniami opłacenia, dane do faktur z rachunkiem bankowym i dane synchronizacji. Po ponownym dodaniu firmy numeracja zacznie się od 1."
        confirmLabel="Usuń firmę"
        variant="danger"
        isPending={remove.isPending}
        onConfirm={() => remove.mutate()}
        onCancel={() => setDeleteOpen(false)}
      >
        <CompanyBox caption="Firma" company={company} />
        <p className="company-notice" data-testid="tenant-delete-invoices" role="status">
          {issuedInvoicesNotice(countQuery.data?.totalCount, countQuery.isError)}
        </p>
      </ConfirmDialog>
    </div>
  )
}

function AddCompanyDialog({ onClose }: { onClose: () => void }): ReactElement {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [nip, setNip] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const [formError, setFormError] = useState<string | null>(null)

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  const create = useMutation({
    mutationFn: createTenant,
    onSuccess: async (created) => {
      notifyMutationSuccess('created', 'Tenant')
      await queryClient.invalidateQueries({ queryKey: ['tenants'] })
      await queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      await queryClient.invalidateQueries({ queryKey: ['credentials'] })
      void navigate(`/company/token?tenantId=${encodeURIComponent(created.id)}`)
    },
    onError: (error) => notifyMutationError('create', 'Tenant', error),
  })

  function onSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault()
    const error = !/^\d{10}$/.test(nip) ? NIP_ERROR : isBadEmail(email) ? EMAIL_ERROR : null
    setFormError(error)
    if (!error) create.mutate({ nip, displayName: displayName || null, notificationEmail: email || null })
  }

  return (
    <div
      className="ui-modal-backdrop"
      onClick={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <form className="ui-modal" role="dialog" aria-modal="true" aria-labelledby="add-company-title" onSubmit={onSubmit} noValidate>
        <div className="ui-modal-header">
          <h2 className="ui-modal-title" id="add-company-title">
            Dodaj firmę
          </h2>
        </div>
        <div className="ui-modal-body">
          <div className="ui-form-group">
            <label htmlFor="tenant-form-nip">NIP</label>
            <input
              id="tenant-form-nip"
              data-testid="tenant-form-nip"
              className="mono nip-input"
              type="text"
              inputMode="numeric"
              maxLength={10}
              autoFocus
              value={nip}
              onChange={(event) => setNip(event.target.value)}
            />
          </div>
          <div className="ui-form-group">
            <label htmlFor="tenant-form-display-name">Nazwa wyświetlana</label>
            <input
              id="tenant-form-display-name"
              data-testid="tenant-form-display-name"
              type="text"
              value={displayName}
              onChange={(event) => setDisplayName(event.target.value)}
            />
          </div>
          <div className="ui-form-group">
            <label htmlFor="tenant-form-notification-email">E-mail do powiadomień</label>
            <input
              id="tenant-form-notification-email"
              aria-describedby="tenant-form-notification-email-hint"
              data-testid="tenant-form-notification-email"
              type="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
            <span className="ui-form-hint" id="tenant-form-notification-email-hint">E-mail wysyłamy tylko wtedy, gdy administrator serwera skonfigurował pocztę.</span>
          </div>
          {formError ? (
            <div className="ui-form-error" role="alert">
              {formError}
            </div>
          ) : null}
        </div>
        <div className="ui-modal-footer">
          <Button variant="outline" onClick={onClose}>
            Anuluj
          </Button>
          <Button type="submit" data-testid="tenant-form-submit" disabled={create.isPending}>
            {create.isPending ? 'Zapisywanie…' : 'Zapisz firmę'}
          </Button>
        </div>
      </form>
    </div>
  )
}
