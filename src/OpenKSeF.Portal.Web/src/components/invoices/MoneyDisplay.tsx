import type { ReactElement } from 'react'

export interface MoneyValue {
  amount: number
  currency: string
}

interface MoneyDisplayProps {
  value: MoneyValue
}

export function MoneyDisplay({ value }: MoneyDisplayProps): ReactElement {
  const formattedAmount = value.amount.toLocaleString('pl-PL', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })

  return (
    <span className="money-display" aria-label={`${formattedAmount} ${value.currency}`}>
      {formattedAmount} {value.currency}
    </span>
  )
}
