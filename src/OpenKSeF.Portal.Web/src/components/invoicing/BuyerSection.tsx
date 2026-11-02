import type { ReactElement } from 'react'

import type { BuyerKind, NipLookupResponse } from '@/api/types'
import type { BuyerFormValue } from '@/invoicing/form'
import { NipLookupField } from './NipLookupField'

interface BuyerSectionProps {
  value: BuyerFormValue
  onChange: (value: BuyerFormValue) => void
  fieldErrors?: Record<string, string>
}

const kinds: { kind: BuyerKind; label: string }[] = [
  { kind: 'Company', label: 'Firma' },
  { kind: 'Person', label: 'Osoba prywatna' },
]

export function BuyerSection({ value, onChange, fieldErrors = {} }: BuyerSectionProps): ReactElement {
  const isPerson = value.kind === 'Person'

  function applyLookup(result: NipLookupResponse): void {
    const next = {
      name: result.name,
      addressLine1: result.addressLine1 ?? '',
      addressLine2: result.addressLine2 ?? '',
    }
    const overwrites = (Object.keys(next) as (keyof typeof next)[]).some(
      (key) => value[key].trim() !== '' && value[key] !== next[key],
    )
    if (overwrites && !window.confirm('Zastąpić wpisane dane nabywcy danymi z białej listy?')) {
      return
    }
    onChange({ ...value, ...next })
  }

  function textField(key: 'name' | 'addressLine1' | 'addressLine2', label: string): ReactElement {
    const id = `buyer-${key}`
    const error = fieldErrors[`buyer.${key}`]
    return (
      <div className="ui-form-group">
        <label htmlFor={id}>
          {label}
          <span aria-hidden="true"> *</span>
        </label>
        <input
          id={id}
          type="text"
          aria-required="true"
          aria-invalid={error ? 'true' : 'false'}
          aria-describedby={error ? `${id}-error` : undefined}
          value={value[key]}
          onChange={(event) => onChange({ ...value, [key]: event.target.value })}
        />
        {error ? (
          <span className="inv-field-error" id={`${id}-error`}>
            {error}
          </span>
        ) : null}
      </div>
    )
  }

  return (
    <section className="inv-card" aria-labelledby="buyer-title">
      <h2 id="buyer-title">Nabywca</h2>
      <div className="inv-segmented" role="group" aria-label="Rodzaj nabywcy">
        {kinds.map((option) => (
          <button
            key={option.kind}
            type="button"
            aria-pressed={value.kind === option.kind}
            onClick={() => onChange({ ...value, kind: option.kind })}
          >
            {option.label}
          </button>
        ))}
      </div>
      {isPerson ? (
        <p className="inv-help">Nabywca dostaje PDF z kodem KOD I.</p>
      ) : (
        <NipLookupField
          id="buyer-nip"
          value={value.nip}
          required
          serverError={fieldErrors['buyer.nip']}
          onChange={(nip) => onChange({ ...value, nip })}
          onFound={applyLookup}
        />
      )}
      {textField('name', isPerson ? 'Imię i nazwisko' : 'Nazwa')}
      <div className="inv-grid">
        {textField('addressLine1', 'Adres (ulica, numer)')}
        {textField('addressLine2', 'Kod pocztowy i miejscowość')}
      </div>
    </section>
  )
}
