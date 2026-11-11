import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Fragment, useEffect, useState, type FormEvent, type ReactElement } from 'react'
import toast from 'react-hot-toast'
import { Link, useLocation } from 'react-router-dom'

import {
  addOrUpdateCertificate,
  addOrUpdateCredential,
  deleteCredential,
  forceFullResync,
  getCredentialStatus,
  type CertificateUpload,
} from '@/api/endpoints/credentials'
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
const CERTIFICATE_HELP = 'Certyfikat typu Uwierzytelnianie z Aplikacji Podatnika KSeF 2.0. Użyj go, gdy nie masz tokenu.'
const EXPIRY_WARNING_MS = 30 * 24 * 60 * 60 * 1000
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
  const [chosenMethod, setMethod] = useState<'Token' | 'Certificate' | null>(null)
  const [token, setToken] = useState('')
  const [certFile, setCertFile] = useState<File | null>(null)
  const [keyFile, setKeyFile] = useState<File | null>(null)
  const [certPassword, setCertPassword] = useState('')
  const [certFormKey, setCertFormKey] = useState(0)
  const [formError, setFormError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [pending, setPending] = useState<'resync' | 'delete' | null>(null)

  const returnTo = (location.state as TokenReturnState | null)?.rejectedInvoice
  const rejected = returnTo?.tenantId === tenantId ? returnTo : null
  const hasCredential = credential?.hasCredential ?? false
  const isCertificate = hasCredential && credential?.credentialType === 'Certificate'
  const method = chosenMethod ?? (isCertificate ? 'Certificate' : 'Token')
  const isCrt = /\.crt$/i.test(certFile?.name ?? '')
  const status = useQuery({
    queryKey: ['credentials', tenantId, 'status'],
    queryFn: () => getCredentialStatus(tenantId),
    enabled: isCertificate,
  })

  const refreshCredentials = async () => {
    await queryClient.invalidateQueries({ queryKey: ['credentials'] })
    await queryClient.invalidateQueries({ queryKey: ['dashboard'] })
  }

  const save = useMutation({
    mutationFn: (input: string | CertificateUpload) =>
      typeof input === 'string' ? addOrUpdateCredential(tenantId, input) : addOrUpdateCertificate(tenantId, input),
    onSuccess: async (_result, input) => {
      if (typeof input === 'string') {
        notifyMutationSuccess('saved', 'Credential')
        setToken('')
      } else {
        toast.success('Certyfikat KSeF zapisany pomyślnie')
        setCertFile(null)
        setKeyFile(null)
        setCertPassword('')
        setCertFormKey((value) => value + 1)
      }
      setSaved(true)
      await refreshCredentials()
    },
    onError: (mutationError, input) => notifyMutationError('save', typeof input === 'string' ? 'Credential' : 'Certificate', mutationError),
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

  const certificateInput = (): CertificateUpload | string => {
    if (!certFile) return 'Wybierz plik certyfikatu'
    if (isCrt && !keyFile) return 'Do pliku .crt dodaj klucz prywatny (.key)'
    if (!certPassword) return 'Hasło jest wymagane'
    return { file: certFile, key: isCrt ? keyFile : null, password: certPassword }
  }

  useEffect(() => {
    const input = method === 'Token' ? token.trim() : certificateInput()
    setSwitchGuard({
      dirty: method === 'Token' ? token.trim() !== '' : Boolean(certFile || keyFile || certPassword),
      busy: save.isPending || remove.isPending || fullResync.isPending,
      environment: null,
      save: () => (method === 'Certificate' && typeof input === 'string' ? Promise.reject(new Error(input)) : save.mutateAsync(input)),
    })
    return () => setSwitchGuard(null)
  })

  function onSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault()
    setFormError(null)
    const input = method === 'Token' ? token.trim() : certificateInput()
    if (method === 'Token' && !input) {
      setFormError('Token jest wymagany')
      return
    }
    if (method === 'Certificate' && typeof input === 'string') {
      setFormError(input)
      return
    }
    setSaved(false)
    save.mutate(input)
  }

  const validTo = status.data?.certificateValidTo
  const expiresIn = validTo ? Date.parse(validTo) - status.dataUpdatedAt : null

  const removeLabel = isCertificate ? 'certyfikat KSeF' : 'token KSeF'

  return (
    <>
      {rejected && saved ? (
        <div className="inv-alert inv-alert--success company-note" role="status" data-testid="token-saved-return">
          <Icon name="circle-check" />
          <span>
            {method === 'Certificate' ? 'Certyfikat' : 'Token'} zapisany.{' '}
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
          <h2 id="token-title">Dostęp do KSeF</h2>
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
            <p className="company-muted">Brak tokenu ani certyfikatu dla tej firmy.</p>
          ) : null}
          {isCertificate && status.isSuccess ? (
            expiresIn === null ? (
              <p className="inv-alert inv-alert--error" role="alert" data-testid="certificate-validity">
                Nie można odczytać zapisanego certyfikatu. Wgraj go ponownie.
              </p>
            ) : expiresIn <= 0 ? (
              <p className="inv-alert inv-alert--error" role="alert" data-testid="certificate-validity">
                Certyfikat wygasł {formatWarsawDate(validTo)}. Pobieranie i wysyłka faktur nie działają. Wgraj nowy certyfikat albo token.
              </p>
            ) : expiresIn < EXPIRY_WARNING_MS ? (
              <p className="inv-alert inv-alert--warning" role="status" data-testid="certificate-validity">
                Certyfikat wygasa {formatWarsawDate(validTo)}. Wygeneruj nowy w Aplikacji Podatnika KSeF i wgraj go tutaj, zanim pobieranie i wysyłka przestaną działać.
              </p>
            ) : (
              <p className="company-muted" data-testid="certificate-validity">
                Certyfikat ważny do {formatWarsawDate(validTo)}
              </p>
            )
          ) : null}
          <form onSubmit={onSubmit} noValidate>
            <fieldset className="company-method">
              <legend>Sposób logowania do KSeF</legend>
              {(['Token', 'Certificate'] as const).map((value) => (
                <label key={value} className="inv-check">
                  <input
                    type="radio"
                    name="credential-method"
                    data-testid={`credential-method-${value.toLowerCase()}`}
                    checked={method === value}
                    onChange={() => {
                      setMethod(value)
                      setFormError(null)
                    }}
                  />
                  <span>{value === 'Token' ? 'Token KSeF' : 'Certyfikat KSeF'}</span>
                </label>
              ))}
            </fieldset>
            {method === 'Token' ? (
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
            ) : (
              <Fragment key={certFormKey}>
                <p className="ui-form-hint" data-testid="credential-certificate-help">{CERTIFICATE_HELP}</p>
                <div className="ui-form-group">
                  <label htmlFor="credential-certificate-file">Plik certyfikatu (.pfx, .p12 lub .crt)</label>
                  <input
                    id="credential-certificate-file"
                    data-testid="credential-certificate-file"
                    type="file"
                    accept=".pfx,.p12,.crt"
                    onChange={(event) => setCertFile(event.target.files?.[0] ?? null)}
                  />
                </div>
                {isCrt ? (
                  <div className="ui-form-group">
                    <label htmlFor="credential-key-file">Klucz prywatny (.key)</label>
                    <input
                      id="credential-key-file"
                      data-testid="credential-key-file"
                      type="file"
                      accept=".key"
                      onChange={(event) => setKeyFile(event.target.files?.[0] ?? null)}
                    />
                  </div>
                ) : null}
                <div className="ui-form-group">
                  <label htmlFor="credential-certificate-password">Hasło</label>
                  <input
                    id="credential-certificate-password"
                    data-testid="credential-certificate-password"
                    type="password"
                    autoComplete="new-password"
                    aria-describedby="credential-certificate-password-hint"
                    value={certPassword}
                    onChange={(event) => setCertPassword(event.target.value)}
                  />
                  <span className="ui-form-hint" id="credential-certificate-password-hint">
                    Służy tylko do odczytu pliku. Nie zapisujemy go.
                  </span>
                </div>
              </Fragment>
            )}
            {formError ? (
              <div className="ui-form-error" role="alert">
                {formError}
              </div>
            ) : null}
            <Button type="submit" data-testid="credential-submit-button" disabled={save.isPending}>
              {save.isPending ? 'Zapisywanie…' : `${hasCredential ? 'Zapisz' : 'Dodaj'} ${method === 'Token' ? 'token' : 'certyfikat'}`}
            </Button>
          </form>
        </section>

        <section className="ui-card company-fetch" aria-labelledby="fetch-title">
          <h2 id="fetch-title">Pobieranie z KSeF</h2>
          {credential && !hasCredential ? <p className="company-muted">Pobieranie ruszy po dodaniu tokenu lub certyfikatu.</p> : null}
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
                  <span>Pobieranie faktur i wysyłanie ich do KSeF przestaną działać do czasu dodania nowego tokenu lub certyfikatu.</span>
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
        message={`${isCertificate ? 'Certyfikat KSeF' : 'Token KSeF'} tej firmy zostanie trwale usunięty. Pobieranie faktur i wysyłanie ich do KSeF przestaną działać do czasu dodania nowego tokenu lub certyfikatu.`}
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
