import type { ReactElement } from 'react'

import type { InvoiceResponse } from '@/api/types'
import { Icon } from '@/components/Icon'
import { formatDate } from '@/invoicing/format'

export function DueDate({ row, testId }: { row: InvoiceResponse; testId: string }): ReactElement {
  return row.isOverdue ? (
    <span data-testid={testId} className="received-overdue">
      <Icon name="clock" size={14} /> {formatDate(row.dueDate)} po terminie
    </span>
  ) : (
    <span data-testid={testId}>{formatDate(row.dueDate)}</span>
  )
}
