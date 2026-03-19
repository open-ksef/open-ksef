import { useState, type ReactElement } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import toast from 'react-hot-toast'

import {
  authenticateAdmin,
  applySetup,
  type SetupApplyRequest,
  type SmtpConfig,
} from '@/api/endpoints/system'
import { Button } from '@/components/Button'

type Step = 1 | 2 | 3 | 4 | 5 | 6

function StepIndicator({ current }: { current: Step }) {
  const steps = [1, 2, 3, 4, 5, 6] as const
  const labels = ['Logowanie', 'Konto i firma', 'Autoryzacja', 'Bezpieczeństwo', 'Integracje', 'Podsumowanie']
  return (
    <div className="onboarding-stepper" data-testid="admin-setup-step-indicator">
      {steps.map((s, i) => (
        <div key={s} className="onboarding-stepper__step">
          <div
            className={[
              'onboarding-stepper__circle',
              s === current && 'onboarding-stepper__circle--active',
              s < current && 'onboarding-stepper__circle--done',
            ]
              .filter(Boolean)
              .join(' ')}
            title={labels[i]}
          >
            {s < current ? '✓' : s}
          </div>
          {i < steps.length - 1 && (
            <div
              className={[
                'onboarding-stepper__line',
                s < current && 'onboarding-stepper__line--done',
              ]
                .filter(Boolean)
                .join(' ')}
            />
          )}
        </div>
      ))}
    </div>
  )
}

function CollapsibleGuide({ title, children }: { title: string; children: React.ReactNode }) {
  const [open, setOpen] = useState(false)
  return (
    <div className="onboarding-instruction" style={{ cursor: 'pointer' }}>
      <div onClick={() => setOpen(!open)} style={{ fontWeight: 600, userSelect: 'none' }}>
        {open ? '▾' : '▸'} {title}
      </div>
      {open && <div style={{ marginTop: '8px' }}>{children}</div>}
    </div>
  )
}

export function AdminSetupPage(): ReactElement {
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [step, setStep] = useState<Step>(1)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  // Step 1
  const [kcUsername, setKcUsername] = useState('admin')
  const [kcPassword, setKcPassword] = useState('')
  const [setupToken, setSetupToken] = useState<string | null>(null)
  const [newKcPassword, setNewKcPassword] = useState('')
  const [newKcPasswordConfirm, setNewKcPasswordConfirm] = useState('')

  // Step 2
  const [externalUrl, setExternalUrl] = useState(globalThis.location?.origin ?? 'http://localhost:8080')
  const [ksefEnv, setKsefEnv] = useState('test')
  const [userEmail, setUserEmail] = useState('')
  const [userPassword, setUserPassword] = useState('')
  const [userFirstName, setUserFirstName] = useState('')
  const [userLastName, setUserLastName] = useState('')
  const [firstTenantNip, setFirstTenantNip] = useState('')
  const [firstTenantDisplayName, setFirstTenantDisplayName] = useState('')

  // Step 3
  const [registrationAllowed, setRegistrationAllowed] = useState(true)
  const [verifyEmail, setVerifyEmail] = useState(false)
  const [loginWithEmail, setLoginWithEmail] = useState(true)
  const [resetPassword, setResetPassword] = useState(true)
  const [passwordPolicyPreset, setPasswordPolicyPreset] = useState('basic')
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

  // Step 4 (auto-generated, shown after apply)
  const [encryptionKey, setEncryptionKey] = useState<string | null>(null)
  const [apiClientSecret, setApiClientSecret] = useState<string | null>(null)

  // Step 5
  const [googleClientId, setGoogleClientId] = useState('')
  const [googleClientSecret, setGoogleClientSecret] = useState('')
  const [pushMode, setPushMode] = useState<'relay' | 'firebase' | 'local'>('relay')
  const [pushRelayUrl, setPushRelayUrl] = useState('https://push.open-ksef.pl')
  const [pushRelayApiKey, setPushRelayApiKey] = useState('')
  const [firebaseJson, setFirebaseJson] = useState('')

  const passwordPolicyValue = (() => {
    switch (passwordPolicyPreset) {
      case 'strong':
        return 'length(12) and specialChars(1) and upperCase(1) and digits(1) and notUsername'
      case 'basic':
      default:
        return 'length(8)'
    }
  })()

  const handleStep1 = async () => {
    setError(null)
    if (!kcPassword) {
      setError('Hasło administratora Keycloak jest wymagane')
      return
    }
    if (newKcPassword && newKcPassword.length < 8) {
      setError('Nowe hasło musi mieć co najmniej 8 znaków')
      return
    }
    if (newKcPassword && newKcPassword !== newKcPasswordConfirm) {
      setError('Hasła nie są identyczne')
      return
    }
    setLoading(true)
    try {
      const resp = await authenticateAdmin({ username: kcUsername, password: kcPassword })
      setSetupToken(resp.setupToken)
      setStep(2)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Nieprawidłowe dane logowania Keycloak')
    } finally {
      setLoading(false)
    }
  }

  const handleStep2 = () => {
    setError(null)
    if (!externalUrl) { setError('Adres URL systemu jest wymagany'); return }
    if (!userEmail || !/^\S+@\S+\.\S+$/.test(userEmail)) { setError('Prawidłowy adres e-mail jest wymagany'); return }
    if (!userPassword || userPassword.length < 8) { setError('Hasło musi mieć co najmniej 8 znaków'); return }
    if (!firstTenantNip || !/^\d{10}$/.test(firstTenantNip)) { setError('NIP musi składać się z 10 cyfr'); return }
    setStep(3)
  }

  const handleStep3 = () => {
    setError(null)
    if (smtpEnabled && !smtpHost) { setError('Host SMTP jest wymagany'); return }
    if (smtpEnabled && !smtpFrom) { setError('Adres nadawcy (From) jest wymagany'); return }
    if (!smtpEnabled && (verifyEmail || resetPassword)) {
      // auto-disable email-dependent features
    }
    setStep(4)
  }

  const handleStep5 = () => {
    setError(null)
    setStep(6)
  }

  const handleApply = async () => {
    if (!setupToken) { setError('Sesja wygasła. Zaloguj się ponownie.'); return }
    setError(null)
    setLoading(true)

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

    const request: SetupApplyRequest = {
      externalBaseUrl: externalUrl.replace(/\/+$/, ''),
      kSeFBaseUrl: ksefEnv,
      adminEmail: userEmail,
      adminPassword: userPassword,
      adminFirstName: userFirstName || undefined,
      adminLastName: userLastName || undefined,
      firstTenantNip: firstTenantNip || undefined,
      firstTenantDisplayName: firstTenantDisplayName || undefined,
      registrationAllowed,
      verifyEmail: smtpEnabled ? verifyEmail : false,
      loginWithEmailAllowed: loginWithEmail,
      resetPasswordAllowed: smtpEnabled ? resetPassword : false,
      passwordPolicy: passwordPolicyValue,
      smtp,
      googleClientId: googleClientId || undefined,
      googleClientSecret: googleClientSecret || undefined,
      pushRelayUrl: pushMode === 'relay' ? pushRelayUrl || undefined : undefined,
      pushRelayApiKey: pushMode === 'relay' && pushRelayUrl !== 'https://push.open-ksef.pl'
        ? pushRelayApiKey || undefined : undefined,
      firebaseCredentialsJson: pushMode === 'firebase' ? firebaseJson || undefined : undefined,
      newKeycloakAdminPassword: newKcPassword || undefined,
    }

    try {
      const result = await applySetup(setupToken, request)
      if (!result.success) {
        setError(result.error ?? 'Konfiguracja nie powiodła się')
        return
      }
      setEncryptionKey(result.encryptionKey ?? null)
      setApiClientSecret(result.apiClientSecret ?? null)
      queryClient.setQueryData(['setup-status'], { isInitialized: true })
      toast.success('System skonfigurowany pomyślnie!')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Konfiguracja nie powiodła się')
    } finally {
      setLoading(false)
    }
  }

  const goToLogin = () => navigate('/login')

  return (
    <main className="auth-page">
      <div className="onboarding-card" style={{ maxWidth: '640px' }}>
        <StepIndicator current={step} />

        {/* Step 1: Admin Authentication */}
        {step === 1 && (
          <>
            <h1 className="onboarding-title">Konfiguracja systemu</h1>
            <p className="onboarding-subtitle">
              Zaloguj się jako administrator Keycloak, aby rozpocząć konfigurację.
            </p>
            <div className="onboarding-form">
              <div className="ui-form-group">
                <label htmlFor="setup-kc-user">Nazwa użytkownika Keycloak</label>
                <input id="setup-kc-user" data-testid="setup-kc-user" type="text"
                  value={kcUsername} onInput={e => setKcUsername((e.target as HTMLInputElement).value)} />
              </div>
              <div className="ui-form-group">
                <label htmlFor="setup-kc-pass">Hasło Keycloak</label>
                <input id="setup-kc-pass" data-testid="setup-kc-pass" type="password"
                  value={kcPassword} onInput={e => setKcPassword((e.target as HTMLInputElement).value)} />
              </div>

              <hr style={{ margin: '16px 0', border: 'none', borderTop: '1px solid var(--ui-border)' }} />

              <CollapsibleGuide title="Zmień hasło administratora Keycloak (zalecane)">
                <div className="onboarding-instruction" style={{ color: 'var(--ui-warning)', marginBottom: '12px' }}>
                  Domyślne hasło administratora Keycloak (admin/admin) stanowi ryzyko
                  bezpieczeństwa. Zalecamy zmianę hasła podczas pierwszej konfiguracji.
                </div>
                <div className="ui-form-group">
                  <label htmlFor="setup-new-kc-pass">Nowe hasło administratora Keycloak</label>
                  <input id="setup-new-kc-pass" data-testid="setup-new-kc-pass" type="password"
                    placeholder="Min. 8 znaków"
                    value={newKcPassword} onInput={e => setNewKcPassword((e.target as HTMLInputElement).value)} />
                </div>
                <div className="ui-form-group">
                  <label htmlFor="setup-new-kc-pass-confirm">Potwierdź nowe hasło</label>
                  <input id="setup-new-kc-pass-confirm" data-testid="setup-new-kc-pass-confirm" type="password"
                    value={newKcPasswordConfirm} onInput={e => setNewKcPasswordConfirm((e.target as HTMLInputElement).value)} />
                </div>
              </CollapsibleGuide>

              {!newKcPassword && (
                <div className="onboarding-instruction" style={{ color: 'var(--ui-warning)' }}>
                  Hasło administratora Keycloak nie zostanie zmienione.
                  Rozwiń sekcję powyżej, aby ustawić nowe hasło.
                </div>
              )}

              {error && <div className="ui-form-error" role="alert">⚠ {error}</div>}
            </div>
            <div className="onboarding-actions onboarding-actions--end">
              <Button data-testid="setup-next" onClick={() => void handleStep1()} disabled={loading}>
                {loading ? 'Logowanie…' : 'Dalej'}
              </Button>
            </div>
          </>
        )}

        {/* Step 2: First Account & Company */}
        {step === 2 && (
          <>
            <h1 className="onboarding-title">Pierwsze konto i firma</h1>
            <p className="onboarding-subtitle">Utwórz pierwsze konto użytkownika i dodaj firmę.</p>
            <div className="onboarding-form">
              <div className="ui-form-group">
                <label htmlFor="setup-url">Zewnętrzny adres URL systemu</label>
                <input id="setup-url" data-testid="setup-url" type="url"
                  value={externalUrl} onInput={e => setExternalUrl((e.target as HTMLInputElement).value)} />
                <span className="ui-form-hint">Publiczny adres, pod którym system będzie dostępny</span>
              </div>
              <div className="ui-form-group">
                <label htmlFor="setup-ksef-env">Środowisko KSeF</label>
                <select id="setup-ksef-env" data-testid="setup-ksef-env"
                  value={ksefEnv} onChange={e => setKsefEnv(e.target.value)}>
                  <option value="test">Test (ksef-test.mf.gov.pl)</option>
                  <option value="production">Produkcja (ksef.podatki.gov.pl)</option>
                </select>
              </div>

              <hr style={{ margin: '16px 0', border: 'none', borderTop: '1px solid var(--ui-border)' }} />

              <h3 style={{ margin: '0 0 8px' }}>Pierwsze konto użytkownika</h3>
              <div className="ui-form-group">
                <label htmlFor="setup-user-email">E-mail</label>
                <input id="setup-user-email" data-testid="setup-admin-email" type="email" placeholder="jan@firma.pl"
                  value={userEmail} onInput={e => setUserEmail((e.target as HTMLInputElement).value)} />
              </div>
              <div className="ui-form-group">
                <label htmlFor="setup-user-pass">Hasło</label>
                <input id="setup-user-pass" data-testid="setup-admin-pass" type="password" placeholder="Min. 8 znaków"
                  value={userPassword} onInput={e => setUserPassword((e.target as HTMLInputElement).value)} />
              </div>
              <div className="ui-form-group">
                <label htmlFor="setup-user-fname">Imię <span style={{ fontWeight: 400, color: 'var(--ui-text-muted)' }}>(opcjonalnie)</span></label>
                <input id="setup-user-fname" data-testid="setup-admin-fname" type="text"
                  value={userFirstName} onInput={e => setUserFirstName((e.target as HTMLInputElement).value)} />
              </div>
              <div className="ui-form-group">
                <label htmlFor="setup-user-lname">Nazwisko <span style={{ fontWeight: 400, color: 'var(--ui-text-muted)' }}>(opcjonalnie)</span></label>
                <input id="setup-user-lname" data-testid="setup-admin-lname" type="text"
                  value={userLastName} onInput={e => setUserLastName((e.target as HTMLInputElement).value)} />
              </div>

              <hr style={{ margin: '16px 0', border: 'none', borderTop: '1px solid var(--ui-border)' }} />

              <h3 style={{ margin: '0 0 8px' }}>Pierwsza firma</h3>
              <div className="ui-form-group">
                <label htmlFor="setup-tenant-nip">NIP</label>
                <input id="setup-tenant-nip" data-testid="setup-tenant-nip" type="text" placeholder="1234567890"
                  maxLength={10}
                  value={firstTenantNip} onInput={e => setFirstTenantNip((e.target as HTMLInputElement).value.replace(/\D/g, ''))} />
              </div>
              <div className="ui-form-group">
                <label htmlFor="setup-tenant-name">Nazwa firmy <span style={{ fontWeight: 400, color: 'var(--ui-text-muted)' }}>(opcjonalnie)</span></label>
                <input id="setup-tenant-name" data-testid="setup-tenant-name" type="text" placeholder="Moja Firma Sp. z o.o."
                  value={firstTenantDisplayName} onInput={e => setFirstTenantDisplayName((e.target as HTMLInputElement).value)} />
              </div>
              {error && <div className="ui-form-error" role="alert">⚠ {error}</div>}
            </div>
            <div className="onboarding-actions">
              <button type="button" className="onboarding-skip-link" onClick={() => setStep(1)}>Wstecz</button>
              <Button data-testid="setup-next" onClick={handleStep2}>Dalej</Button>
            </div>
          </>
        )}

        {/* Step 3: Auth & Email */}
        {step === 3 && (
          <>
            <h1 className="onboarding-title">Autoryzacja i e-mail</h1>
            <p className="onboarding-subtitle">Konfiguracja polityki logowania i serwera pocztowego.</p>
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
                <label htmlFor="setup-pass-policy">Polityka haseł</label>
                <select id="setup-pass-policy" data-testid="setup-pass-policy" value={passwordPolicyPreset} onChange={e => setPasswordPolicyPreset(e.target.value)}>
                  <option value="basic">Podstawowa (min. 8 znaków)</option>
                  <option value="strong">Silna (12 znaków, cyfry, duże litery, znaki specjalne)</option>
                </select>
              </div>

              <hr style={{ margin: '16px 0', border: 'none', borderTop: '1px solid var(--ui-border)' }} />

              <div className="ui-form-group">
                <label><input type="checkbox" checked={smtpEnabled} onChange={e => setSmtpEnabled(e.target.checked)} /> Skonfiguruj serwer SMTP</label>
                {!smtpEnabled && (
                  <span className="ui-form-hint" style={{ color: 'var(--ui-warning)' }}>
                    Bez SMTP weryfikacja e-mail i reset hasła będą niedostępne.
                  </span>
                )}
              </div>

              {smtpEnabled && (
                <>
                  <CollapsibleGuide title="Jak skonfigurować SMTP?">
                    <ul style={{ margin: 0, paddingLeft: '20px' }}>
                      <li><strong>Gmail:</strong> smtp.gmail.com, port 587, StartTLS, wymaga hasła aplikacji</li>
                      <li><strong>Outlook/O365:</strong> smtp.office365.com, port 587, StartTLS</li>
                      <li><strong>Własny serwer:</strong> wprowadź dane ręcznie</li>
                    </ul>
                  </CollapsibleGuide>

                  <div className="ui-form-group">
                    <label htmlFor="setup-smtp-host">Host SMTP</label>
                    <input id="setup-smtp-host" data-testid="setup-smtp-host" type="text" placeholder="smtp.gmail.com"
                      value={smtpHost} onInput={e => setSmtpHost((e.target as HTMLInputElement).value)} />
                  </div>
                  <div className="ui-form-group">
                    <label htmlFor="setup-smtp-port">Port</label>
                    <input id="setup-smtp-port" data-testid="setup-smtp-port" type="text" placeholder="587"
                      value={smtpPort} onInput={e => setSmtpPort((e.target as HTMLInputElement).value)} />
                  </div>
                  <div className="ui-form-group">
                    <label htmlFor="setup-smtp-from">Adres nadawcy (From)</label>
                    <input id="setup-smtp-from" data-testid="setup-smtp-from" type="email" placeholder="noreply@firma.pl"
                      value={smtpFrom} onInput={e => setSmtpFrom((e.target as HTMLInputElement).value)} />
                  </div>
                  <div className="ui-form-group">
                    <label htmlFor="setup-smtp-display">Nazwa wyświetlana</label>
                    <input id="setup-smtp-display" type="text" placeholder="OpenKSeF"
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
                        <label htmlFor="setup-smtp-user">Nazwa użytkownika SMTP</label>
                        <input id="setup-smtp-user" data-testid="setup-smtp-user" type="text"
                          value={smtpUser} onInput={e => setSmtpUser((e.target as HTMLInputElement).value)} />
                      </div>
                      <div className="ui-form-group">
                        <label htmlFor="setup-smtp-pass">Hasło SMTP</label>
                        <input id="setup-smtp-pass" data-testid="setup-smtp-pass" type="password"
                          value={smtpPassword} onInput={e => setSmtpPassword((e.target as HTMLInputElement).value)} />
                      </div>
                    </>
                  )}
                </>
              )}
              {error && <div className="ui-form-error" role="alert">⚠ {error}</div>}
            </div>
            <div className="onboarding-actions">
              <button type="button" className="onboarding-skip-link" onClick={() => setStep(2)}>Wstecz</button>
              <Button data-testid="setup-next" onClick={handleStep3}>Dalej</Button>
            </div>
          </>
        )}

        {/* Step 4: Security (auto-generated) */}
        {step === 4 && (
          <>
            <h1 className="onboarding-title">Bezpieczeństwo</h1>
            <p className="onboarding-subtitle">
              Klucze zostaną wygenerowane automatycznie podczas konfiguracji.
              Zostaną zapisane bezpiecznie i udostępnione między usługami.
            </p>
            <div className="onboarding-form">
              <div className="onboarding-instruction">
                Klucz szyfrowania AES-256 oraz sekret klienta API zostaną wygenerowane
                automatycznie po kliknięciu „Zastosuj" na ostatnim kroku.
                Nie musisz ich kopiować ani konfigurować ręcznie.
              </div>
              {encryptionKey && (
                <div className="ui-form-group">
                  <label>Klucz szyfrowania (wygenerowany)</label>
                  <input type="text" readOnly value={encryptionKey} data-testid="setup-encryption-key" style={{ fontFamily: 'monospace', fontSize: '12px' }} />
                </div>
              )}
              {apiClientSecret && (
                <div className="ui-form-group">
                  <label>Sekret klienta API (z Keycloak)</label>
                  <input type="text" readOnly value={apiClientSecret} data-testid="setup-api-secret" style={{ fontFamily: 'monospace', fontSize: '12px' }} />
                </div>
              )}
            </div>
            <div className="onboarding-actions">
              <button type="button" className="onboarding-skip-link" onClick={() => setStep(3)}>Wstecz</button>
              <Button data-testid="setup-next" onClick={() => setStep(5)}>Dalej</Button>
            </div>
          </>
        )}

        {/* Step 5: Optional Integrations */}
        {step === 5 && (
          <>
            <h1 className="onboarding-title">Integracje</h1>
            <p className="onboarding-subtitle">Opcjonalne integracje. Możesz je pominąć i skonfigurować później.</p>
            <div className="onboarding-form">
              <h3 style={{ margin: '0 0 8px' }}>Google OAuth</h3>
              <CollapsibleGuide title="Jak uzyskać dane Google OAuth?">
                <ol style={{ margin: 0, paddingLeft: '20px' }}>
                  <li>Przejdź do <a href="https://console.cloud.google.com/apis/credentials" target="_blank" rel="noopener noreferrer">Google Cloud Console &gt; Credentials</a></li>
                  <li>Utwórz OAuth 2.0 Client ID (typ: Aplikacja webowa)</li>
                  <li>Dodaj URI przekierowania: <code>{externalUrl}/auth/realms/openksef/broker/google/endpoint</code></li>
                  <li>Skopiuj Client ID i Client Secret poniżej</li>
                </ol>
              </CollapsibleGuide>
              <div className="ui-form-group">
                <label htmlFor="setup-google-id">Google Client ID</label>
                <input id="setup-google-id" data-testid="setup-google-id" type="text" placeholder="123456789-xxx.apps.googleusercontent.com"
                  value={googleClientId} onInput={e => setGoogleClientId((e.target as HTMLInputElement).value)} />
              </div>
              <div className="ui-form-group">
                <label htmlFor="setup-google-secret">Google Client Secret</label>
                <input id="setup-google-secret" data-testid="setup-google-secret" type="password"
                  value={googleClientSecret} onInput={e => setGoogleClientSecret((e.target as HTMLInputElement).value)} />
              </div>

              <hr style={{ margin: '16px 0', border: 'none', borderTop: '1px solid var(--ui-border)' }} />

              <h3 style={{ margin: '0 0 8px' }}>Powiadomienia push</h3>

              <div className="onboarding-instruction">
                Powiadomienia push informują użytkowników mobilnych o nowych fakturach.
                Lokalne powiadomienia przez SignalR działają zawsze (gdy aplikacja jest połączona).
                Dla powiadomień zdalnych (gdy aplikacja jest w tle) wybierz jedną z opcji poniżej.
              </div>

              <div className="ui-form-group">
                <label>
                  <input type="radio" name="pushMode" value="relay"
                    checked={pushMode === 'relay'} onChange={() => setPushMode('relay')} />
                  {' '}Relay OpenKSeF <span style={{ fontWeight: 400, color: 'var(--ui-text-muted)' }}>(zalecane)</span>
                </label>
                <span className="ui-form-hint">
                  Powiadomienia dostarczane przez serwer relay zespołu OpenKSeF. Nie wymaga konfiguracji Firebase.
                </span>
              </div>

              {pushMode === 'relay' && (
                <>
                  <div className="ui-form-group">
                    <label htmlFor="setup-relay-url">URL serwera relay</label>
                    <input id="setup-relay-url" data-testid="setup-relay-url" type="url"
                      value={pushRelayUrl} onInput={e => setPushRelayUrl((e.target as HTMLInputElement).value)} />
                  </div>
                  {pushRelayUrl === 'https://push.open-ksef.pl' ? (
                    <div className="onboarding-instruction">
                      System zostanie automatycznie zarejestrowany w serwisie relay.
                      Klucz API zostanie wygenerowany automatycznie podczas konfiguracji.
                    </div>
                  ) : (
                    <div className="ui-form-group">
                      <label htmlFor="setup-relay-key">Klucz API relay</label>
                      <input id="setup-relay-key" data-testid="setup-relay-key" type="password"
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
                  {' '}Własny projekt Firebase <span style={{ fontWeight: 400, color: 'var(--ui-text-muted)' }}>(zaawansowane)</span>
                </label>
                <span className="ui-form-hint">
                  Użyj własnego projektu Firebase do dostarczania powiadomień.
                </span>
              </div>

              {pushMode === 'firebase' && (
                <>
                  <CollapsibleGuide title="Jak uzyskać dane Firebase?">
                    <ol style={{ margin: 0, paddingLeft: '20px' }}>
                      <li>Firebase Console &gt; Project Settings &gt; Service Accounts &gt; Generate new private key</li>
                      <li>Wklej zawartość JSON poniżej</li>
                    </ol>
                  </CollapsibleGuide>
                  <div className="ui-form-group">
                    <label htmlFor="setup-firebase">Firebase Credentials JSON</label>
                    <textarea id="setup-firebase" data-testid="setup-firebase" rows={4}
                      placeholder='{"type":"service_account","project_id":"...","private_key":"..."}'
                      value={firebaseJson} onInput={e => setFirebaseJson((e.target as HTMLTextAreaElement).value)} />
                  </div>
                </>
              )}

              <div className="ui-form-group">
                <label>
                  <input type="radio" name="pushMode" value="local"
                    checked={pushMode === 'local'} onChange={() => setPushMode('local')} />
                  {' '}Tylko lokalne (SignalR)
                </label>
                <span className="ui-form-hint">
                  Brak zdalnych powiadomień push. Użytkownicy otrzymują powiadomienia tylko gdy aplikacja jest aktywnie połączona z serwerem.
                </span>
              </div>

              {error && <div className="ui-form-error" role="alert">⚠ {error}</div>}
            </div>
            <div className="onboarding-actions">
              <button type="button" className="onboarding-skip-link" onClick={() => setStep(4)}>Wstecz</button>
              <Button data-testid="setup-next" onClick={handleStep5}>Dalej</Button>
            </div>
          </>
        )}

        {/* Step 6: Summary + Apply */}
        {step === 6 && !encryptionKey && (
          <>
            <h1 className="onboarding-title">Podsumowanie</h1>
            <p className="onboarding-subtitle">Sprawdź konfigurację i zastosuj.</p>
            <div className="onboarding-form">
              <table style={{ width: '100%', fontSize: '14px', borderCollapse: 'collapse' }}>
                <tbody>
                  <tr><td style={{ padding: '4px 8px', fontWeight: 600 }}>URL systemu</td><td style={{ padding: '4px 8px' }}>{externalUrl}</td></tr>
                  <tr><td style={{ padding: '4px 8px', fontWeight: 600 }}>Środowisko KSeF</td><td style={{ padding: '4px 8px' }}>{ksefEnv === 'production' ? 'Produkcja' : 'Test'}</td></tr>
                  <tr><td style={{ padding: '4px 8px', fontWeight: 600 }}>Użytkownik</td><td style={{ padding: '4px 8px' }}>{userEmail}</td></tr>
                  <tr><td style={{ padding: '4px 8px', fontWeight: 600 }}>Firma (NIP)</td><td style={{ padding: '4px 8px' }}>{firstTenantNip}{firstTenantDisplayName ? ` — ${firstTenantDisplayName}` : ''}</td></tr>
                  <tr><td style={{ padding: '4px 8px', fontWeight: 600 }}>Rejestracja</td><td style={{ padding: '4px 8px' }}>{registrationAllowed ? 'Tak' : 'Nie'}</td></tr>
                  <tr><td style={{ padding: '4px 8px', fontWeight: 600 }}>SMTP</td><td style={{ padding: '4px 8px' }}>{smtpEnabled ? smtpHost : 'Pominięto'}</td></tr>
                  <tr><td style={{ padding: '4px 8px', fontWeight: 600 }}>Google OAuth</td><td style={{ padding: '4px 8px' }}>{googleClientId ? 'Skonfigurowano' : 'Pominięto'}</td></tr>
                  <tr><td style={{ padding: '4px 8px', fontWeight: 600 }}>Powiadomienia push</td><td style={{ padding: '4px 8px' }}>
                    {pushMode === 'relay' ? `Relay (${pushRelayUrl})` : pushMode === 'firebase' ? 'Firebase (własny)' : 'Tylko lokalne (SignalR)'}
                  </td></tr>
                  <tr><td style={{ padding: '4px 8px', fontWeight: 600 }}>Hasło admina Keycloak</td><td style={{ padding: '4px 8px' }}>
                    {newKcPassword ? 'Zostanie zmienione' : 'Bez zmian'}
                  </td></tr>
                </tbody>
              </table>
              {error && <div className="ui-form-error" role="alert">⚠ {error}</div>}
            </div>
            <div className="onboarding-actions">
              <button type="button" className="onboarding-skip-link" onClick={() => setStep(5)}>Wstecz</button>
              <Button data-testid="setup-apply" onClick={() => void handleApply()} disabled={loading}>
                {loading ? 'Konfiguracja…' : 'Zastosuj'}
              </Button>
            </div>
          </>
        )}

        {/* Step 6: Success */}
        {step === 6 && encryptionKey && (
          <div className="onboarding-success" data-testid="setup-success">
            <div className="onboarding-success__icon">✓</div>
            <h1 className="onboarding-success__title">System skonfigurowany!</h1>
            <p className="onboarding-success__detail">
              Zaloguj się jako <strong>{userEmail}</strong>, aby rozpocząć korzystanie z OpenKSeF.
            </p>
            <div className="onboarding-actions onboarding-actions--end" style={{ width: '100%' }}>
              <Button data-testid="setup-go-login" size="lg" onClick={goToLogin}>
                Przejdź do logowania
              </Button>
            </div>
          </div>
        )}
      </div>
    </main>
  )
}
