import type { ReactElement } from 'react'

type BadgeStatus = 'success' | 'warning' | 'error'

interface StatusBadgeProps {
  status: BadgeStatus
  label: string
}

export function StatusBadge({ status, label }: StatusBadgeProps): ReactElement {
  return <span className={`ui-status-badge ui-status-badge--${status}`}>{label}</span>
}
