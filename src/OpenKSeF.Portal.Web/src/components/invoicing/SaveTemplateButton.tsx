import { useQueryClient } from '@tanstack/react-query'
import { useState, type ReactElement } from 'react'

import { readValidationEnvelope } from '@/api/errors'
import { ConfirmDialog } from '@/components/ConfirmDialog'

interface SaveTemplateButtonProps {
  defaultName: string
  disabled?: boolean
  save: (name: string) => Promise<unknown>
  label?: string
  ariaLabel?: string
  message?: string
}

const SAVE_MESSAGE = 'Szablon zapamięta nabywcę, pozycje, płatność i termin. Numer i daty nadamy przy nowej fakturze.'

export function SaveTemplateButton({ defaultName, disabled, save, label = 'Zapisz jako szablon', ariaLabel, message = SAVE_MESSAGE }: SaveTemplateButtonProps): ReactElement {
  const [open, setOpen] = useState(false)
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const [saved, setSaved] = useState<string | null>(null)
  const queryClient = useQueryClient()

  async function submit(): Promise<void> {
    const trimmed = name.trim()
    if (!trimmed) {
      setError('Podaj nazwę szablonu')
      return
    }
    setPending(true)
    try {
      await save(trimmed)
      void queryClient.invalidateQueries({ queryKey: ['invoice-templates'] })
      setOpen(false)
      setSaved(trimmed)
    } catch (saveError) {
      const envelope = await readValidationEnvelope(saveError)
      setError(envelope?.errors[0]?.message ?? (saveError instanceof Error ? saveError.message : 'Nie udało się zapisać szablonu'))
    } finally {
      setPending(false)
    }
  }

  return (
    <>
      <button
        type="button"
        className="ui-button ui-button--outline ui-button--sm"
        disabled={disabled}
        aria-label={ariaLabel}
        onClick={() => {
          setName(defaultName.slice(0, 100))
          setError(null)
          setSaved(null)
          setOpen(true)
        }}
      >
        {label}
      </button>
      <span className="inv-help" role="status">
        {saved ? `Zapisano szablon „${saved}”.` : ''}
      </span>
      <ConfirmDialog
        open={open}
        title={label}
        message={message}
        confirmLabel="Zapisz"
        pendingLabel="Zapisywanie…"
        isPending={pending}
        onConfirm={() => void submit()}
        onCancel={() => setOpen(false)}
      >
        <div className="ui-form-group">
          <label htmlFor="template-name">Nazwa szablonu</label>
          <input
            id="template-name"
            type="text"
            maxLength={100}
            data-autofocus
            aria-required="true"
            aria-invalid={error ? 'true' : 'false'}
            aria-describedby={error ? 'template-name-error' : undefined}
            value={name}
            onChange={(event) => setName(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') {
                event.preventDefault()
                void submit()
              }
            }}
          />
          {error ? (
            <span className="inv-field-error" id="template-name-error">
              {error}
            </span>
          ) : null}
        </div>
      </ConfirmDialog>
    </>
  )
}
