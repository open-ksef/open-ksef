import type { ChangeEvent, ReactElement } from 'react'

import type { MoneyValue } from './MoneyDisplay'

interface MoneyInputProps {
  value: MoneyValue
  onChange: (value: MoneyValue) => void
  currency?: string
  pricingMode?: 'Net' | 'Gross'
  label?: string
  id?: string
  disabled?: boolean
}

export function MoneyInput({
  value,
  onChange,
  currency,
  pricingMode,
  label = 'Kwota',
  id,
  disabled = false,
}: MoneyInputProps): ReactElement {
  const activeCurrency = currency ?? value.currency
  const inputId = id ?? 'money-input'

  return (
    <label className="money-input" htmlFor={inputId}>
      <span className="money-input__label">
        {label}
        {pricingMode ? ` (${pricingMode === 'Net' ? 'netto' : 'brutto'})` : ''}
      </span>
      <span className="money-input__control">
        <input
          id={inputId}
          type="text"
          inputMode="decimal"
          value={formatEditableAmount(value.amount)}
          onChange={(event) => handleChange(event, activeCurrency, onChange)}
          disabled={disabled}
        />
        <span className="money-input__currency" aria-hidden="true">
          {activeCurrency}
        </span>
      </span>
    </label>
  )
}

function handleChange(
  event: ChangeEvent<HTMLInputElement>,
  currency: string,
  onChange: (value: MoneyValue) => void,
): void {
  const normalizedValue = event.target.value.replace(/\s+/g, '').replace(',', '.')

  if (normalizedValue.length === 0) {
    onChange({ amount: 0, currency })
    return
  }

  const parsedValue = Number(normalizedValue)
  if (!Number.isNaN(parsedValue)) {
    onChange({ amount: parsedValue, currency })
  }
}

function formatEditableAmount(amount: number): string {
  return Number.isInteger(amount) ? String(amount) : String(amount).replace('.', ',')
}
