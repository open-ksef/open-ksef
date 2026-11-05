import type { ReactElement } from 'react'

import type { KsefEnvironment } from '@/api/types'
import { EnvBadge } from './KsefStatusBadge'

interface SellerCardProps {
  nip: string
  name: string | null
  environment: KsefEnvironment
}

export function SellerCard({ nip, name, environment }: SellerCardProps): ReactElement {
  return (
    <section className="inv-seller" aria-label="Sprzedawca" data-testid="seller-card">
      <span className="inv-caption">Sprzedawca</span>
      <b title={name ?? undefined}>{name ?? '—'}</b>
      <span className="mono">NIP {nip}</span> <EnvBadge environment={environment} />
    </section>
  )
}
