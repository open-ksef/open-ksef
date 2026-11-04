import type { ReactElement } from 'react'

import type { BuyerKind, NipLookupResponse } from '@/api/types'
import { COUNTRY_OPTIONS, countryName, isEuCountry } from '@/invoicing/foreign'
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
  { kind: 'Foreign', label: 'Firma zagraniczna' },
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
      <div className={key === 'name' ? 'ui-form-group inv-buyer-name' : 'ui-form-group'}>
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
      <div className="inv-card-head">
        <h2 id="buyer-title">Nabywca</h2>
        <div className="inv-chips" role="group" aria-label="Rodzaj nabywcy">
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
      </div>
      {isPerson ? (
        <p className="inv-help">Nabywca dostaje PDF z kodem KOD I.</p>
      ) : value.kind === 'Foreign' ? (
        <ForeignBuyerFields value={value} fieldErrors={fieldErrors} onChange={onChange} />
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
      <div className="inv-fields">
        {textField('name', isPerson ? 'Imię i nazwisko' : 'Nazwa')}
        {textField('addressLine1', 'Adres (ulica, numer)')}
        {textField('addressLine2', 'Kod pocztowy i miejscowość')}
      </div>
    </section>
  )
}

function ForeignBuyerFields({ value, onChange, fieldErrors = {} }: BuyerSectionProps): ReactElement {
  const isEu = isEuCountry(value.countryCode)
  const countryError = fieldErrors['buyer.countryCode']
  const idError = fieldErrors['buyer.foreignId'] ?? fieldErrors['buyer.vatUe'] ?? fieldErrors['buyer.nip']
  const options =
    value.countryCode && !COUNTRY_OPTIONS.some((option) => option.code === value.countryCode)
      ? [{ code: value.countryCode, name: countryName(value.countryCode) }, ...COUNTRY_OPTIONS]
      : COUNTRY_OPTIONS

  return (
    <>
      <p className="inv-help">Nabywca nie dostanie faktury z KSeF. Wyślij mu PDF z kodem KOD I.</p>
      <div className="inv-fields">
        <div className="ui-form-group">
          <label htmlFor="buyer-countryCode">
            Kraj<span aria-hidden="true"> *</span>
          </label>
          <select
            id="buyer-countryCode"
            aria-required="true"
            aria-invalid={countryError ? 'true' : 'false'}
            aria-describedby={countryError ? 'buyer-countryCode-error' : undefined}
            value={value.countryCode}
            onChange={(event) => onChange({ ...value, countryCode: event.target.value, noTaxId: !isEuCountry(event.target.value) && value.noTaxId })}
          >
            <option value="">Wybierz kraj</option>
            {options.map((option) => (
              <option key={option.code} value={option.code}>
                {option.name}
              </option>
            ))}
          </select>
          {countryError ? (
            <span className="inv-field-error" id="buyer-countryCode-error">
              {countryError}
            </span>
          ) : null}
        </div>
        <div className="ui-form-group">
          <label htmlFor="buyer-foreignId">
            {isEu ? 'Numer VAT UE' : 'Numer podatkowy'}
            {isEu ? <span aria-hidden="true"> *</span> : null}
          </label>
          <input
            id="buyer-foreignId"
            type="text"
            className="mono"
            aria-required={isEu}
            aria-invalid={idError ? 'true' : 'false'}
            aria-describedby={idError ? 'buyer-foreignId-error' : undefined}
            disabled={value.noTaxId}
            value={value.noTaxId ? '' : value.foreignId}
            onChange={(event) => onChange({ ...value, foreignId: event.target.value })}
          />
          {idError ? (
            <span className="inv-field-error" id="buyer-foreignId-error">
              {idError}
            </span>
          ) : null}
          {isEu ? <span className="inv-help">Z kodem kraju lub bez niego.</span> : null}
        </div>
      </div>
      {isEu ? null : (
        <label className="inv-check">
          <input type="checkbox" checked={value.noTaxId} onChange={(event) => onChange({ ...value, noTaxId: event.target.checked })} />
          Bez numeru podatkowego
        </label>
      )}
    </>
  )
}
