import type { ReactElement, ReactNode } from 'react'

import { Button } from './Button'

interface FilterBarProps {
  children: ReactNode
  onApply: () => void
  onReset?: () => void
  applyButtonTestId?: string
  resetButtonTestId?: string
  applyLabel?: string
  resetLabel?: string
}

export function FilterBar({
  children,
  onApply,
  onReset,
  applyButtonTestId,
  resetButtonTestId,
  applyLabel = 'Zastosuj',
  resetLabel = 'Resetuj',
}: FilterBarProps): ReactElement {
  return (
    <section className="ui-filter-bar">
      <div className="ui-filter-bar__inputs">{children}</div>
      <div className="ui-filter-bar__actions">
        <Button variant="primary" onClick={onApply} data-testid={applyButtonTestId}>{applyLabel}</Button>
        {onReset ? <Button variant="outline" onClick={onReset} data-testid={resetButtonTestId}>{resetLabel}</Button> : null}
      </div>
    </section>
  )
}
