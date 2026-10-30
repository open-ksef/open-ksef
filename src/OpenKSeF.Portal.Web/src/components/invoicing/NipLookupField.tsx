import { useState, type ReactElement } from 'react'

import { lookupNip } from '@/api/endpoints/nipLookup'
import { ApiError } from '@/api/errors'
import type { NipLookupResponse } from '@/api/types'
import { isValidNip } from '@/invoicing/nip'

interface NipLookupFieldProps {
  id: string
  value: string
  onChange: (value: string) => void
  onFound: (result: NipLookupResponse) => void
  serverError?: string
  required?: boolean
}

type LookupState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'found'; vatStatus: string; at: Date }
  | { kind: 'notFound' }
  | { kind: 'unavailable' }

const timeFormat = new Intl.DateTimeFormat('pl-PL', { dateStyle: 'short', timeStyle: 'short' })

export function NipLookupField({ id, value, onChange, onFound, serverError, required }: NipLookupFieldProps): ReactElement {
  const [state, setState] = useState<LookupState>({ kind: 'idle' })
  const valid = isValidNip(value)
  const localError = value.length > 0 && !valid ? 'Niepoprawny NIP (suma kontrolna)' : null
  const error = serverError ?? localError
  const errorId = `${id}-error`
  const statusId = `${id}-status`

  async function handleLookup(): Promise<void> {
    setState({ kind: 'loading' })
    try {
      const result = await lookupNip(value)
      onFound(result)
      setState({ kind: 'found', vatStatus: result.vatStatus, at: new Date() })
    } catch (lookupError) {
      setState(lookupError instanceof ApiError && lookupError.status === 404 ? { kind: 'notFound' } : { kind: 'unavailable' })
    }
  }

  return (
    <div className="ui-form-group">
      <label htmlFor={id}>NIP nabywcy</label>
      <div className="inv-input-group">
        <input
          id={id}
          type="text"
          inputMode="numeric"
          autoComplete="off"
          aria-required={required}
          value={value}
          aria-invalid={error ? 'true' : 'false'}
          aria-describedby={[error ? errorId : null, state.kind !== 'idle' ? statusId : null].filter(Boolean).join(' ') || undefined}
          onChange={(event) => {
            onChange(event.target.value.replace(/\D+/g, '').slice(0, 10))
            setState({ kind: 'idle' })
          }}
        />
        <button
          type="button"
          className="ui-button ui-button--secondary ui-button--md"
          disabled={!valid || state.kind === 'loading'}
          aria-busy={state.kind === 'loading'}
          onClick={() => void handleLookup()}
        >
          {state.kind === 'loading' ? <span className="inv-spinner" aria-hidden="true" /> : null}
          Pobierz z NIP
        </button>
      </div>
      {error ? (
        <span className="inv-field-error" id={errorId}>
          {error}
        </span>
      ) : null}
      <span id={statusId} role="status">
        {state.kind === 'found' ? (
          <span className={`inv-badge inv-badge--${state.vatStatus === 'Czynny' ? 'success' : 'warning'}`}>
            {state.vatStatus === 'Czynny' ? 'Czynny podatnik VAT' : state.vatStatus} · biała lista {timeFormat.format(state.at)}
          </span>
        ) : null}
        {state.kind === 'notFound' ? (
          <span className="inv-badge inv-badge--warning">Nie ma na białej liście — wpisz dane ręcznie</span>
        ) : null}
        {state.kind === 'unavailable' ? (
          <span className="inv-field-error">Biała lista nie odpowiada, wpisz dane ręcznie</span>
        ) : null}
      </span>
    </div>
  )
}
