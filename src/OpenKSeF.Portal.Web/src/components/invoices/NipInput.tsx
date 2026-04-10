import type { ChangeEvent, ReactElement } from 'react'

import { nipSchema } from '@/api/schemas/invoice'

interface NipInputProps {
  value: string
  onChange: (value: string) => void
  label?: string
  id?: string
  disabled?: boolean
}

export function NipInput({
  value,
  onChange,
  label = 'NIP',
  id = 'nip-input',
  disabled = false,
}: NipInputProps): ReactElement {
  const validationResult = nipSchema.safeParse(value)
  const errorMessage = value.length === 0 || validationResult.success ? null : 'Wprowadź poprawny numer NIP.'
  const errorId = `${id}-error`

  return (
    <label className="nip-input" htmlFor={id}>
      <span className="nip-input__label">{label}</span>
      <input
        id={id}
        type="text"
        inputMode="numeric"
        value={value}
        onChange={(event) => handleChange(event, onChange)}
        aria-invalid={errorMessage ? 'true' : 'false'}
        aria-describedby={errorMessage ? errorId : undefined}
        disabled={disabled}
      />
      {errorMessage ? (
        <span className="nip-input__error" id={errorId}>
          {errorMessage}
        </span>
      ) : null}
    </label>
  )
}

function handleChange(event: ChangeEvent<HTMLInputElement>, onChange: (value: string) => void): void {
  onChange(event.target.value.replace(/\D+/g, '').slice(0, 10))
}
