import type { ChangeEvent, ReactElement } from 'react'

const defaultVatRates = ['23%', '8%', '5%', '0%', 'zw', 'np'] as const

interface VatRateSelectProps {
  value: string
  onChange: (value: string) => void
  label?: string
  id?: string
  options?: readonly string[]
  disabled?: boolean
}

export function VatRateSelect({
  value,
  onChange,
  label = 'Stawka VAT',
  id = 'vat-rate-select',
  options = defaultVatRates,
  disabled = false,
}: VatRateSelectProps): ReactElement {
  return (
    <label className="vat-rate-select" htmlFor={id}>
      <span className="vat-rate-select__label">{label}</span>
      <select
        id={id}
        value={value}
        onChange={(event: ChangeEvent<HTMLSelectElement>) => onChange(event.target.value)}
        disabled={disabled}
      >
        {options.map((rate) => (
          <option key={rate} value={rate}>
            {rate}
          </option>
        ))}
      </select>
    </label>
  )
}
