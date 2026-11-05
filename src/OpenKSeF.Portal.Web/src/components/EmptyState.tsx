import type { ReactElement, ReactNode } from 'react'

import { Button } from './Button'
import { Icon, type IconName } from './Icon'

interface EmptyStateAction {
  label: string
  onClick: () => void
}

interface EmptyStateProps {
  title: string
  message: string
  action?: EmptyStateAction
  icon?: IconName
  actions?: ReactNode
}

export function EmptyState({ title, message, action, icon, actions }: EmptyStateProps): ReactElement {
  return (
    <section className={icon ? 'ui-empty-state ui-empty-state--setup' : 'ui-empty-state'} role="status">
      {icon ? null : <div className="ui-empty-state__icon" aria-hidden="true">○</div>}
      <h2>
        {icon ? <Icon name={icon} size={20} /> : null}
        {title}
      </h2>
      <p>{message}</p>
      {action ? <Button onClick={action.onClick}>{action.label}</Button> : null}
      {actions}
    </section>
  )
}
