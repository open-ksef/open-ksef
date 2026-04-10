import type { ReactElement } from 'react'

import { MoneyDisplay, type MoneyValue } from './MoneyDisplay'

interface TotalsSummaryCardProps {
  net: MoneyValue
  vat: MoneyValue
  gross: MoneyValue
  currency: string
}

export function TotalsSummaryCard({ net, vat, gross }: TotalsSummaryCardProps): ReactElement {
  return (
    <section className="totals-summary-card" aria-label="Podsumowanie kwot">
      <dl className="totals-summary-card__rows">
        <div className="totals-summary-card__row">
          <dt className="totals-summary-card__label">Netto</dt>
          <dd className="totals-summary-card__value">
            <MoneyDisplay value={net} />
          </dd>
        </div>
        <div className="totals-summary-card__row">
          <dt className="totals-summary-card__label">VAT</dt>
          <dd className="totals-summary-card__value">
            <MoneyDisplay value={vat} />
          </dd>
        </div>
        <div className="totals-summary-card__row totals-summary-card__row--gross">
          <dt className="totals-summary-card__label">Brutto</dt>
          <dd className="totals-summary-card__value">
            <MoneyDisplay value={gross} />
          </dd>
        </div>
      </dl>
    </section>
  )
}
