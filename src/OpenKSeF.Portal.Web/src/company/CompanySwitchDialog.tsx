import { useEffect, useRef, useState, type KeyboardEvent, type ReactElement } from 'react'

import type { KsefEnvironment, TenantResponse } from '@/api/types'
import { ErrorBanner } from '@/components/ErrorBanner'
import { CompanyBox } from './CompanyBox'
import { companyName } from './CompanyContext'

interface CompanySwitchDialogProps {
  current: TenantResponse
  target: TenantResponse
  environment: KsefEnvironment | null
  onStay: () => void
  onClear: () => void
  onSave: () => Promise<void>
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
          {environment ? 'Zmienić firmę podczas wystawiania?' : 'Zmienić firmę?'}
        </h2>
        {environment ? (
          <>
            <p>Nie przeniesiemy nabywcy ani pozycji do innej firmy. Faktura ma innego sprzedawcę, więc zaczniesz od pustego formularza.</p>
            <CompanyBox caption="Teraz wystawiasz jako sprzedawca" company={current} environment={environment} />
            <CompanyBox caption="Nowy sprzedawca" company={target} environment={environment} />
          </>
        ) : (
          <p>
            Masz niezapisane zmiany w firmie {companyName(current)}. Nie przeniesiemy ich do firmy {companyName(target)}.
          </p>
        )}
        {failed ? (
          <ErrorBanner
            message={
              environment
                ? 'Nie udało się zapisać szkicu. Firma nie została zmieniona. Sprawdź błędy w formularzu.'
                : 'Nie udało się zapisać zmian. Firma nie została zmieniona. Sprawdź błędy w formularzu.'
            }
          />
        ) : null}
        <div className="company-switch-actions">
          <button type="button" className="ui-button ui-button--outline ui-button--md" data-testid="company-switch-stay" disabled={pending} onClick={onStay}>
            Zostań przy tej firmie
          </button>
          <button type="button" className="ui-button ui-button--outline ui-button--md" data-testid="company-switch-clear" disabled={pending} onClick={onClear}>
            {environment ? 'Wyczyść i zmień' : 'Odrzuć zmiany i zmień'}
          </button>
          <button
            ref={saveRef}
            type="button"
            className="ui-button ui-button--primary ui-button--md"
            data-testid="company-switch-save"
            disabled={pending}
            onClick={() => void save()}
          >
            {pending ? 'Zapisywanie…' : environment ? 'Zapisz szkic i zmień' : 'Zapisz i zmień'}
          </button>
        </div>
      </div>
    </div>
  )
}
