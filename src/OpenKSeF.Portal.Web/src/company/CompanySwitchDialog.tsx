import { useEffect, useRef, useState, type KeyboardEvent, type ReactElement } from 'react'

import type { KsefEnvironment, TenantResponse } from '@/api/types'
import { ErrorBanner } from '@/components/ErrorBanner'
import { EnvBadge } from '@/components/invoicing/KsefStatusBadge'
import { companyName } from './CompanyContext'

interface CompanySwitchDialogProps {
  current: TenantResponse
  target: TenantResponse
  environment: KsefEnvironment
  onStay: () => void
  onClear: () => void
  onSave: () => Promise<void>
}

function SellerBox({ caption, company, environment }: { caption: string; company: TenantResponse; environment: KsefEnvironment }): ReactElement {
  return (
    <div className="company-switch-seller">
      <span className="company-switch-seller__caption">{caption}</span>
      <b>{companyName(company)}</b>
      <span className="company-nip">NIP {company.nip}</span> <EnvBadge environment={environment} />
    </div>
  )
}

export function CompanySwitchDialog({ current, target, environment, onStay, onClear, onSave }: CompanySwitchDialogProps): ReactElement {
  const [pending, setPending] = useState(false)
  const [failed, setFailed] = useState(false)
  const saveRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    saveRef.current?.focus()
    return () => document.querySelector<HTMLElement>('[data-testid="company-card"]')?.focus()
  }, [])

  useEffect(() => {
    if (pending) return
    const handleKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') onStay()
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [pending, onStay])

  function keepFocusInside(event: KeyboardEvent<HTMLDivElement>): void {
    if (event.key !== 'Tab') return
    const buttons = [...event.currentTarget.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')]
    const edge = event.shiftKey ? buttons[0] : buttons[buttons.length - 1]
    if (document.activeElement === edge) {
      event.preventDefault()
      ;(event.shiftKey ? buttons[buttons.length - 1] : buttons[0])?.focus()
    }
  }

  async function save(): Promise<void> {
    setPending(true)
    setFailed(false)
    try {
      await onSave()
    } catch {
      setFailed(true)
      setPending(false)
    }
  }

  return (
    <div className="ui-modal-backdrop">
      <div
        className="ui-modal company-switch-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="company-switch-title"
        data-testid="company-switch-dialog"
        onKeyDown={keepFocusInside}
      >
        <h2 className="ui-modal-title" id="company-switch-title">
          Zmienić firmę podczas wystawiania?
        </h2>
        <p>Nie przeniesiemy nabywcy ani pozycji do innej firmy. Faktura ma innego sprzedawcę, więc zaczniesz od pustego formularza.</p>
        <SellerBox caption="Teraz wystawiasz jako sprzedawca" company={current} environment={environment} />
        <SellerBox caption="Nowy sprzedawca" company={target} environment={environment} />
        {failed ? <ErrorBanner message="Nie udało się zapisać szkicu. Firma nie została zmieniona. Sprawdź błędy w formularzu." /> : null}
        <div className="company-switch-actions">
          <button type="button" className="ui-button ui-button--outline ui-button--md" data-testid="company-switch-stay" disabled={pending} onClick={onStay}>
            Zostań przy tej firmie
          </button>
          <button type="button" className="ui-button ui-button--outline ui-button--md" data-testid="company-switch-clear" disabled={pending} onClick={onClear}>
            Wyczyść i zmień
          </button>
          <button
            ref={saveRef}
            type="button"
            className="ui-button ui-button--primary ui-button--md"
            data-testid="company-switch-save"
            disabled={pending}
            onClick={() => void save()}
          >
            {pending ? 'Zapisywanie…' : 'Zapisz szkic i zmień'}
          </button>
        </div>
      </div>
    </div>
  )
}
