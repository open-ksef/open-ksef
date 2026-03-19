import { useState, type ReactElement } from 'react'
import toast from 'react-hot-toast'

import {
  getSettings,
  updateSettings,
  type SettingsResponse,
  type SettingsUpdateRequest,
  type SmtpConfig,
} from '@/api/endpoints/system'
import { Button } from '@/components/Button'
import { ConfirmDialog } from '@/components/ConfirmDialog'

function CollapsibleSection({ title, children, defaultOpen = false }: { title: string; children: React.ReactNode; defaultOpen?: boolean }) {
  const [open, setOpen] = useState(defaultOpen)
  return (
    <div style={{ marginBottom: '24px' }}>
      <h2
        onClick={() => setOpen(!open)}
        style={{ cursor: 'pointer', userSelect: 'none', margin: '0 0 12px', fontSize: '18px', display: 'flex', alignItems: 'center', gap: '8px' }}
      >
        <span style={{ fontSize: '12px' }}>{open ? '▾' : '▸'}</span> {title}
      </h2>
      {open && <div>{children}</div>}
    </div>
  )
}

export function SettingsPage(): ReactElement {
  const [kcUsername, setKcUsername] = useState('admin')
  const [kcPassword, setKcPassword] = useState('')
  const [authenticated, setAuthenticated] = useState(false)
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [settings, setSettings] = useState<SettingsResponse | null>(null)

  // Editable fields
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
    } else {
      setSmtpEnabled(false)
    }

    setGoogleClientId(resp.googleClientId ?? '')
    setGoogleClientSecret('')
    setPushRelayUrl(resp.pushRelayUrl ?? '')
    setPushRelayApiKey(resp.pushRelayApiKey ?? '')
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
      setError('Hasło administratora Keycloak jest wymagane')
      return
    }
    setLoading(true)
    try {
      const resp = await getSettings(kcUsername, kcPassword)
      populateFromResponse(resp)
      setAuthenticated(true)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Nieprawidłowe dane logowania Keycloak')
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
          password: smtpAuth ? smtpPassword : undefined,
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
      pushRelayApiKey: pushMode === 'relay' && pushRelayUrl !== 'https://push.open-ksef.pl'
        ? (pushRelayApiKey || undefined) : undefined,
      reRegisterRelay: pushMode === 'relay' && reRegisterRelay,
      firebaseCredentialsJson: pushMode === 'firebase' ? (firebaseJson || undefined) : '',
      confirmCredentialWipe: confirmWipe,
    }
  }

  const handleSave = async () => {
    setError(null)
    setSaving(true)
    try {
      const request = buildUpdateRequest()
      const result = await updateSettings(request)
      if (!result.success) {
        if (result.error?.includes('Potwierdź operację')) {
          setPendingRequest(request)
          setConfirmWipeOpen(true)
          return
        }
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

  if (!authenticated) {
    return (
      <section>
        <header className="page-header">
          <h1>Ustawienia</h1>
        </header>

        <div style={{ maxWidth: '480px' }}>
          <p style={{ color: 'var(--ui-text-muted)', marginBottom: '16px' }}>
            Zaloguj się jako administrator Keycloak, aby zarządzać ustawieniami systemu.
          </p>
          <div className="onboarding-form">
            <div className="ui-form-group">
              <label htmlFor="settings-kc-user">Nazwa użytkownika Keycloak</label>
              <input id="settings-kc-user" data-testid="settings-kc-user" type="text"
                value={kcUsername} onInput={e => setKcUsername((e.target as HTMLInputElement).value)} />
            </div>
            <div className="ui-form-group">
              <label htmlFor="settings-kc-pass">Hasło Keycloak</label>
              <input id="settings-kc-pass" data-testid="settings-kc-pass" type="password"
                value={kcPassword} onInput={e => setKcPassword((e.target as HTMLInputElement).value)}
                onKeyDown={e => { if (e.key === 'Enter') void handleLogin() }} />
            </div>
            {error && <div className="ui-form-error" role="alert">⚠ {error}</div>}
            <div style={{ marginTop: '12px' }}>
              <Button data-testid="settings-login" onClick={() => void handleLogin()} disabled={loading}>
                {loading ? 'Logowanie…' : 'Zaloguj'}
              </Button>
            </div>
          </div>
        </div>
      </section>
    )
  }

  return (
    <section>
      <header className="page-header" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <h1>Ustawienia</h1>
        <Button data-testid="settings-save" onClick={() => void handleSave()} disabled={saving}>
          {saving ? 'Zapisywanie…' : 'Zapisz zmiany'}
        </Button>
      </header>

      {error && <div className="ui-form-error" role="alert" style={{ marginBottom: '16px' }}>⚠ {error}</div>}

      <div style={{ maxWidth: '640px' }}>
        <CollapsibleSection title="Podstawowe" defaultOpen>
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
                <option value="test">Test (ksef-test.mf.gov.pl)</option>
                <option value="production">Produkcja (ksef.podatki.gov.pl)</option>
              </select>
              {settings?.kSeFEnvironmentLockReason && (
                <span className="ui-form-hint" style={{ color: settings.kSeFEnvironmentLocked ? 'var(--ui-color-error, #c0392b)' : 'var(--ui-warning)' }}>
                  {settings.kSeFEnvironmentLockReason}
                </span>
              )}
            </div>
          </div>
        </CollapsibleSection>

        <CollapsibleSection title="Autoryzacja" defaultOpen>
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
        </CollapsibleSection>

        <CollapsibleSection title="SMTP">
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
                      <input id="settings-smtp-pass" type="password"
                        value={smtpPassword} onInput={e => setSmtpPassword((e.target as HTMLInputElement).value)} />
                    </div>
                  </>
                )}
              </>
            )}
          </div>
        </CollapsibleSection>

        <CollapsibleSection title="Integracje">
          <div className="onboarding-form">
            <h3 style={{ margin: '0 0 8px' }}>Google OAuth</h3>
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

            <h3 style={{ margin: '0 0 8px' }}>Powiadomienia push</h3>

            <div className="ui-form-group">
              <label>
                <input type="radio" name="pushMode" value="relay"
                  checked={pushMode === 'relay'} onChange={() => setPushMode('relay')} />
                {' '}Relay OpenKSeF <span style={{ fontWeight: 400, color: 'var(--ui-text-muted)' }}>(zalecane)</span>
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

                {pushRelayUrl === 'https://push.open-ksef.pl' ? (
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
                    <input id="settings-relay-key" data-testid="settings-relay-key" type="password"
                      placeholder="Klucz API dla niestandardowego serwera relay"
                      value={pushRelayApiKey} onInput={e => setPushRelayApiKey((e.target as HTMLInputElement).value)} />
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
        </CollapsibleSection>

        <div style={{ marginTop: '24px', paddingTop: '16px', borderTop: '1px solid var(--ui-border)' }}>
          <Button data-testid="settings-save-bottom" onClick={() => void handleSave()} disabled={saving}>
            {saving ? 'Zapisywanie…' : 'Zapisz zmiany'}
          </Button>
        </div>
      </div>

      <ConfirmDialog
        open={confirmWipeOpen}
        title="Zmiana środowiska KSeF"
        message="Zmiana środowiska KSeF wymaga usunięcia wszystkich zapisanych poświadczeń KSeF (tokenów autoryzacyjnych i certyfikatów). Tej operacji nie można cofnąć. Czy chcesz kontynuować?"
        confirmLabel="Usuń poświadczenia i zmień"
        variant="danger"
        onConfirm={() => void handleConfirmWipe()}
        onCancel={() => { setConfirmWipeOpen(false); setPendingRequest(null) }}
      />
    </section>
  )
}
