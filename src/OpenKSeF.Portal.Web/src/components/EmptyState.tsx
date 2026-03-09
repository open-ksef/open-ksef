import type { ReactElement } from 'react'

import { Button } from './Button'

interface EmptyStateAction {
  label: string
  onClick: () => void
}

interface EmptyStateProps {
  title: string
  message: string
  action?: EmptyStateAction
}

export function EmptyState({ title, message, action }: EmptyStateProps): ReactElement {
  return (
    <section className="ui-empty-state" role="status">
      <div className="ui-empty-state__icon" aria-hidden="true">○</div>
      <h2>{title}</h2>
      <p>{message}</p>
      {action ? <Button onClick={action.onClick}>{action.label}</Button> : null}
    </section>
  )
}
