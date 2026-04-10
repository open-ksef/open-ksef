import type { ReactElement } from 'react'

import type { DocumentStatus } from '@/api/schemas/invoice'

const statusPresentation: Record<
  DocumentStatus,
  { label: string; icon: string; tone: 'draft' | 'approved' | 'submitted' | 'accepted' | 'rejected' }
> = {
  Draft: { label: 'Robocza', icon: 'D', tone: 'draft' },
  Approved: { label: 'Zatwierdzona', icon: 'A', tone: 'approved' },
  SubmittedToKsef: { label: 'Wysłana do KSeF', icon: 'K', tone: 'submitted' },
  AcceptedByKsef: { label: 'Zaakceptowana przez KSeF', icon: 'OK', tone: 'accepted' },
  RejectedByKsef: { label: 'Odrzucona przez KSeF', icon: '!', tone: 'rejected' },
}

interface DocumentStatusBadgeProps {
  status: DocumentStatus
}

export function DocumentStatusBadge({ status }: DocumentStatusBadgeProps): ReactElement {
  const presentation = statusPresentation[status]

  return (
    <span
      className={`invoice-status-badge invoice-status-badge--${presentation.tone}`}
      role="status"
      aria-label={presentation.label}
    >
      <span className="invoice-status-badge__icon" aria-hidden="true">
        {presentation.icon}
      </span>
      <span>{presentation.label}</span>
    </span>
  )
}
