import type { ReactElement } from 'react'

import type { KsefSubmissionState } from '@/api/schemas/invoice'

const statePresentation: Record<
  KsefSubmissionState,
  {
    label: string
    icon: string
    tone: 'neutral' | 'ready' | 'submitted' | 'accepted' | 'rejected'
  }
> = {
  NotPlanned: { label: 'Brak wysyłki do KSeF', icon: 'NP', tone: 'neutral' },
  Ready: { label: 'Gotowa do wysyłki', icon: 'GO', tone: 'ready' },
  Submitted: { label: 'Wysłana do KSeF', icon: 'K', tone: 'submitted' },
  Accepted: { label: 'Przyjęta przez KSeF', icon: 'OK', tone: 'accepted' },
  Rejected: { label: 'Odrzucona przez KSeF', icon: '!', tone: 'rejected' },
}

interface KsefSubmissionStatusProps {
  state: KsefSubmissionState
  identifiers?: {
    ksefDocumentNumber?: string | null
    ksefReferenceNumber?: string | null
  }
  rejectionReason?: string | null
}

export function KsefSubmissionStatus({
  state,
  identifiers,
  rejectionReason,
}: KsefSubmissionStatusProps): ReactElement {
  const presentation = statePresentation[state]
  const hasIdentifiers = Boolean(identifiers?.ksefDocumentNumber || identifiers?.ksefReferenceNumber)

  return (
    <section
      className={`ksef-submission-status ksef-submission-status--${presentation.tone}`}
      role="status"
      aria-live={state === 'Submitted' ? 'polite' : undefined}
      aria-label={presentation.label}
    >
      <div className="ksef-submission-status__header">
        <span className="ksef-submission-status__icon" aria-hidden="true">
          {presentation.icon}
        </span>
        <span className="ksef-submission-status__label">{presentation.label}</span>
      </div>
      {hasIdentifiers ? (
        <dl className="ksef-submission-status__details">
          {identifiers?.ksefDocumentNumber ? (
            <>
              <dt>Numer KSeF</dt>
              <dd>{identifiers.ksefDocumentNumber}</dd>
            </>
          ) : null}
          {identifiers?.ksefReferenceNumber ? (
            <>
              <dt>Referencja</dt>
              <dd>{identifiers.ksefReferenceNumber}</dd>
            </>
          ) : null}
        </dl>
      ) : null}
      {rejectionReason ? <p className="ksef-submission-status__reason">{rejectionReason}</p> : null}
    </section>
  )
}
