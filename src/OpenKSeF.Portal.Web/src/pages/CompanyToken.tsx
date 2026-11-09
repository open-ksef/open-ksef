import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type FormEvent, type ReactElement } from 'react'
import toast from 'react-hot-toast'
import { Link, useLocation } from 'react-router-dom'

import { addOrUpdateCredential, deleteCredential, forceFullResync } from '@/api/endpoints/credentials'
import type { TenantResponse } from '@/api/types'
import { CompanyBox } from '@/company/CompanyBox'
import { useCompany } from '@/company/useCompany'
import { Button } from '@/components/Button'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { ErrorBanner } from '@/components/ErrorBanner'
import { Icon } from '@/components/Icon'
import { KsefSync } from '@/components/KsefSync'
import { Skeleton } from '@/components/Skeleton'
import { useCompanyState } from '@/hooks/useCompanyState'
import { useKsefFetch } from '@/hooks/useKsefFetch'
import { formatWarsawDate } from '@/invoicing/format'
import { notifyMutationError, notifyMutationSuccess } from '@/notifications'

export interface TokenReturnState {
  rejectedInvoice?: { id: string; number: string | null; tenantId: string }
}

const TOKEN_HELP = 'Token z uprawnieniem odczytu i zapisu, z Aplikacji Podatnika KSeF. Bez zapisu nie wystawisz faktur.'
const LONG_TOAST = { duration: 12_000 }
const FULL_RESYNC_TEXT = 'Pobiera brakujące pozycje, numer rachunku i termin. Niczego nie usuwa, oznaczenia opłacenia zostają. KSeF pozwala na około 16 plików na minutę, dlatego przy większej liczbie faktur uruchom ją ponownie później.'

export function CompanyTokenPage(): ReactElement {
  const { company } = useCompany()
  return company ? <TokenTab key={company.id} company={company} /> : <Skeleton lines={6} />
}

function TokenTab({ company }: { company: TenantResponse }): ReactElement {
  const tenantId = company.id
  const location = useLocation()
  const queryClient = useQueryClient()
  const { setSwitchGuard } = useCompany()
  const { state, summary, credential, error, refetch } = useCompanyState()
  const ksef = useKsefFetch(tenantId)
  const [token, setToken] = useState('')
  const [formError, setFormError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [pending, setPending] = useState<'resync' | 'delete' | null>(null)

  const returnTo = (location.state as TokenReturnState | null)?.rejectedInvoice
  const rejected = returnTo?.tenantId === tenantId ? returnTo : null
  const hasCredential = credential?.hasCredential ?? false
  const isCertificate = hasCredential && credential?.credentialType === 'Certificate'

  const refreshCredentials = async () => {
    await queryClient.invalidateQueries({ queryKey: ['credentials'] })
    await queryClient.invalidateQueries({ queryKey: ['dashboard'] })
  }

  const save = useMutation({
    mutationFn: (value: string) => addOrUpdateCredential(tenantId, value),
    onSuccess: async () => {
      notifyMutationSuccess('saved', 'Credential')
      setToken('')
      setSaved(true)
      await refreshCredentials()
    },
    onError: (mutationError) => notifyMutationError('save', 'Credential', mutationError),
  })

  const remove = useMutation({
    mutationFn: () => deleteCredential(tenantId),
    onSuccess: async () => {
      notifyMutationSuccess('deleted', 'Credential')
      await refreshCredentials()
    },
    onError: (mutationError) => notifyMutationError('delete', 'Credential', mutationError),
    onSettled: () => setPending(null),
  })

  const fullResync = useMutation({
    mutationFn: () => forceFullResync(tenantId),
    onSuccess: async (result) => {
      const counts = `Pobrane: ${result.fetchedInvoices}, nowe: ${result.newInvoices}.`
      const later = 'Nieopłacone uzupełnimy automatycznie, opłacone przy kolejnej pełnej resynchronizacji.'
      if (result.erroredDownloads) {
        const queued = result.queuedDownloads ? `, ${result.queuedDownloads} w kolejce` : ''
        toast.error(`Pełna resynchronizacja niepełna. ${counts} Bez szczegółów z KSeF: ${result.erroredDownloads} z błędem${queued}. ${later}`, LONG_TOAST)
      } else if (result.erroredDownloads === undefined && result.queuedDownloads === undefined && result.failedDownloads) {
        toast.error(`Pełna resynchronizacja niepełna. ${counts} Bez szczegółów z KSeF (liczba: ${result.failedDownloads}). ${later}`, LONG_TOAST)
      } else if (result.queuedDownloads) {
        toast.success(`Pełna resynchronizacja zakończona. ${counts} Szczegóły pozostałych faktur (liczba: ${result.queuedDownloads}) pobierzemy w kolejnych cyklach, a opłaconych przy kolejnej pełnej resynchronizacji.`, LONG_TOAST)
      } else {
        toast.success(`Pełna resynchronizacja zakończona. ${counts}`)
      }
      await refreshCredentials()
      await queryClient.invalidateQueries({ queryKey: ['invoices'] })
      await queryClient.invalidateQueries({ queryKey: ['transfer-details', tenantId] })
    },
    onError: (mutationError) => notifyMutationError('save', 'Credential', mutationError),
    onSettled: () => setPending(null),
  })

  useEffect(() => {
    setSwitchGuard({
      dirty: token.trim() !== '',
      busy: save.isPending || remove.isPending || fullResync.isPending,
      environment: null,
      save: () => save.mutateAsync(token.trim()),
    })
    return () => setSwitchGuard(null)
  })

  function onSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault()
    setFormError(null)
    if (!token.trim()) {
      setFormError('Token jest wymagany')
      return
    }
    setSaved(false)
    save.mutate(token.trim())
  }

  const removeLabel = isCertificate ? 'certyfikat KSeF' : 'token KSeF'

  return (
    <>
      {rejected && saved ? (
        <div className="inv-alert inv-alert--success company-note" role="status" data-testid="token-saved-return">
          <Icon name="circle-check" />
          <span>
            Token zapisany.{' '}
            <Link to={`/issued/${encodeURIComponent(rejected.id)}?tenantId=${encodeURIComponent(tenantId)}`}>
              Wróć do faktury {rejected.number ?? 'bez numeru'}
            </Link>
          </span>
        </div>
      ) : null}
      {rejected && !saved ? (
        <div className="inv-alert inv-alert--info company-note" role="status">
          <Icon name="key-round" />
          <span>Podmieniasz token po odrzuceniu faktury {rejected.number ?? 'bez numeru'}. Token musi mieć uprawnienie do zapisu.</span>
        </div>
      ) : null}
      {error ? <ErrorBanner message={error instanceof Error ? error.message : 'Nie udało się załadować danych'} onRetry={refetch} /> : null}

      <div className="company-grid">
        <section className="ui-card" aria-labelledby="token-title">
          <h2 id="token-title">Token KSeF</h2>
          {hasCredential ? (
            <div className="company-tokbox">
              <Icon name="key-round" />
              <span data-testid="credential-type-badge" className={`credential-type-badge credential-type-badge--${isCertificate ? 'certificate' : 'token'}`}>
                {isCertificate ? 'Certyfikat' : 'Token'}
              </span>
              <span>
                {isCertificate ? 'Certyfikat' : 'Token'} zapisany. Ostatnia aktualizacja: {formatWarsawDate(credential?.lastUpdatedAt)}
              </span>
            </div>
          ) : credential ? (
            <p className="company-muted">Brak tokenu dla tej firmy.</p>
          ) : null}
          {isCertificate ? (
            <p className="inv-alert inv-alert--info" data-testid="credential-certificate-hint">
              Ta firma używa certyfikatu. Pobieranie faktur działa dalej, ale wysyłka faktur do KSeF wymaga tokenu. Wklej token poniżej: zastąpi certyfikat na stałe, a w portalu nie wgrasz go ponownie.
            </p>
          ) : null}
          <form onSubmit={onSubmit} noValidate>
            <div className="ui-form-group">
              <label htmlFor="credential-token-input">{hasCredential ? 'Nowy token KSeF' : 'Token KSeF'}</label>
              <input
                id="credential-token-input"
                data-testid="credential-token-input"
                className="mono"
                type="password"
                autoComplete="new-password"
                placeholder="Wklej token uwierzytelniający KSeF tutaj..."
                value={token}
                onChange={(event) => setToken(event.target.value)}
              />
              <span className="ui-form-hint" data-testid="credential-token-help">
                {TOKEN_HELP}
              </span>
            </div>
            {formError ? (
              <div className="ui-form-error" role="alert">
                {formError}
              </div>
            ) : null}
            <Button type="submit" data-testid="credential-submit-button" disabled={save.isPending}>
              {save.isPending ? 'Zapisywanie…' : hasCredential ? 'Zapisz token' : 'Dodaj token'}
            </Button>
          </form>
        </section>

        <section className="ui-card company-fetch" aria-labelledby="fetch-title">
          <h2 id="fetch-title">Pobieranie z KSeF</h2>
          {credential && !hasCredential ? <p className="company-muted">Pobieranie ruszy po dodaniu tokenu.</p> : null}
          <KsefSync state={state} summary={summary} ksef={ksef} disabled={!hasCredential} variant="primary" />
          <p className="ui-form-hint">Pierwsze pobranie obejmuje ostatnie 6 miesięcy.</p>
        </section>

        {hasCredential ? (
          <section className="ui-card ui-card--danger" aria-labelledby="token-danger-title">
            <h2 id="token-danger-title">Działania ryzykowne</h2>
            <ul className="danger-list">
              <li>
                <span>
                  <b>Pełna resynchronizacja</b>
                  <span>{FULL_RESYNC_TEXT}</span>
                </span>
                <button
                  type="button"
                  className="ui-button ui-button--danger-outline ui-button--md"
                  data-testid="credential-full-resync-button"
                  disabled={fullResync.isPending || ksef.pending}
                  onClick={() => setPending('resync')}
                >
                  Pełna resynchronizacja
                </button>
              </li>
              <li>
                <span>
                  <b>Usuń {removeLabel}</b>
                  <span>Pobieranie faktur i wysyłanie ich do KSeF przestaną działać do czasu dodania nowego tokenu.</span>
                </span>
                <button
                  type="button"
                  className="ui-button ui-button--danger-outline ui-button--md"
                  data-testid="credential-delete-button"
                  onClick={() => setPending('delete')}
                >
                  {isCertificate ? 'Usuń certyfikat' : 'Usuń token'}
                </button>
              </li>
            </ul>
          </section>
        ) : null}
      </div>

      <ConfirmDialog
        open={pending === 'delete'}
        title={`Usunąć ${removeLabel}?`}
        message={`${isCertificate ? 'Certyfikat KSeF' : 'Token KSeF'} tej firmy zostanie trwale usunięty. Pobieranie faktur i wysyłanie ich do KSeF przestaną działać do czasu dodania nowego tokenu.`}
        confirmLabel={`Usuń ${removeLabel}`}
        variant="danger"
        isPending={remove.isPending}
        onConfirm={() => remove.mutate()}
        onCancel={() => setPending(null)}
      >
        <CompanyBox caption="Firma" company={company} />
      </ConfirmDialog>

      <ConfirmDialog
        open={pending === 'resync'}
        title="Wykonać pełną resynchronizację?"
        message={FULL_RESYNC_TEXT}
        confirmLabel="Wykonaj pełną resynchronizację"
        pendingLabel="Trwa resynchronizacja…"
        variant="danger"
        isPending={fullResync.isPending}
        onConfirm={() => fullResync.mutate()}
        onCancel={() => setPending(null)}
      >
        <CompanyBox caption="Firma" company={company} />
      </ConfirmDialog>
    </>
  )
}
