import type { ReactElement } from 'react'

import { formatMoney } from '@/invoicing/format'

interface MoneyDisplayProps {
  amount: number
  currency?: string
}

export function MoneyDisplay({ amount, currency = 'PLN' }: MoneyDisplayProps): ReactElement {
  return <span className="num">{formatMoney(amount, currency)}</span>
}
