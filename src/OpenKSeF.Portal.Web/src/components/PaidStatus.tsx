import type { ReactElement } from 'react'

import type { InvoiceResponse } from '@/api/types'
import { Icon } from '@/components/Icon'
import { formatWarsawDate } from '@/invoicing/format'

export const SELLER_PAID_LABEL = 'Opłacona wg sprzedawcy'

export function PaidStatus({ row, testId }: { row: InvoiceResponse; testId: string }): ReactElement {
  const paidBy = row.paidByName ? ` ${formatWarsawDate(row.paidAt)} przez ${row.paidByName}` : ''
  return (
    <span data-testid={testId} className={row.isPaid ? 'received-paid' : 'received-paid received-paid--unpaid'}>
      <Icon name={row.isPaid ? 'circle-check' : 'circle-dashed'} size={14} />
      {row.isPaid ? (row.paidBySeller ? SELLER_PAID_LABEL : `Opłacona${paidBy}`) : 'Nieopłacona'}
    </span>
  )
}
