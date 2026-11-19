import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type FormEvent, type ReactElement } from 'react'
import toast from 'react-hot-toast'
import { useNavigate } from 'react-router-dom'

import { claimTenant } from '@/api/endpoints/tenants'
import { Button } from '@/components/Button'
import { nipError } from '@/invoicing/nip'
import { companyName } from './CompanyContext'

interface ClaimCompanyFormProps {
  initialNip: string
  onCancel: () => void
}

export function ClaimCompanyForm({ initialNip, onCancel }: ClaimCompanyFormProps): ReactElement {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [nip, setNip] = useState(initialNip)
  const [token, setToken] = useState('')
  const [confirming, setConfirming] = useState(false)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onCancel()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onCancel])

  async function claim(): Promise<void> {
    const request = { nip, token: token.trim() }
    setToken('')
    setPending(true)
    try {
      const claimed = await claimTenant(request)
      await Promise.all(
        [['tenants'], ['dashboard'], ['credentials'], ['onboarding-status']].map((queryKey) => queryClient.invalidateQueries({ queryKey })),
      )
      toast.success(`Przejęto firmę ${companyName(claimed)}.`)
      void navigate(`/?tenantId=${encodeURIComponent(claimed.id)}`)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Nie udało się przejąć firmy.')
      setConfirming(false)
      setPending(false)
    }
  }

  function onSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault()
    if (confirming) {
      void claim()
      return
    }
    const invalid = nipError(nip) ?? (token.trim() ? null : 'Token KSeF jest wymagany')
    setError(invalid)
    if (!invalid) setConfirming(true)
  }

  return (
    <div
      className="ui-modal-backdrop"
      onClick={(event) => {
        if (event.target === event.currentTarget) onCancel()
      }}
    >
      <form className="ui-modal" role="dialog" aria-modal="true" aria-labelledby="claim-company-title" onSubmit={onSubmit} noValidate data-testid="claim-form">
        <div className="ui-modal-header">
          <h2 className="ui-modal-title" id="claim-company-title">
            Przejmij firmę
          </h2>
        </div>
        <div className="ui-modal-body">
          {confirming ? (
            <div data-testid="claim-confirm">
              <p>
                Przejmujesz firmę z NIP <span className="mono">{nip}</span>.
              </p>
              <p className="company-notice" role="status">
                Obecni właściciele firmy stracą do niej dostęp, a Ty zostaniesz jedynym właścicielem. Administratorzy, księgowi i pracownicy zachowają swoje role.
                Faktury i dane firmy zostają bez zmian.
              </p>
            </div>
          ) : (
            <>
              <div className="ui-form-group">
                <label htmlFor="claim-nip">NIP</label>
                <input
                  id="claim-nip"
                  data-testid="claim-nip"
                  className="mono nip-input"
                  type="text"
                  inputMode="numeric"
                  maxLength={10}
                  value={nip}
                  onChange={(event) => setNip(event.target.value)}
                />
              </div>
              <div className="ui-form-group">
                <label htmlFor="claim-token">Token KSeF</label>
                <input
                  id="claim-token"
                  data-testid="claim-token"
                  className="mono"
                  type="password"
                  autoComplete="off"
                  maxLength={256}
                  autoFocus
                  aria-describedby="claim-token-hint"
                  value={token}
                  onChange={(event) => setToken(event.target.value)}
                />
                <span className="ui-form-hint" id="claim-token-hint">
                  Token z uprawnieniem Zarządzanie uprawnieniami dla tego NIP-u, z Aplikacji Podatnika KSeF. Wygeneruj osobny token: sprawdzimy go w KSeF i nie zapiszemy.
                </span>
              </div>
            </>
          )}
          {error ? (
            <div className="ui-form-error" role="alert" data-testid="claim-error">
              {error}
            </div>
          ) : null}
        </div>
        <div className="ui-modal-footer">
          <Button variant="outline" onClick={confirming ? () => setConfirming(false) : onCancel} disabled={pending}>
            {confirming ? 'Wróć' : 'Anuluj'}
          </Button>
          <Button type="submit" data-testid="claim-submit" disabled={pending}>
            {pending ? 'Sprawdzanie w KSeF…' : confirming ? 'Przejmij firmę' : 'Dalej'}
          </Button>
        </div>
      </form>
    </div>
  )
}
