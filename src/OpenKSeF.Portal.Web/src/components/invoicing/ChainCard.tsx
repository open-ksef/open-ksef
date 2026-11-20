import type { ReactElement, ReactNode } from 'react'
import { Link } from 'react-router-dom'

import type { IssuedInvoiceCorrectionItem, OrderChain } from '@/api/types'
import { KIND_LABELS } from '@/invoicing/advance'
import { formatDate, formatMoney, formatSignedMoney } from '@/invoicing/format'
import { KsefStatusBadge } from './KsefStatusBadge'

interface ChainCardProps {
  chain: OrderChain
  currency: string
  currentId: string
  itemHref: (id: string) => string
  children?: ReactNode
}

export function ChainCard({ chain, currency, currentId, itemHref, children }: ChainCardProps): ReactElement {
  const documents: IssuedInvoiceCorrectionItem[] = [...chain.advances, ...(chain.final ? [chain.final] : []), ...chain.corrections]
  return (
    <section className="card" aria-labelledby="chain-title" data-testid="chain">
      <h2 id="chain-title">Zamówienie i zaliczki</h2>
      <dl className="kv">
        <dt>Wartość zamówienia</dt>
        <dd data-testid="chain-order">{formatMoney(chain.orderValue, currency)}</dd>
        <dt>Pozostało po zaliczkach</dt>
        <dd data-testid="chain-remaining">
          <b>{formatMoney(chain.remaining, currency)}</b>
        </dd>
      </dl>
      <ul className="inv-kor-list" aria-label="Dokumenty zamówienia">
        {documents.map((item) => (
          <li key={item.id}>
            <span className="inv-caption">{KIND_LABELS[item.invoiceType]} </span>
            {item.id === currentId ? (
              <b className="mono">{item.number ?? 'Szkic'}</b>
            ) : (
              <Link to={itemHref(item.id)} className="mono">
                {item.number ?? 'Szkic'}
              </Link>
            )}{' '}
            {formatDate(item.issueDate)} · <KsefStatusBadge status={item.status} /> · {formatSignedMoney(item.totalGross, currency)}
          </li>
        ))}
      </ul>
      {children}
    </section>
  )
}
