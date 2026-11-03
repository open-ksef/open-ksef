import { useMutation, useQuery } from '@tanstack/react-query'
import { useRef, useState, type ReactElement } from 'react'
import toast from 'react-hot-toast'
import { Link } from 'react-router-dom'

import { registerDevice } from '@/api/endpoints/devices'
import { listTenants } from '@/api/endpoints/tenants'
import { ApiError } from '@/api/errors'

import {
  DEFAULT_RELAY_URL,
  getSettings,
  updateSettings,
  type SettingsResponse,
  type SettingsUpdateRequest,
  type SmtpConfig,
} from '@/api/endpoints/system'
import { Button } from '@/components/Button'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { Icon } from '@/components/Icon'
import { notifyMutationError, notifyMutationSuccess } from '@/notifications'

function ManualDeviceRegistration(): ReactElement {
  const [token, setToken] = useState('')
  const [platform, setPlatform] = useState(0)
  const [tenantId, setTenantId] = useState('')
  const [error, setError] = useState<string | null>(null)
  const tenantsQuery = useQuery({ queryKey: ['tenants'], queryFn: () => listTenants() })

  const register = useMutation({
    mutationFn: registerDevice,
    onSuccess: () => {
      notifyMutationSuccess('registered', 'Device')
      setToken('')
    },
    onError: (mutationError) => notifyMutationError('register', 'Device', mutationError),
  })

  return (
    <section className="ui-card" aria-labelledby="settings-device-title">
      <h2 id="settings-device-title">Ręczna rejestracja urządzenia</h2>
      <p className="ui-form-hint">Narzędzie testowe. Telefon rejestruje się sam po zalogowaniu w aplikacji.</p>
      <div className="onboarding-form">
        <div className="ui-form-group">
          <label htmlFor="device-form-token">Token push</label>
          <input id="device-form-token" data-testid="device-form-token" type="text" value={token} onChange={(event) => setToken(event.target.value)} />
        </div>
        <div className="ui-form-group">
          <label htmlFor="device-form-platform">Platforma</label>
          <select id="device-form-platform" data-testid="device-form-platform" value={platform} onChange={(event) => setPlatform(Number(event.target.value))}>
            <option value={0}>Android</option>
            <option value={1}>iOS</option>
          </select>
        </div>
        <div className="ui-form-group">
          <label htmlFor="device-form-tenant">Firma (opcjonalnie)</label>
          <select id="device-form-tenant" data-testid="device-form-tenant" value={tenantId} onChange={(event) => setTenantId(event.target.value)}>
            <option value="">Wszystkie firmy</option>
            {(tenantsQuery.data ?? []).map((tenant) => (
              <option key={tenant.id} value={tenant.id}>
                {tenant.displayName || tenant.nip}
              </option>
            ))}
          </select>
        </div>
        {error ? (
          <div className="ui-form-error" role="alert">
            {error}
          </div>
        ) : null}
        <div>
          <Button
            data-testid="device-form-submit"
            disabled={register.isPending}
            onClick={() => {
              setError(token.trim() ? null : 'Token jest wymagany')
              if (token.trim()) register.mutate({ token: token.trim(), platform, tenantId: tenantId || null })
            }}
          >
            {register.isPending ? 'Rejestrowanie…' : 'Zarejestruj urządzenie'}
          </Button>
        </div>
      </div>
    </section>
  )
}

export function SettingsAdminPage(): ReactElement {
  const [kcUsername, setKcUsername] = useState('')
  const [kcPassword, setKcPassword] = useState('')
  const [authenticated, setAuthenticated] = useState(false)
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [settings, setSettings] = useState<SettingsResponse | null>(null)

  const [externalUrl, setExternalUrl] = useState('')
  const [ksefEnv, setKsefEnv] = useState('test')
  const [registrationAllowed, setRegistrationAllowed] = useState(true)
  const [verifyEmail, setVerifyEmail] = useState(false)
  const [loginWithEmail, setLoginWithEmail] = useState(true)
  const [resetPassword, setResetPassword] = useState(true)
  const [passwordPolicy, setPasswordPolicy] = useState('length(8)')

  const [smtpEnabled, setSmtpEnabled] = useState(false)
  const [smtpHost, setSmtpHost] = useState('')
  const [smtpPort, setSmtpPort] = useState('587')
  const [smtpFrom, setSmtpFrom] = useState('')
  const [smtpFromDisplay, setSmtpFromDisplay] = useState('OpenKSeF')
  const [smtpStarttls, setSmtpStarttls] = useState(true)
  const [smtpSsl, setSmtpSsl] = useState(false)
  const [smtpAuth, setSmtpAuth] = useState(false)
  const [smtpUser, setSmtpUser] = useState('')
  const [smtpPassword, setSmtpPassword] = useState('')

  const [googleClientId, setGoogleClientId] = useState('')
  const [googleClientSecret, setGoogleClientSecret] = useState('')
  const [pushMode, setPushMode] = useState<'relay' | 'firebase' | 'local'>('relay')
  const [pushRelayUrl, setPushRelayUrl] = useState('')
  const [pushRelayApiKey, setPushRelayApiKey] = useState('')
  const [pushRelayInstanceId, setPushRelayInstanceId] = useState('')
  const [reRegisterRelay, setReRegisterRelay] = useState(false)
  const [firebaseJson, setFirebaseJson] = useState('')

  const [confirmWipeOpen, setConfirmWipeOpen] = useState(false)
  const [pendingRequest, setPendingRequest] = useState<SettingsUpdateRequest | null>(null)
  const saveButton = useRef<HTMLElement | null>(null)

  const populateFromResponse = (resp: SettingsResponse) => {
    setSettings(resp)
    setExternalUrl(resp.externalBaseUrl ?? '')
    setKsefEnv(resp.kSeFEnvironment ?? 'test')
    setRegistrationAllowed(resp.registrationAllowed)
    setVerifyEmail(resp.verifyEmail)
    setLoginWithEmail(resp.loginWithEmailAllowed)
    setResetPassword(resp.resetPasswordAllowed)
    setPasswordPolicy(resp.passwordPolicy ?? 'length(8)')

    if (resp.smtp) {
      setSmtpEnabled(true)
      setSmtpHost(resp.smtp.host)
      setSmtpPort(resp.smtp.port)
      setSmtpFrom(resp.smtp.from)
      setSmtpFromDisplay(resp.smtp.fromDisplayName ?? 'OpenKSeF')
      setSmtpStarttls(resp.smtp.starttls)
      setSmtpSsl(resp.smtp.ssl)
      setSmtpAuth(resp.smtp.auth)
      setSmtpUser(resp.smtp.user ?? '')
      setSmtpPassword('')
    } else {
      setSmtpEnabled(false)
    }

    setGoogleClientId(resp.googleClientId ?? '')
    setGoogleClientSecret('')
    setPushRelayUrl(resp.pushRelayUrl ?? '')
    setPushRelayApiKey('')
    setPushRelayInstanceId(resp.pushRelayInstanceId ?? '')
    setReRegisterRelay(false)

    if (resp.pushRelayUrl) {
      setPushMode('relay')
    } else if (resp.firebaseConfigured) {
      setPushMode('firebase')
    } else {
      setPushMode('local')
    }
  }

  const handleLogin = async () => {
    setError(null)
    if (!kcPassword) {
      setError('Hasło administratora jest wymagane')
      return
    }
    setLoading(true)
    try {
      const resp = await getSettings(kcUsername, kcPassword)
      populateFromResponse(resp)
      setAuthenticated(true)
    } catch (err) {
      setError(err instanceof ApiError && err.status !== 401 ? err.message : 'Nieprawidłowe dane logowania')
    } finally {
      setLoading(false)
    }
  }

  const buildUpdateRequest = (confirmWipe = false): SettingsUpdateRequest => {
    const smtp: SmtpConfig | undefined = smtpEnabled
      ? {
          host: smtpHost,
          port: smtpPort,
          from: smtpFrom,
          fromDisplayName: smtpFromDisplay || undefined,
          starttls: smtpStarttls,
          ssl: smtpSsl,
          auth: smtpAuth,
          user: smtpAuth ? smtpUser : undefined,
          password: smtpAuth ? smtpPassword || undefined : undefined,
        }
      : undefined

    return {
      kcAdminUsername: kcUsername,
      kcAdminPassword: kcPassword,
      externalBaseUrl: externalUrl.replace(/\/+$/, '') || undefined,
      kSeFEnvironment: ksefEnv,
      registrationAllowed,
      verifyEmail: smtpEnabled ? verifyEmail : false,
      loginWithEmailAllowed: loginWithEmail,
      resetPasswordAllowed: smtpEnabled ? resetPassword : false,
      passwordPolicy,
      smtp,
      clearSmtp: !smtpEnabled,
      googleClientId: googleClientId || undefined,
      googleClientSecret: googleClientSecret || undefined,
      pushRelayUrl: pushMode === 'relay' ? (pushRelayUrl || undefined) : '',
      pushRelayApiKey: pushMode === 'relay' && pushRelayUrl !== DEFAULT_RELAY_URL
        ? (pushRelayApiKey || undefined) : undefined,
      reRegisterRelay: pushMode === 'relay' && reRegisterRelay,
      firebaseCredentialsJson: pushMode === 'firebase' ? (firebaseJson || undefined) : '',
      confirmCredentialWipe: confirmWipe,
    }
  }

  const handleSave = async () => {
    // The button is disabled while saving, so it has lost focus before the dialog opens.
    saveButton.current = document.activeElement as HTMLElement | null
    setError(null)
    setSaving(true)
    try {
      const request = buildUpdateRequest()
      const result = await updateSettings(request)
      if (result.requiresConfirmation) {
        setPendingRequest(request)
        setConfirmWipeOpen(true)
        return
      }
      if (!result.success) {
        setError(result.error ?? 'Zapisywanie nie powiodło się')
        return
      }

      toast.success('Ustawienia zapisane pomyślnie!')

      const refreshed = await getSettings(kcUsername, kcPassword)
      populateFromResponse(refreshed)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Zapisywanie nie powiodło się')
    } finally {
      setSaving(false)
    }
  }

  const handleConfirmWipe = async () => {
    setConfirmWipeOpen(false)
    if (!pendingRequest) return

    setSaving(true)
    setError(null)
    try {
      const request = { ...pendingRequest, confirmCredentialWipe: true }
      const result = await updateSettings(request)
      if (!result.success) {
        setError(result.error ?? 'Zapisywanie nie powiodło się')
        return
      }
      toast.success('Ustawienia zapisane. Poświadczenia KSeF zostały usunięte.')

      const refreshed = await getSettings(kcUsername, kcPassword)
      populateFromResponse(refreshed)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Zapisywanie nie powiodło się')
    } finally {
      setSaving(false)
      setPendingRequest(null)
    }
  }

  const passwordPolicyPreset = (() => {
    if (passwordPolicy?.includes('length(12)')) return 'strong'
    return 'basic'
  })()

  const passwordPolicyFromPreset = (preset: string) => {
    switch (preset) {
      case 'strong':
        return 'length(12) and specialChars(1) and upperCase(1) and digits(1) and notUsername'
      case 'basic':
      default:
        return 'length(8)'
    }
  }

  const backLink = (
    <Link to="/settings" className="back-link">
      <Icon name="arrow-left" size={14} /> Ustawienia
    </Link>
  )

  if (!authenticated) {
    return (
      <section>
        {backLink}
        <header className="page-header">
          <h1>Administracja instancji</h1>
        </header>

        <section className="ui-card settings-login" aria-labelledby="settings-login-title">
          <h2 id="settings-login-title">Zaloguj się jako administrator</h2>
          <p className="company-muted">Tylko dla osoby, która zarządza tą instancją OpenKSeF.</p>
          <div className="onboarding-form">
            <div className="ui-form-group">
              <label htmlFor="settings-kc-user">Nazwa użytkownika administratora</label>
              <input id="settings-kc-user" data-testid="settings-kc-user" type="text" autoComplete="off"
                value={kcUsername} onInput={e => setKcUsername((e.target as HTMLInputElement).value)} />
            </div>
            <div className="ui-form-group">
              <label htmlFor="settings-kc-pass">Hasło administratora</label>
              <input id="settings-kc-pass" data-testid="settings-kc-pass" type="password"
                value={kcPassword} onInput={e => setKcPassword((e.target as HTMLInputElement).value)}
                onKeyDown={e => { if (e.key === 'Enter') void handleLogin() }} />
            </div>
            {error && <div className="ui-form-error" role="alert">{error}</div>}
            <div>
              <Button data-testid="settings-login" onClick={() => void handleLogin()} disabled={loading}>
                {loading ? 'Logowanie…' : 'Zaloguj'}
              </Button>
            </div>
          </div>
        </section>
      </section>
    )
  }

  return (
    <section>
      {backLink}
      <header className="page-header">
        <h1>Administracja instancji</h1>
        <Button data-testid="settings-save" onClick={() => void handleSave()} disabled={saving}>
          {saving ? 'Zapisywanie…' : 'Zapisz zmiany'}
        </Button>
      </header>

      {error && <div className="ui-form-error settings-error" role="alert">{error}</div>}

      <div className="company-grid">
        <section className="ui-card" aria-labelledby="settings-basic-title">
          <h2 id="settings-basic-title">Podstawowe</h2>
          <div className="onboarding-form">
            <div className="ui-form-group">
              <label htmlFor="settings-url">Zewnętrzny adres URL systemu</label>
              <input id="settings-url" data-testid="settings-url" type="url"
                value={externalUrl} onInput={e => setExternalUrl((e.target as HTMLInputElement).value)} />
            </div>
            <div className="ui-form-group">
              <label htmlFor="settings-ksef-env">Środowisko KSeF</label>
              <select id="settings-ksef-env" data-testid="settings-ksef-env"
                value={ksefEnv}
                onChange={e => setKsefEnv(e.target.value)}
                disabled={settings?.kSeFEnvironmentLocked}>
                <option value="test">Test</option>
                <option value="production">Produkcja</option>
              </select>
              {settings?.kSeFEnvironmentLockReason && (
                <span className="ui-form-hint" style={{ color: settings.kSeFEnvironmentLocked ? 'var(--ui-error)' : 'var(--ui-warning)' }}>
                  {settings.kSeFEnvironmentLockReason}
                </span>
              )}
            </div>
          </div>
        </section>

        <section className="ui-card" aria-labelledby="settings-auth-title">
          <h2 id="settings-auth-title">Autoryzacja</h2>
          <div className="onboarding-form">
            <div className="ui-form-group">
              <label><input type="checkbox" checked={registrationAllowed} onChange={e => setRegistrationAllowed(e.target.checked)} /> Zezwalaj na rejestrację</label>
            </div>
            <div className="ui-form-group">
              <label><input type="checkbox" checked={verifyEmail} onChange={e => setVerifyEmail(e.target.checked)} disabled={!smtpEnabled} /> Wymagaj weryfikacji e-mail</label>
            </div>
            <div className="ui-form-group">
              <label><input type="checkbox" checked={loginWithEmail} onChange={e => setLoginWithEmail(e.target.checked)} /> Logowanie adresem e-mail</label>
            </div>
            <div className="ui-form-group">
              <label><input type="checkbox" checked={resetPassword} onChange={e => setResetPassword(e.target.checked)} disabled={!smtpEnabled} /> Zezwalaj na reset hasła</label>
            </div>
            <div className="ui-form-group">
              <label htmlFor="settings-pass-policy">Polityka haseł</label>
              <select id="settings-pass-policy" data-testid="settings-pass-policy"
                value={passwordPolicyPreset}
                onChange={e => setPasswordPolicy(passwordPolicyFromPreset(e.target.value))}>
                <option value="basic">Podstawowa (min. 8 znaków)</option>
                <option value="strong">Silna (12 znaków, cyfry, duże litery, znaki specjalne)</option>
              </select>
            </div>
          </div>
        </section>

        <section className="ui-card" aria-labelledby="settings-smtp-title">
          <h2 id="settings-smtp-title">SMTP</h2>
          <div className="onboarding-form">
            <div className="ui-form-group">
              <label><input type="checkbox" checked={smtpEnabled} onChange={e => setSmtpEnabled(e.target.checked)} /> Serwer SMTP włączony</label>
              {!smtpEnabled && (
                <span className="ui-form-hint" style={{ color: 'var(--ui-warning)' }}>
                  Bez SMTP weryfikacja e-mail i reset hasła będą niedostępne.
                </span>
              )}
            </div>

            {smtpEnabled && (
              <>
                <div className="ui-form-group">
                  <label htmlFor="settings-smtp-host">Host SMTP</label>
                  <input id="settings-smtp-host" data-testid="settings-smtp-host" type="text" placeholder="smtp.gmail.com"
                    value={smtpHost} onInput={e => setSmtpHost((e.target as HTMLInputElement).value)} />
                </div>
                <div className="ui-form-group">
                  <label htmlFor="settings-smtp-port">Port</label>
                  <input id="settings-smtp-port" type="text" placeholder="587"
                    value={smtpPort} onInput={e => setSmtpPort((e.target as HTMLInputElement).value)} />
                </div>
                <div className="ui-form-group">
                  <label htmlFor="settings-smtp-from">Adres nadawcy (From)</label>
                  <input id="settings-smtp-from" data-testid="settings-smtp-from" type="email" placeholder="noreply@firma.pl"
                    value={smtpFrom} onInput={e => setSmtpFrom((e.target as HTMLInputElement).value)} />
                </div>
                <div className="ui-form-group">
                  <label htmlFor="settings-smtp-display">Nazwa wyświetlana</label>
                  <input id="settings-smtp-display" type="text" placeholder="OpenKSeF"
                    value={smtpFromDisplay} onInput={e => setSmtpFromDisplay((e.target as HTMLInputElement).value)} />
                </div>
                <div className="ui-form-group">
                  <label><input type="checkbox" checked={smtpStarttls} onChange={e => setSmtpStarttls(e.target.checked)} /> StartTLS</label>
                </div>
                <div className="ui-form-group">
                  <label><input type="checkbox" checked={smtpSsl} onChange={e => setSmtpSsl(e.target.checked)} /> SSL/TLS</label>
                </div>
                <div className="ui-form-group">
                  <label><input type="checkbox" checked={smtpAuth} onChange={e => setSmtpAuth(e.target.checked)} /> Uwierzytelnianie</label>
                </div>
                {smtpAuth && (
                  <>
                    <div className="ui-form-group">
                      <label htmlFor="settings-smtp-user">Nazwa użytkownika SMTP</label>
                      <input id="settings-smtp-user" type="text"
                        value={smtpUser} onInput={e => setSmtpUser((e.target as HTMLInputElement).value)} />
                    </div>
                    <div className="ui-form-group">
                      <label htmlFor="settings-smtp-pass">Hasło SMTP</label>
                      <input id="settings-smtp-pass" data-testid="settings-smtp-pass" type="password" autoComplete="new-password"
                        value={smtpPassword} onInput={e => setSmtpPassword((e.target as HTMLInputElement).value)} />
                      {settings?.smtp?.auth && (
                        <span className="ui-form-hint" data-testid="settings-smtp-pass-hint">Pozostaw puste, aby zachować obecne hasło.</span>
                      )}
                    </div>
                  </>
                )}
              </>
            )}
          </div>
        </section>

        <section className="ui-card" aria-labelledby="settings-integrations-title">
          <h2 id="settings-integrations-title">Integracje</h2>
          <div className="onboarding-form">
            <h3 className="settings-h3">Google OAuth</h3>
            {settings?.googleConfigured && (
              <div className="onboarding-instruction" style={{ marginBottom: '8px' }}>
                Google OAuth jest skonfigurowany. Podaj nowe dane, aby zaktualizować.
              </div>
            )}
            <div className="ui-form-group">
              <label htmlFor="settings-google-id">Google Client ID</label>
              <input id="settings-google-id" data-testid="settings-google-id" type="text"
                placeholder="123456789-xxx.apps.googleusercontent.com"
                value={googleClientId} onInput={e => setGoogleClientId((e.target as HTMLInputElement).value)} />
            </div>
            <div className="ui-form-group">
              <label htmlFor="settings-google-secret">Google Client Secret</label>
              <input id="settings-google-secret" data-testid="settings-google-secret" type="password"
                placeholder={settings?.googleConfigured ? '(nie zmieniono)' : ''}
                value={googleClientSecret} onInput={e => setGoogleClientSecret((e.target as HTMLInputElement).value)} />
            </div>

            <hr style={{ margin: '16px 0', border: 'none', borderTop: '1px solid var(--ui-border)' }} />

            <h3 className="settings-h3">Powiadomienia push</h3>

            <div className="ui-form-group">
              <label>
                <input type="radio" name="pushMode" value="relay"
                  checked={pushMode === 'relay'} onChange={() => setPushMode('relay')} />
                {' '}Relay OpenKSeF <span style={{ fontWeight: 400, color: 'var(--ui-muted)' }}>(zalecane)</span>
              </label>
            </div>

            {pushMode === 'relay' && (
              <>
                <div className="ui-form-group">
                  <label htmlFor="settings-relay-url">URL serwera relay</label>
                  <input id="settings-relay-url" data-testid="settings-relay-url" type="url"
                    value={pushRelayUrl} onInput={e => setPushRelayUrl((e.target as HTMLInputElement).value)} />
                </div>

                {pushRelayInstanceId && (
                  <div className="onboarding-instruction" style={{ marginBottom: '8px' }}>
                    <strong>Status rejestracji:</strong> Zarejestrowano (ID: <code style={{ fontSize: '12px' }}>{pushRelayInstanceId}</code>)
                  </div>
                )}

                {pushRelayUrl === DEFAULT_RELAY_URL ? (
                  <>
                    {!pushRelayInstanceId && (
                      <div className="onboarding-instruction" style={{ color: 'var(--ui-warning)' }}>
                        System nie jest zarejestrowany w serwisie relay. Kliknij &quot;Zarejestruj ponownie&quot;, aby uzyskać klucz API.
                      </div>
                    )}
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '12px' }}>
                      <Button
                        data-testid="settings-reregister-relay"
                        size="sm"
                        onClick={() => setReRegisterRelay(true)}
                        disabled={reRegisterRelay}
                      >
                        {reRegisterRelay ? 'Ponowna rejestracja przy zapisie...' : 'Zarejestruj ponownie'}
                      </Button>
                      {reRegisterRelay && (
                        <span className="ui-form-hint">Nowy klucz zostanie wygenerowany po zapisaniu zmian.</span>
                      )}
                    </div>
                  </>
                ) : (
                  <div className="ui-form-group">
                    <label htmlFor="settings-relay-key">Klucz API relay</label>
                    <input id="settings-relay-key" data-testid="settings-relay-key" type="password" autoComplete="new-password"
                      placeholder="Klucz API dla niestandardowego serwera relay"
                      value={pushRelayApiKey} onInput={e => setPushRelayApiKey((e.target as HTMLInputElement).value)} />
                    {settings?.pushRelayApiKeyConfigured && (
                      <span className="ui-form-hint" data-testid="settings-relay-key-hint">Klucz jest zapisany. Pozostaw puste, aby go zachować.</span>
                    )}
                  </div>
                )}
              </>
            )}

            <div className="ui-form-group">
              <label>
                <input type="radio" name="pushMode" value="firebase"
                  checked={pushMode === 'firebase'} onChange={() => setPushMode('firebase')} />
                {' '}Własny projekt Firebase
              </label>
            </div>

            {pushMode === 'firebase' && (
              <div className="ui-form-group">
                <label htmlFor="settings-firebase">Firebase Credentials JSON</label>
                <textarea id="settings-firebase" data-testid="settings-firebase" rows={4}
                  placeholder='{"type":"service_account","project_id":"...","private_key":"..."}'
                  value={firebaseJson} onInput={e => setFirebaseJson((e.target as HTMLTextAreaElement).value)} />
                {settings?.firebaseConfigured && !firebaseJson && (
                  <span className="ui-form-hint">Firebase jest skonfigurowany. Pozostaw puste, aby zachować obecną konfigurację.</span>
                )}
              </div>
            )}

            <div className="ui-form-group">
              <label>
                <input type="radio" name="pushMode" value="local"
                  checked={pushMode === 'local'} onChange={() => setPushMode('local')} />
                {' '}Tylko lokalne (SignalR)
              </label>
            </div>
          </div>
        </section>

        <ManualDeviceRegistration />
      </div>

      <div className="inv-header company-grid-foot settings-foot">
        <Button data-testid="settings-save-bottom" onClick={() => void handleSave()} disabled={saving}>
          {saving ? 'Zapisywanie…' : 'Zapisz zmiany'}
        </Button>
      </div>

      <ConfirmDialog
        open={confirmWipeOpen}
        title="Zmiana środowiska KSeF"
        message="Zmiana środowiska KSeF wymaga usunięcia wszystkich zapisanych poświadczeń KSeF (tokenów autoryzacyjnych i certyfikatów). Tej operacji nie można cofnąć. Czy chcesz kontynuować?"
        confirmLabel="Usuń poświadczenia i zmień"
        variant="danger"
        onConfirm={() => void handleConfirmWipe()}
        onCancel={() => { setConfirmWipeOpen(false); setPendingRequest(null); saveButton.current?.focus() }}
      />
    </section>
  )
}
