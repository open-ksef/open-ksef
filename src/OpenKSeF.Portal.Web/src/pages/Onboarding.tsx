import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type ReactElement } from 'react'
import toast from 'react-hot-toast'
import { useNavigate } from 'react-router-dom'

import { addOrUpdateCredential, forceCredentialSync } from '@/api/endpoints/credentials'
import { createTenant } from '@/api/endpoints/tenants'
import { BrandMark } from '@/components/BrandMark'
import { Button } from '@/components/Button'
import { Icon } from '@/components/Icon'

type Step = 1 | 2 | 3

const STEPS: Step[] = [1, 2, 3]
const STEP_NAMES: Record<Step, string> = { 1: 'Dane firmy', 2: 'Połącz z KSeF', 3: 'Gotowe' }

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

export function OnboardingPage(): ReactElement {
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [step, setStep] = useState<Step>(1)
  const [form, setForm] = useState<CompanyForm>(INITIAL_FORM)
  const [validationError, setValidationError] = useState<string | null>(null)
  const [tenantId, setTenantId] = useState<string | null>(null)
  const [tenantLabel, setTenantLabel] = useState('')
  const [token, setToken] = useState('')
  const [leaving, setLeaving] = useState(false)

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
    mutationFn: (tid: string) => addOrUpdateCredential(tid, token.trim()),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['credentials'] })
    },
    onError: (err) => {
      toast.error(`Błąd zapisywania tokenu KSeF: ${err.message}`)
    },
  })

  const syncMutation = useMutation({
    mutationFn: (tid: string) => forceCredentialSync(tid),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      void queryClient.invalidateQueries({ queryKey: ['invoices'] })
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

  const handleStep2Submit = () => {
    if (!tenantId) return
    setValidationError(null)

    if (!token.trim()) {
      setValidationError('Token KSeF jest wymagany')
      return
    }

    addCredentialMutation.mutate(tenantId, {
      onSuccess: () => {
        setStep(3)
        syncMutation.mutate(tenantId)
      },
    })
  }

  // The guard keeps a stale "no company" status for 30 s and would send the user straight back here.
  const goToStart = async () => {
    setLeaving(true)
    await queryClient.invalidateQueries({ queryKey: ['onboarding-status'] })
    void navigate('/')
  }

  return (
    <div className="wiz">
      <div className="wiz__brand">
        <BrandMark size={24} />
        <span>OpenKSeF</span>
      </div>
      <main className="wiz__card">
        <ol className="wiz__steps" aria-label="Kroki" data-testid="onboarding-step-indicator">
          {STEPS.map((item) => (
            <li key={item} className={item === step ? 'wiz__step wiz__step--current' : 'wiz__step'} aria-current={item === step ? 'step' : undefined}>
              <span className="wiz__n">{item < step ? <Icon name="check" size={14} /> : item}</span>
              {STEP_NAMES[item]}
            </li>
          ))}
        </ol>

        {step === 1 && (
          <>
            <h1>Dane firmy</h1>
            <p className="wiz__lead">Podaj dane swojej firmy, aby rozpocząć korzystanie z KSeF.</p>

            <div className="onboarding-form">
              <div className="ui-form-group">
                <label htmlFor="onboarding-nip">NIP</label>
                <input
                  id="onboarding-nip"
                  data-testid="onboarding-nip"
                  className="mono nip-input"
                  type="text"
                  inputMode="numeric"
                  maxLength={10}
                  value={form.nip}
                  onChange={(e) => setForm((f) => ({ ...f, nip: e.target.value }))}
                />
              </div>

              <div className="ui-form-group">
                <label htmlFor="onboarding-display-name">Nazwa firmy (opcjonalnie)</label>
                <input
                  id="onboarding-display-name"
                  data-testid="onboarding-display-name"
                  type="text"
                  value={form.displayName}
                  onChange={(e) => setForm((f) => ({ ...f, displayName: e.target.value }))}
                />
              </div>

              <div className="ui-form-group">
                <label htmlFor="onboarding-email">E-mail do powiadomień (opcjonalnie)</label>
                <input
                  id="onboarding-email"
                  aria-describedby="onboarding-email-hint"
                  data-testid="onboarding-email"
                  type="email"
                  value={form.notificationEmail}
                  onChange={(e) => setForm((f) => ({ ...f, notificationEmail: e.target.value }))}
                />
                <span className="ui-form-hint" id="onboarding-email-hint">E-mail wysyłamy tylko wtedy, gdy administrator serwera skonfigurował pocztę.</span>
              </div>

              {validationError && (
                <div className="ui-form-error" role="alert">
                  {validationError}
                </div>
              )}
            </div>

            <div className="wiz__actions">
              <span />
              <Button data-testid="onboarding-next" onClick={handleStep1Submit} disabled={createTenantMutation.isPending}>
                {createTenantMutation.isPending ? 'Zapisywanie…' : 'Dalej'}
              </Button>
            </div>
          </>
        )}

        {step === 2 && (
          <>
            <h1>Połącz z KSeF</h1>
            <p className="wiz__lead">Dodaj token KSeF, aby pobierać i wystawiać faktury.</p>

            <div className="onboarding-form">
              <div className="ui-form-group">
                <label htmlFor="onboarding-token">Token KSeF</label>
                <input
                  id="onboarding-token"
                  data-testid="onboarding-token"
                  className="mono"
                  type="password"
                  autoComplete="new-password"
                  placeholder="Wklej token uwierzytelniający KSeF tutaj..."
                  value={token}
                  onChange={(e) => setToken(e.target.value)}
                />
                <span className="ui-form-hint" data-testid="onboarding-instruction">
                  Token z uprawnieniem odczytu i zapisu, z Aplikacji Podatnika KSeF. Bez zapisu nie wystawisz faktur.
                </span>
              </div>

              {validationError && (
                <div className="ui-form-error" role="alert">
                  {validationError}
                </div>
              )}
            </div>

            <div className="wiz__actions">
              <button type="button" className="wiz__skip" data-testid="onboarding-skip-token" disabled={leaving} onClick={() => void goToStart()}>
                Pomiń ten krok
              </button>
              <Button data-testid="onboarding-next" onClick={handleStep2Submit} disabled={addCredentialMutation.isPending}>
                {addCredentialMutation.isPending ? 'Zapisywanie…' : 'Dalej'}
              </Button>
            </div>
          </>
        )}

        {step === 3 && (
          <div data-testid="onboarding-success">
            <h1>Gotowe</h1>
            <p className="wiz__lead">
              Firma <b>{tenantLabel}</b> (NIP: <span className="mono">{form.nip}</span>) została skonfigurowana.
            </p>

            <p className="wiz__sync" role="status">
              {syncMutation.isPending ? (
                <>
                  <Icon name="loader" /> Trwa pierwsza synchronizacja faktur…
                </>
              ) : null}
              {syncMutation.data
                ? `Synchronizacja zakończona: pobrano ${syncMutation.data.fetchedInvoices}, nowe ${syncMutation.data.newInvoices}.`
                : null}
              {syncMutation.isError ? 'Pierwsze pobranie się nie udało. Uruchomisz je ponownie przyciskiem Pobierz z KSeF.' : null}
            </p>

            <div className="wiz__actions">
              <span />
              <Button data-testid="onboarding-go-dashboard" disabled={leaving} onClick={() => void goToStart()}>
                Przejdź na Start
              </Button>
            </div>
          </div>
        )}
      </main>
    </div>
  )
}
