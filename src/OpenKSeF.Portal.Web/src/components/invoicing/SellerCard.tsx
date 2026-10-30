import type { ReactElement } from 'react'
import { Link } from 'react-router-dom'

import { formatNrb } from '@/invoicing/format'

interface SellerCardProps {
  nip: string
  name: string | null
  addressLine1: string | null
  addressLine2: string | null
  bankAccount: string | null
  editHref?: string
}

export function SellerCard({ nip, name, addressLine1, addressLine2, bankAccount, editHref }: SellerCardProps): ReactElement {
  return (
    <section className="inv-card" aria-labelledby="seller-card-title">
      <h2 id="seller-card-title">Sprzedawca</h2>
      <dl className="inv-readonly">
        <dt>Nazwa</dt>
        <dd>{name ?? '—'}</dd>
        <dt>NIP</dt>
        <dd className="mono">{nip}</dd>
        <dt>Adres</dt>
        <dd>
          {addressLine1 ?? '—'}
          {addressLine2 ? <br /> : null}
          {addressLine2}
        </dd>
        <dt>Rachunek</dt>
        <dd className="mono">{formatNrb(bankAccount)}</dd>
      </dl>
      {editHref ? <Link to={editHref}>Zmień w Dane do faktur</Link> : null}
    </section>
  )
}
