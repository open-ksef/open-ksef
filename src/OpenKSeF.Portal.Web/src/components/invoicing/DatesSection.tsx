import type { ReactElement } from 'react'

interface DatesSectionProps {
  issueDate: string
  saleDate: string
  onChange: (value: { issueDate: string; saleDate: string }) => void
  fieldErrors?: Record<string, string>
}

export function DatesSection({ issueDate, saleDate, onChange, fieldErrors = {} }: DatesSectionProps): ReactElement {
  function dateField(id: string, key: 'issueDate' | 'saleDate', label: string, value: string, onValue: (next: string) => void) {
    const error = fieldErrors[key]
    return (
      <div className="ui-form-group">
        <label htmlFor={id}>{label}</label>
        <input
          id={id}
          type="date"
          value={value}
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
    <section className="inv-card" aria-labelledby="dates-title">
      <h2 id="dates-title">Daty</h2>
      <div className="inv-grid">
        {dateField('issue-date', 'issueDate', 'Data wystawienia', issueDate, (next) =>
          onChange({ issueDate: next, saleDate: saleDate === issueDate ? next : saleDate }),
        )}
        {dateField('sale-date', 'saleDate', 'Data sprzedaży', saleDate, (next) => onChange({ issueDate, saleDate: next }))}
      </div>
      {saleDate && issueDate && saleDate !== issueDate ? (
        <p className="inv-help">Data sprzedaży inna niż data wystawienia trafi na fakturę osobno.</p>
      ) : null}
    </section>
  )
}
