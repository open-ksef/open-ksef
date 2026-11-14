import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState, type ReactElement } from 'react'
import toast from 'react-hot-toast'
import { useNavigate, useSearchParams } from 'react-router-dom'

import { credentialsOf, deleteCredential, getSessionDevices, remainingRecoveryCodes, type AccountDevice } from '@/auth/account'
import type { AccountAction } from '@/auth/AuthContext'
import { signOutAllDevices } from '@/auth/signOutAll'
import { useAuth } from '@/auth/useAuth'
import { Button } from '@/components/Button'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { ErrorBanner } from '@/components/ErrorBanner'
import { Icon } from '@/components/Icon'
import { SettingsHeader } from '@/components/SettingsHeader'
import { Skeleton } from '@/components/Skeleton'
import { useSignInMethods } from '@/hooks/useSignInMethods'
import { formatDateTime, formatWarsawDate, polishPlural } from '@/invoicing/format'

const TOTAL_CODES = 12

const iso = (ms?: number) => (ms ? new Date(ms).toISOString() : null)

interface SessionRow {
  id: string
  device: string
  sub: string
  lastAccess: string
  started: string
}

function sessionRows(devices: AccountDevice[]): SessionRow[] {
  return devices.flatMap((device) => device.sessions.map((session) => {
    const app = session.clients.some((client) => client.clientId === 'openksef-mobile')
    const browser = (session.browser ?? device.browser ?? '').split('/')[0] || 'Przeglądarka'
    return {
      id: session.id,
      device: app ? `Telefon: ${device.os || 'nieznany'}` : `${browser}, ${device.os || 'nieznany system'}`,
      sub: session.current ? `Ta przeglądarka · ${session.ipAddress}` : app ? 'Aplikacja OpenKSeF' : session.ipAddress,
      lastAccess: session.current ? 'teraz' : formatDateTime(iso(session.lastAccess * 1000)),
      started: formatDateTime(iso(session.started * 1000)),
    }
  }))
}

function codesLeftText(left: number): string {
  return `${polishPlural(left, ['Został', 'Zostały', 'Zostało'])} ${left} z ${TOTAL_CODES}`
}

function lowCodesText(left: number): string {
  if (left === 0) return 'Nie masz już kodów zapasowych. Wygeneruj nowe.'
  return `${polishPlural(left, ['Został', 'Zostały', 'Zostało'])} tylko ${left} ${polishPlural(left, ['kod zapasowy', 'kody zapasowe', 'kodów zapasowych'])}. Wygeneruj nowe, zanim się skończą.`
}

export function SettingsSecurityPage(): ReactElement {
  const { startAction } = useAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [searchParams] = useSearchParams()
  const { credentials, linked, password, google } = useSignInMethods()
  const devices = useQuery({ queryKey: ['account', 'sessions'], queryFn: getSessionDevices })
  const [confirmRegenerate, setConfirmRegenerate] = useState(false)
  const [confirmSignOutAll, setConfirmSignOutAll] = useState(false)
  const handledStatus = useRef(false)

  const otp = credentialsOf(credentials.data, 'otp').sort((a, b) => (b.createdDate ?? 0) - (a.createdDate ?? 0))
  const codesLeft = remainingRecoveryCodes(credentials.data) ?? 0
  const googleOnly = google && !password
  const lowCodes = codesLeft <= 3
  const actionStatus = searchParams.get('kc_action_status')
  const startedAction = searchParams.get('action')

  useEffect(() => {
    if (!actionStatus || !credentials.data || handledStatus.current) return
    handledStatus.current = true
    navigate('/settings/security', { replace: true })
    if (actionStatus !== 'success') return
    toast.success('Zapisano.')
    const older = startedAction === 'CONFIGURE_TOTP' ? otp.slice(1) : []
    if (older.length) {
      void Promise.all(older.map((credential) => deleteCredential(credential.id)))
        .then(() => queryClient.invalidateQueries({ queryKey: ['account', 'credentials'] }))
        .catch(() => toast.error('Nie udało się usunąć starej aplikacji uwierzytelniającej. Odśwież stronę i spróbuj ponownie.'))
    }
  }, [actionStatus, credentials.data, navigate, otp, queryClient, startedAction])

  const action = useMutation({
    mutationFn: (name: AccountAction) => startAction(name),
    onError: () => toast.error('Nie udało się otworzyć strony logowania. Sprawdź połączenie i spróbuj ponownie.'),
  })

  const signOutAll = useMutation({
    mutationFn: signOutAllDevices,
    onSuccess: () => navigate('/login', { replace: true }),
    onError: (error) => {
      toast.error(`Nie udało się wylogować wszystkich urządzeń: ${error.message} Sesje mogą nadal działać. Spróbuj ponownie.`)
      setConfirmSignOutAll(false)
    },
  })

  const accountLoading = credentials.isLoading || linked.isLoading
  const accountError = credentials.error ?? linked.error
  const rows = sessionRows(devices.data ?? [])

  return (
    <section>
      <SettingsHeader current="/settings/security" />
      {accountError && (
        <ErrorBanner message="Nie udało się wczytać danych konta." onRetry={() => { void credentials.refetch(); void linked.refetch() }} />
      )}
      <div className="company-grid">
        {!googleOnly && (
          <section className="ui-card" aria-labelledby="password-title">
            <h2 id="password-title">Hasło</h2>
            {accountLoading ? <Skeleton lines={1} /> : (
              <dl className="settings-kv">
                <dt>Ostatnia zmiana</dt>
                <dd data-testid="password-changed">{formatWarsawDate(iso(credentialsOf(credentials.data, 'password')[0]?.createdDate))}</dd>
              </dl>
            )}
            <div className="settings-actions">
              <Button variant="outline" data-testid="password-change" onClick={() => action.mutate('UPDATE_PASSWORD')}>
                <Icon name="key-round" /> Zmień hasło
              </Button>
            </div>
          </section>
        )}

        <section className="ui-card" aria-labelledby="tfa-title">
          <h2 id="tfa-title">Weryfikacja dwuetapowa</h2>
          {accountLoading ? <Skeleton lines={3} /> : googleOnly ? (
            <>
              <div className="settings-2fa-head"><span className="ui-status-badge ui-status-badge--success">Przez Google</span></div>
              <p className="settings-muted">Logujesz się przez Google. Weryfikację dwuetapową ustawiasz na swoim koncie Google.</p>
            </>
          ) : otp.length === 0 ? (
            <>
              <div className="settings-2fa-head"><span className="ui-status-badge ui-status-badge--warning" data-testid="tfa-status">Nieustawiona</span></div>
              <p className="settings-muted">Ustawisz ją przy następnym logowaniu hasłem. Możesz to zrobić teraz.</p>
              <div className="settings-actions">
                <Button data-testid="tfa-setup" onClick={() => action.mutate('CONFIGURE_TOTP')}>
                  <Icon name="shield-check" /> Ustaw teraz
                </Button>
              </div>
            </>
          ) : (
            <>
              <div className="settings-2fa-head"><span className="ui-status-badge ui-status-badge--success" data-testid="tfa-status">Włączona</span></div>
              <dl className="settings-kv">
                <dt>Metoda</dt>
                <dd>Aplikacja uwierzytelniająca</dd>
                <dt>Od</dt>
                <dd>{formatWarsawDate(iso(otp[0].createdDate))}</dd>
                <dt>Kody zapasowe</dt>
                <dd data-testid="tfa-codes">{codesLeftText(codesLeft)}</dd>
              </dl>
              {lowCodes && (
                <div className="auth-notice auth-notice--warning" role="status">
                  <Icon name="triangle-alert" />
                  <span>{lowCodesText(codesLeft)}</span>
                </div>
              )}
              <div className="settings-actions">
                <Button variant={lowCodes ? 'primary' : 'outline'} data-testid="tfa-regenerate" onClick={() => setConfirmRegenerate(true)}>
                  <Icon name="key-round" /> Wygeneruj nowe kody
                </Button>
                <Button variant="outline" data-testid="tfa-change" onClick={() => action.mutate('CONFIGURE_TOTP')}>
                  <Icon name="smartphone" /> Zmień aplikację
                </Button>
              </div>
            </>
          )}
        </section>

        <section className="ui-card" aria-labelledby="sessions-title">
          <h2 id="sessions-title">Aktywne sesje i urządzenia</h2>
          {devices.isLoading ? <Skeleton lines={3} /> : devices.error ? (
            <ErrorBanner message="Nie udało się wczytać sesji." onRetry={() => void devices.refetch()} />
          ) : (
            <>
              <div className="ui-table-wrap settings-sessions-table" tabIndex={0} role="region" aria-label="Aktywne sesje">
                <table className="ui-table">
                  <thead>
                    <tr><th>Urządzenie</th><th>Ostatnia aktywność</th><th>Zalogowano</th></tr>
                  </thead>
                  <tbody>
                    {rows.map((row) => (
                      <tr key={row.id}>
                        <td><span className="settings-session"><b>{row.device}</b><span>{row.sub}</span></span></td>
                        <td>{row.lastAccess}</td>
                        <td>{row.started}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <ul className="settings-sessions-list" aria-label="Aktywne sesje">
                {rows.map((row) => (
                  <li key={row.id} className="settings-session">
                    <b>{row.device}</b>
                    <span>{row.sub}</span>
                    <span>Aktywne {row.lastAccess}</span>
                  </li>
                ))}
              </ul>
            </>
          )}
          <p className="settings-muted">Pojedynczej sesji nie wylogujesz. Przycisk poniżej wylogowuje wszystkie naraz, także tę przeglądarkę.</p>
          <div className="settings-actions">
            <button type="button" className="ui-button ui-button--danger-outline ui-button--md" data-testid="sign-out-all-button" onClick={() => setConfirmSignOutAll(true)}>
              Wyloguj wszystkie urządzenia
            </button>
          </div>
        </section>
      </div>

      <ConfirmDialog
        open={confirmRegenerate}
        title="Wygenerować nowe kody zapasowe?"
        message="Dotychczasowe kody przestaną działać. Nowe zobaczysz raz, na następnej stronie."
        confirmLabel="Wygeneruj nowe kody"
        pendingLabel="Przekierowanie…"
        isPending={action.isPending}
        onConfirm={() => action.mutate('CONFIGURE_RECOVERY_AUTHN_CODES')}
        onCancel={() => setConfirmRegenerate(false)}
      />

      <ConfirmDialog
        open={confirmSignOutAll}
        title="Wylogować wszystkie urządzenia?"
        message="Aplikacja mobilna zostanie wylogowana na każdym telefonie, a telefony przestaną dostawać powiadomienia. Portal zostanie wylogowany we wszystkich przeglądarkach, także w tej. Token dostępu wydany wcześniej może działać jeszcze do 30 minut."
        confirmLabel="Wyloguj wszystkie"
        pendingLabel="Wylogowywanie…"
        variant="danger"
        isPending={signOutAll.isPending}
        onConfirm={() => signOutAll.mutate()}
        onCancel={() => setConfirmSignOutAll(false)}
      />
    </section>
  )
}
