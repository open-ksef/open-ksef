import type { ChangeEvent, ReactElement } from 'react'

const defaultCurrencies = ['PLN', 'EUR', 'USD', 'GBP', 'CHF', 'CZK'] as const

interface CurrencySelectProps {
  value: string
  onChange: (value: string) => void
  label?: string
  id?: string
  options?: readonly string[]
  disabled?: boolean
}

export function CurrencySelect({
  value,
  onChange,
  label = 'Waluta',
  id = 'currency-select',
  options = defaultCurrencies,
  disabled = false,
}: CurrencySelectProps): ReactElement {
  return (
    <label className="currency-select" htmlFor={id}>
      <span className="currency-select__label">{label}</span>
      <select
        id={id}
        value={value}
        onChange={(event: ChangeEvent<HTMLSelectElement>) => onChange(event.target.value)}
        disabled={disabled}
      >
        {options.map((currency) => (
          <option key={currency} value={currency}>
            {currency}
          </option>
        ))}
      </select>
    </label>
  )
}
