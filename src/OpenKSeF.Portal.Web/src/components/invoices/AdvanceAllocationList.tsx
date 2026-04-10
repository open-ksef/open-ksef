import type { ReactElement } from 'react'

import { MoneyDisplay, type MoneyValue } from './MoneyDisplay'

export interface AdvanceAllocation {
  advanceInvoiceId: string
  advanceDocumentNumber: string
  settledAmount: MoneyValue
}

interface AdvanceAllocationListProps {
  allocations: AdvanceAllocation[]
}

export function AdvanceAllocationList({ allocations }: AdvanceAllocationListProps): ReactElement | null {
  if (allocations.length === 0) {
    return null
  }

  return (
    <section className="advance-allocation-list" aria-label="Rozliczone zaliczki">
      <h2 className="advance-allocation-list__title">Rozliczone zaliczki</h2>
      <ul className="advance-allocation-list__items">
        {allocations.map((allocation) => (
          <li
            key={allocation.advanceInvoiceId}
            className="advance-allocation-list__item"
            data-testid="advance-row"
          >
            <span className="advance-allocation-list__number">{allocation.advanceDocumentNumber}</span>
            <span className="advance-allocation-list__amount">
              <MoneyDisplay value={allocation.settledAmount} />
            </span>
          </li>
        ))}
      </ul>
    </section>
  )
}
