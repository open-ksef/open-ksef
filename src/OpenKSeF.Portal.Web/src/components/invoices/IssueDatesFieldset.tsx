import type { ChangeEvent, ReactElement } from 'react'

export interface IssueDatesValue {
  issueDate: string
  saleDate?: string | null
  dueDate?: string | null
}

interface IssueDatesFieldsetProps {
  value: IssueDatesValue
  onChange: (value: IssueDatesValue) => void
  disabled?: boolean
  mode?: 'full' | 'compact'
}

export function IssueDatesFieldset({
  value,
  onChange,
  disabled = false,
  mode = 'full',
}: IssueDatesFieldsetProps): ReactElement {
  if (mode === 'compact') {
    return (
      <DateField
        id="issue-date"
        label="Data wystawienia"
        value={value.issueDate}
        onChange={(issueDate) => onChange({ ...value, issueDate })}
        disabled={disabled}
      />
    )
  }

  return (
    <fieldset className="issue-dates-fieldset" disabled={disabled}>
      <legend className="issue-dates-fieldset__title">Daty dokumentu</legend>
      <DateField
        id="issue-date"
        label="Data wystawienia"
        value={value.issueDate}
        onChange={(issueDate) => onChange({ ...value, issueDate })}
      />
      <DateField
        id="sale-date"
        label="Data sprzedaży"
        value={value.saleDate ?? ''}
        onChange={(saleDate) => onChange({ ...value, saleDate: saleDate || null })}
      />
      <DateField
        id="due-date"
        label="Termin płatności"
        value={value.dueDate ?? ''}
        onChange={(dueDate) => onChange({ ...value, dueDate: dueDate || null })}
      />
    </fieldset>
  )
}

interface DateFieldProps {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
  disabled?: boolean
}

function DateField({ id, label, value, onChange, disabled }: DateFieldProps): ReactElement {
  return (
    <label className="issue-dates-fieldset__field" htmlFor={id}>
      <span className="issue-dates-fieldset__label">{label}</span>
      <input
        id={id}
        type="date"
        value={value}
        disabled={disabled}
        onChange={(event: ChangeEvent<HTMLInputElement>) => onChange(event.target.value)}
      />
    </label>
  )
}
