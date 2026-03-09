import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useRef, useState, type ReactElement } from 'react'
import { useNavigate } from 'react-router-dom'
import toast from 'react-hot-toast'

import { createTenant } from '@/api/endpoints/tenants'
import {
  addOrUpdateCredential,
  addOrUpdateCertificateCredential,
  addOrUpdatePemCertificateCredential,
  forceCredentialSync,
} from '@/api/endpoints/credentials'
import type { CredentialType } from '@/api/types'
import { Button } from '@/components/Button'

type Step = 1 | 2 | 3

interface CompanyForm {
  nip: string
  displayName: string
  notificationEmail: string
}

const INITIAL_FORM: CompanyForm = {
  nip: '',
  displayName: '',
  notificationEmail: '',
}

function StepIndicator({ current }: { current: Step }) {
  const steps = [1, 2, 3] as const
  return (
    <div className="onboarding-stepper" data-testid="onboarding-step-indicator">
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

export function OnboardingPage(): ReactElement {
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [step, setStep] = useState<Step>(1)
  const [form, setForm] = useState<CompanyForm>(INITIAL_FORM)
  const [validationError, setValidationError] = useState<string | null>(null)
  const [tenantId, setTenantId] = useState<string | null>(null)
  const [tenantLabel, setTenantLabel] = useState('')
  const [credentialType, setCredentialType] = useState<CredentialType>('Token')
  const [token, setToken] = useState('')
  const [certFormat, setCertFormat] = useState<'pem' | 'pfx'>('pem')
  const [certFile, setCertFile] = useState<File | null>(null)
  const [keyFile, setKeyFile] = useState<File | null>(null)
  const [certPassword, setCertPassword] = useState('')
  const [tokenSkipped, setTokenSkipped] = useState(false)
  const [syncResult, setSyncResult] = useState<{ fetchedInvoices: number; newInvoices: number } | null>(null)
  const certInputRef = useRef<HTMLInputElement>(null)
  const keyInputRef = useRef<HTMLInputElement>(null)

  const createTenantMutation = useMutation({
    mutationFn: createTenant,
    onSuccess: (data) => {
      setTenantId(data.id)
      setTenantLabel(data.displayName || data.nip)
      void queryClient.invalidateQueries({ queryKey: ['tenants'] })
      setStep(2)
    },
    onError: (err) => {
      toast.error(`Błąd tworzenia firmy: ${err.message}`)
    },
  })

  const addCredentialMutation = useMutation({
    mutationFn: async ({ tid }: { tid: string }) => {
      if (credentialType === 'Certificate') {
        if (certFormat === 'pem') {
          if (!certFile || !keyFile) throw new Error('Pliki certyfikatu i klucza są wymagane')
          return addOrUpdatePemCertificateCredential(tid, certFile, keyFile, certPassword)
        }
        if (!certFile) throw new Error('Plik certyfikatu jest wymagany')
        return addOrUpdateCertificateCredential(tid, certFile, certPassword)
      }
      return addOrUpdateCredential(tid, token.trim())
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['credentials'] })
    },
    onError: (err) => {
      toast.error(`Błąd zapisywania danych logowania: ${err.message}`)
    },
  })

  const syncMutation = useMutation({
    mutationFn: (tid: string) => forceCredentialSync(tid),
    onSuccess: (result) => {
      setSyncResult({ fetchedInvoices: result.fetchedInvoices, newInvoices: result.newInvoices })
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      void queryClient.invalidateQueries({ queryKey: ['invoices'] })
    },
    onError: () => {
      // sync failure is non-critical at onboarding
    },
  })

  const handleStep1Submit = () => {
    setValidationError(null)

    if (!/^\d{10}$/.test(form.nip)) {
      setValidationError('NIP musi zawierać dokładnie 10 cyfr')
      return
    }

    if (form.notificationEmail && !/^\S+@\S+\.\S+$/.test(form.notificationEmail)) {
      setValidationError('Adres e-mail do powiadomień jest nieprawidłowy')
      return
    }

    createTenantMutation.mutate({
      nip: form.nip,
      displayName: form.displayName || null,
      notificationEmail: form.notificationEmail || null,
    })
  }

  const handleStep2Submit = async () => {
    if (!tenantId) return
    setValidationError(null)

    if (credentialType === 'Token' && !token.trim()) {
      setValidationError('Token KSeF jest wymagany')
      return
    }

    if (credentialType === 'Certificate') {
      if (certFormat === 'pem') {
        if (!certFile) {
          setValidationError('Plik certyfikatu (.crt) jest wymagany')
          return
        }
        if (!keyFile) {
          setValidationError('Plik klucza prywatnego (.key) jest wymagany')
          return
        }
      } else {
        if (!certFile) {
          setValidationError('Plik certyfikatu (.pfx/.p12) jest wymagany')
          return
        }
        if (!certPassword) {
          setValidationError('Hasło certyfikatu jest wymagane')
          return
        }
      }
    }

    try {
      await addCredentialMutation.mutateAsync({ tid: tenantId })
      setTokenSkipped(false)
      setStep(3)
      syncMutation.mutate(tenantId)
    } catch {
      // error handled by mutation
    }
  }

  const handleSkipToken = () => {
    setTokenSkipped(true)
    setStep(3)
  }

  const handleGoToDashboard = () => {
    void queryClient.invalidateQueries({ queryKey: ['onboarding-status'] })
    navigate('/')
  }

  return (
    <main className="auth-page">
      <div className="onboarding-card">
        <StepIndicator current={step} />

        {step === 1 && (
          <>
            <h1 className="onboarding-title">Dane firmy</h1>
            <p className="onboarding-subtitle">
              Podaj dane swojej firmy, aby rozpocząć korzystanie z KSeF.
            </p>

            <div className="onboarding-form">
              <div className="ui-form-group">
                <label htmlFor="onboarding-nip">NIP</label>
                <input
                  id="onboarding-nip"
                  data-testid="onboarding-nip"
                  type="text"
                  inputMode="numeric"
                  placeholder="1234567890"
                  value={form.nip}
                  onInput={(e) =>
                    setForm((f) => ({ ...f, nip: (e.target as HTMLInputElement).value }))
                  }
                />
                <span className="ui-form-hint">10-cyfrowy numer identyfikacji podatkowej</span>
              </div>

              <div className="ui-form-group">
                <label htmlFor="onboarding-display-name">
                  Nazwa wyświetlana{' '}
                  <span style={{ fontWeight: 400, color: 'var(--ui-text-muted)' }}>(opcjonalnie)</span>
                </label>
                <input
                  id="onboarding-display-name"
                  data-testid="onboarding-display-name"
                  type="text"
                  placeholder="np. Acme Sp. z o.o."
                  value={form.displayName}
                  onInput={(e) =>
                    setForm((f) => ({ ...f, displayName: (e.target as HTMLInputElement).value }))
                  }
                />
              </div>

              <div className="ui-form-group">
                <label htmlFor="onboarding-email">
                  E-mail do powiadomień{' '}
                  <span style={{ fontWeight: 400, color: 'var(--ui-text-muted)' }}>(opcjonalnie)</span>
                </label>
                <input
                  id="onboarding-email"
                  data-testid="onboarding-email"
                  type="email"
                  placeholder="faktury@firma.pl"
                  value={form.notificationEmail}
                  onInput={(e) =>
                    setForm((f) => ({
                      ...f,
                      notificationEmail: (e.target as HTMLInputElement).value,
                    }))
                  }
                />
              </div>

              {validationError && (
                <div className="ui-form-error" role="alert">
                  ⚠ {validationError}
                </div>
              )}
            </div>

            <div className="onboarding-actions onboarding-actions--end">
              <Button
                data-testid="onboarding-next"
                onClick={handleStep1Submit}
                disabled={createTenantMutation.isPending}
              >
                {createTenantMutation.isPending ? 'Zapisywanie…' : 'Dalej'}
              </Button>
            </div>
          </>
        )}

        {step === 2 && (
          <>
            <h1 className="onboarding-title">Połącz z KSeF</h1>
            <p className="onboarding-subtitle">
              Dodaj dane uwierzytelniające KSeF, aby umożliwić synchronizację faktur.
            </p>

            <div className="onboarding-credential-type" data-testid="onboarding-credential-type">
              <button
                type="button"
                data-testid="onboarding-credential-type-token"
                className={`onboarding-credential-type__option${credentialType === 'Token' ? ' onboarding-credential-type__option--active' : ''}`}
                onClick={() => setCredentialType('Token')}
              >
                Token autoryzacyjny
              </button>
              <button
                type="button"
                data-testid="onboarding-credential-type-certificate"
                className={`onboarding-credential-type__option${credentialType === 'Certificate' ? ' onboarding-credential-type__option--active' : ''}`}
                onClick={() => setCredentialType('Certificate')}
              >
                Certyfikat KSeF
              </button>
            </div>

            {credentialType === 'Token' && (
              <>
                <div
                  className="onboarding-deprecation-warning"
                  data-testid="onboarding-token-deprecation-warning"
                >
                  Od 1 stycznia 2027 r. tokeny autoryzacyjne nie będą obsługiwane przez KSeF.
                  Zalecamy konfigurację certyfikatu KSeF.
                </div>

                <div
                  className="onboarding-instruction"
                  data-testid="onboarding-instruction"
                >
                  Jak uzyskać token KSeF:
                  <ol>
                    <li>
                      Przejdź na{' '}
                      <a
                        href="https://ksef-test.mf.gov.pl"
                        target="_blank"
                        rel="noopener noreferrer"
                      >
                        ksef-test.mf.gov.pl
                      </a>{' '}
                      (lub{' '}
                      <a
                        href="https://ksef.podatki.gov.pl"
                        target="_blank"
                        rel="noopener noreferrer"
                      >
                        ksef.podatki.gov.pl
                      </a>{' '}
                      dla produkcji)
                    </li>
                    <li>Zaloguj się danymi firmy (e-Dowód, profil zaufany lub certyfikat)</li>
                    <li>Przejdź do sekcji &quot;Tokeny autoryzacyjne&quot;</li>
                    <li>
                      Wygeneruj nowy token z uprawnieniami:{' '}
                      <strong>odczyt faktur</strong> i <strong>wystawianie faktur</strong>
                    </li>
                    <li>Skopiuj wygenerowany token i wklej poniżej</li>
                  </ol>
                </div>

                <div className="onboarding-form">
                  <div className="ui-form-group">
                    <label htmlFor="onboarding-token">Token KSeF</label>
                    <textarea
                      id="onboarding-token"
                      data-testid="onboarding-token"
                      placeholder="Wklej token uwierzytelniający KSeF tutaj..."
                      value={token}
                      onInput={(e) => setToken((e.target as HTMLTextAreaElement).value)}
                    />
                  </div>
                </div>
              </>
            )}

            {credentialType === 'Certificate' && (
              <div className="onboarding-form">
                <div className="onboarding-instruction">
                  Zaloguj się na{' '}
                  <a href="https://ksef.podatki.gov.pl" target="_blank" rel="noopener noreferrer">
                    ksef.podatki.gov.pl
                  </a>{' '}
                  (lub{' '}
                  <a href="https://ksef-test.mf.gov.pl" target="_blank" rel="noopener noreferrer">
                    ksef-test.mf.gov.pl
                  </a>{' '}
                  dla testu), pobierz certyfikat (.crt) i klucz prywatny (.key).
                  Certyfikat musi być zarejestrowany w KSeF dla podanego NIP.
                </div>

                <div
                  className="onboarding-credential-type"
                  data-testid="onboarding-cert-format"
                  style={{ marginBottom: '12px' }}
                >
                  <button
                    type="button"
                    data-testid="onboarding-cert-format-pem"
                    className={`onboarding-credential-type__option${certFormat === 'pem' ? ' onboarding-credential-type__option--active' : ''}`}
                    onClick={() => { setCertFormat('pem'); setCertFile(null); setKeyFile(null); setCertPassword('') }}
                  >
                    CRT + KEY
                  </button>
                  <button
                    type="button"
                    data-testid="onboarding-cert-format-pfx"
                    className={`onboarding-credential-type__option${certFormat === 'pfx' ? ' onboarding-credential-type__option--active' : ''}`}
                    onClick={() => { setCertFormat('pfx'); setCertFile(null); setKeyFile(null); setCertPassword('') }}
                  >
                    PFX / P12
                  </button>
                </div>

                {certFormat === 'pem' && (
                  <>
                    <div className="ui-form-group">
                      <label htmlFor="onboarding-certificate-file">Plik certyfikatu (.crt / .cer / .pem)</label>
                      <input
                        id="onboarding-certificate-file"
                        data-testid="onboarding-certificate-file"
                        ref={certInputRef}
                        type="file"
                        accept=".crt,.cer,.pem"
                        onChange={(e) => setCertFile(e.target.files?.[0] ?? null)}
                      />
                      {certFile && (
                        <span className="ui-form-hint">Wybrany plik: {certFile.name}</span>
                      )}
                    </div>

                    <div className="ui-form-group">
                      <label htmlFor="onboarding-key-file">Klucz prywatny (.key / .pem)</label>
                      <input
                        id="onboarding-key-file"
                        data-testid="onboarding-key-file"
                        ref={keyInputRef}
                        type="file"
                        accept=".key,.pem"
                        onChange={(e) => setKeyFile(e.target.files?.[0] ?? null)}
                      />
                      {keyFile && (
                        <span className="ui-form-hint">Wybrany plik: {keyFile.name}</span>
                      )}
                    </div>

                    <div className="ui-form-group">
                      <label htmlFor="onboarding-certificate-password">
                        Hasło klucza prywatnego{' '}
                        <span style={{ fontWeight: 400, color: 'var(--ui-text-muted)' }}>(jeśli zaszyfrowany)</span>
                      </label>
                      <input
                        id="onboarding-certificate-password"
                        data-testid="onboarding-certificate-password"
                        type="password"
                        placeholder="Hasło do klucza prywatnego"
                        value={certPassword}
                        onInput={(e) => setCertPassword((e.target as HTMLInputElement).value)}
                      />
                    </div>
                  </>
                )}

                {certFormat === 'pfx' && (
                  <>
                    <div className="ui-form-group">
                      <label htmlFor="onboarding-certificate-file">Plik certyfikatu (.pfx / .p12)</label>
                      <input
                        id="onboarding-certificate-file"
                        data-testid="onboarding-certificate-file"
                        ref={certInputRef}
                        type="file"
                        accept=".pfx,.p12"
                        onChange={(e) => setCertFile(e.target.files?.[0] ?? null)}
                      />
                      {certFile && (
                        <span className="ui-form-hint">Wybrany plik: {certFile.name}</span>
                      )}
                    </div>

                    <div className="ui-form-group">
                      <label htmlFor="onboarding-certificate-password">Hasło certyfikatu</label>
                      <input
                        id="onboarding-certificate-password"
                        data-testid="onboarding-certificate-password"
                        type="password"
                        placeholder="Hasło do pliku PFX"
                        value={certPassword}
                        onInput={(e) => setCertPassword((e.target as HTMLInputElement).value)}
                      />
                    </div>
                  </>
                )}
              </div>
            )}

            {validationError && (
              <div className="ui-form-error" role="alert">
                ⚠ {validationError}
              </div>
            )}

            <div className="onboarding-actions">
              <button
                type="button"
                className="onboarding-skip-link"
                data-testid="onboarding-skip-token"
                onClick={handleSkipToken}
              >
                Pomiń ten krok
              </button>
              <Button
                data-testid="onboarding-next"
                onClick={() => void handleStep2Submit()}
                disabled={addCredentialMutation.isPending}
              >
                {addCredentialMutation.isPending ? 'Zapisywanie…' : 'Dalej'}
              </Button>
            </div>
          </>
        )}

        {step === 3 && (
          <div className="onboarding-success" data-testid="onboarding-success">
            <div className="onboarding-success__icon">✓</div>
            <h1 className="onboarding-success__title">Gotowe!</h1>
            <p className="onboarding-success__detail">
              Firma <strong>{tenantLabel}</strong> (NIP: {form.nip}) została skonfigurowana.
            </p>

            {!tokenSkipped && syncMutation.isPending && (
              <div className="onboarding-success__sync">
                Trwa pierwsza synchronizacja faktur…
              </div>
            )}

            {!tokenSkipped && syncResult && (
              <div className="onboarding-success__sync">
                Synchronizacja zakończona — pobrano {syncResult.fetchedInvoices} faktur
                {syncResult.newInvoices > 0 && `, w tym ${syncResult.newInvoices} nowych`}.
              </div>
            )}

            {tokenSkipped && (
              <div className="onboarding-success__warning">
                Dane logowania KSeF nie zostały dodane. Synchronizacja faktur nie będzie działać do momentu
                dodania danych logowania na stronie &quot;Dane logowania&quot;.
              </div>
            )}

            <div className="onboarding-actions onboarding-actions--end" style={{ width: '100%' }}>
              <Button
                data-testid="onboarding-go-dashboard"
                size="lg"
                onClick={handleGoToDashboard}
              >
                Przejdź do pulpitu
              </Button>
            </div>
          </div>
        )}
      </div>
    </main>
  )
}
