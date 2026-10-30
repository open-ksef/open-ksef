import type { ReactElement } from 'react'

import { ART_113, type ExemptionChoice } from '@/invoicing/form'

interface ExemptionBasisFieldProps {
  choice: ExemptionChoice
  text: string
  onChange: (value: { choice: ExemptionChoice; text: string }) => void
  serverError?: string
}

const choices: { value: ExemptionChoice; label: string }[] = [
  { value: 'art113', label: `${ART_113} (zwolnienie podmiotowe)` },
  { value: 'Act', label: 'Inny przepis ustawy o VAT' },
  { value: 'Directive', label: 'Przepis dyrektywy' },
  { value: 'Other', label: 'Inna podstawa' },
]

export function ExemptionBasisField({ choice, text, onChange, serverError }: ExemptionBasisFieldProps): ReactElement {
  const needsText = choice !== 'art113'
  const error = serverError ?? (needsText && text.trim() === '' ? 'Podaj podstawę zwolnienia z VAT' : undefined)

  return (
    <div className="inv-grid">
      <div className="ui-form-group">
        <label htmlFor="exemption-choice">Podstawa zwolnienia z VAT</label>
        <select
          id="exemption-choice"
          value={choice}
          aria-describedby={error && !needsText ? 'exemption-error' : undefined}
          onChange={(event) => onChange({ choice: event.target.value as ExemptionChoice, text })}
        >
          {choices.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </div>
      {needsText ? (
        <div className="ui-form-group">
          <label htmlFor="exemption-text">Przepis lub opis podstawy</label>
          <input
            id="exemption-text"
            type="text"
            placeholder="np. art. 43 ust. 1 pkt 37 ustawy o VAT"
            value={text}
            aria-invalid={error ? 'true' : 'false'}
            aria-describedby={error ? 'exemption-error' : undefined}
            onChange={(event) => onChange({ choice, text: event.target.value })}
          />
        </div>
      ) : null}
      {error ? (
        <span className="inv-field-error" id="exemption-error">
          {error}
        </span>
      ) : null}
    </div>
  )
}
