import type { ReactElement } from 'react'

import type { IssuedInvoiceStatus, KsefEnvironment } from '@/api/types'

const statusPresentation: Record<IssuedInvoiceStatus, { label: string; tone: string; icon: string }> = {
  Draft: { label: 'Szkic', tone: 'neutral', icon: '✎' },
  Sending: { label: 'Wysyłanie', tone: 'info', icon: '' },
  Accepted: { label: 'Przyjęta w KSeF', tone: 'success', icon: '✓' },
  Rejected: { label: 'Odrzucona', tone: 'error', icon: '✕' },
}

export function KsefStatusBadge({ status }: { status: IssuedInvoiceStatus }): ReactElement {
  const presentation = statusPresentation[status]
  return (
    <span className={`inv-badge inv-badge--${presentation.tone}`}>
      {status === 'Sending' ? (
        <span className="inv-spinner" aria-hidden="true" />
      ) : (
        <span aria-hidden="true">{presentation.icon}</span>
      )}
      {presentation.label}
    </span>
  )
}

const envPresentation: Record<KsefEnvironment, { label: string; tone: string }> = {
  test: { label: 'TEST', tone: 'warning' },
  demo: { label: 'DEMO', tone: 'neutral' },
  production: { label: 'PRODUKCJA', tone: 'error-filled' },
}

export function EnvBadge({ environment }: { environment: KsefEnvironment }): ReactElement {
  const presentation = envPresentation[environment]
  return (
    <span className={`inv-badge inv-badge--${presentation.tone}`} title="Środowisko KSeF">
      <span className="visually-hidden">Środowisko KSeF: </span>
      {presentation.label}
    </span>
  )
}
