import type { ReactElement } from 'react'

import type { VatRateCode } from '@/api/types'
import { VAT_RATES } from '@/invoicing/totals'

interface VatRateSelectProps {
  id: string
  value: VatRateCode
  onChange: (value: VatRateCode) => void
  label?: string
  disabled?: boolean
  describedBy?: string
}

export function VatRateSelect({
  id,
  value,
  onChange,
  label = 'Stawka VAT',
  disabled = false,
  describedBy,
}: VatRateSelectProps): ReactElement {
  return (
    <select
      id={id}
      aria-label={label}
      aria-describedby={describedBy}
      value={value}
      disabled={disabled}
      onChange={(event) => onChange(event.target.value as VatRateCode)}
    >
      {VAT_RATES.map((rate) => (
        <option key={rate.code} value={rate.code}>
          {rate.label}
        </option>
      ))}
    </select>
  )
}
