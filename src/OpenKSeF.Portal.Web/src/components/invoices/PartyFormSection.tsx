import type { ChangeEvent, ReactElement } from 'react'

import { NipInput } from './NipInput'
import type { PartyValue } from './PartyCard'

interface PartyFormSectionProps {
  value: PartyValue
  onChange: (value: PartyValue) => void
  showNip: boolean
  title?: string
  disabled?: boolean
}

export function PartyFormSection({
  value,
  onChange,
  showNip,
  title = 'Dane strony',
  disabled = false,
}: PartyFormSectionProps): ReactElement {
  return (
    <fieldset className="party-form-section" disabled={disabled}>
      <legend className="party-form-section__title">{title}</legend>
      <label className="party-form-section__field" htmlFor={`${title}-name`}>
        <span className="party-form-section__label">Nazwa</span>
        <input
          id={`${title}-name`}
          type="text"
          value={value.name}
          onChange={(event: ChangeEvent<HTMLInputElement>) => onChange({ ...value, name: event.target.value })}
        />
      </label>
      {showNip ? (
        <NipInput
          id={`${title}-nip`}
          label="NIP"
          value={value.nip ?? ''}
          onChange={(nip) => onChange({ ...value, nip })}
          disabled={disabled}
        />
      ) : null}
    </fieldset>
  )
}
