import type { ReactElement } from 'react'

import { addDaysIso } from '@/invoicing/format'

interface DatesValue {
  issueDate: string
  saleDate: string
  dueDate: string
}

interface DatesSectionProps extends DatesValue {
  onChange: (value: DatesValue) => void
  fieldErrors?: Record<string, string>
}

export function DatesSection({ issueDate, saleDate, dueDate, onChange, fieldErrors = {} }: DatesSectionProps): ReactElement {
  const current = { issueDate, saleDate, dueDate }

  function dateField(id: string, key: keyof DatesValue, label: string, onValue: (next: string) => void) {
    const error = fieldErrors[key]
    return (
      <div className="ui-form-group inv-date">
        <label htmlFor={id}>{label}</label>
        <input
          id={id}
          type="date"
          value={current[key]}
          aria-invalid={error ? 'true' : 'false'}
          aria-describedby={error ? `${id}-error` : undefined}
          onChange={(event) => onValue(event.target.value)}
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
    <>
      <div className="inv-fields">
        {dateField('issue-date', 'issueDate', 'Data wystawienia', (next) =>
          onChange({ ...current, issueDate: next, saleDate: saleDate === issueDate ? next : saleDate }),
        )}
        {dateField('sale-date', 'saleDate', 'Data sprzedaży', (next) => onChange({ ...current, saleDate: next }))}
        {dateField('due-date', 'dueDate', 'Termin płatności', (next) => onChange({ ...current, dueDate: next }))}
        <div className="ui-form-group inv-days">
          <span id="due-days-label" className="inv-field-label">
            Termin za (dni)
          </span>
          <div className="inv-chips" role="group" aria-labelledby="due-days-label">
            {[7, 14, 30].map((days) => {
              const target = issueDate ? addDaysIso(issueDate, days) : ''
              return (
                <button
                  key={days}
                  type="button"
                  aria-label={`${days} dni`}
                  disabled={!issueDate}
                  aria-pressed={target !== '' && dueDate === target}
                  onClick={() => onChange({ ...current, dueDate: target })}
                >
                  {days}
                </button>
              )
            })}
          </div>
        </div>
      </div>
      {saleDate && issueDate && saleDate !== issueDate ? (
        <p className="inv-help">Data sprzedaży inna niż data wystawienia trafi na fakturę osobno.</p>
      ) : null}
    </>
  )
}
