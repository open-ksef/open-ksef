import type { ReactElement } from 'react'

export interface PartyValue {
  name: string
  nip: string | null
}

interface PartyCardProps {
  party: PartyValue
  title?: string
}

export function PartyCard({ party, title = 'Strona dokumentu' }: PartyCardProps): ReactElement {
  return (
    <section className="party-card" aria-label={title}>
      <h2 className="party-card__title">{title}</h2>
      <dl className="party-card__details">
        <dt>Nazwa</dt>
        <dd>{party.name}</dd>
        <dt>NIP</dt>
        <dd>{party.nip ?? '—'}</dd>
      </dl>
    </section>
  )
}
