import type { ReactElement } from 'react'

import type { VatSummaryRow } from '@/api/types'
import { formatAmount, formatMoney } from '@/invoicing/format'
import { vatRateLabel } from '@/invoicing/totals'

interface VatSummaryProps {
  rows: VatSummaryRow[]
  totalNet: number
  totalVat: number
  totalGross: number
  currency: string
  sticky?: boolean
}

export function VatSummary({ rows, totalNet, totalVat, totalGross, currency, sticky = false }: VatSummaryProps): ReactElement {
  const table = (
    <table className="inv-sum">
      <caption className="visually-hidden">Kwoty w {currency}</caption>
      <thead>
        <tr>
          <th scope="col">Stawka</th>
          <th scope="col">Netto</th>
          <th scope="col">VAT</th>
          <th scope="col">Brutto</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr key={row.vatRate}>
            <td>{vatRateLabel(row.vatRate)}</td>
            <td>{formatAmount(row.net)}</td>
            <td>{formatAmount(row.vat)}</td>
            <td>{formatAmount(Math.round((row.net + row.vat) * 100) / 100)}</td>
          </tr>
        ))}
      </tbody>
      <tfoot>
        <tr>
          <td>Razem</td>
          <td>{formatAmount(totalNet)}</td>
          <td>{formatAmount(totalVat)}</td>
          <td>{formatAmount(totalGross)}</td>
        </tr>
      </tfoot>
    </table>
  )

  if (sticky) {
    return (
      <div className="inv-card inv-vat" tabIndex={0} role="region" aria-label={`Podsumowanie VAT w ${currency}`}>
        {table}
        <p className="inv-help">Kwoty w {currency}.</p>
      </div>
    )
  }

  return (
    <section className="inv-card" aria-labelledby="vat-summary-title">
      <h2 id="vat-summary-title">Podsumowanie</h2>
      {table}
      <p className="inv-help">Kwoty w {currency}.</p>
      <div>
        <span className="inv-caption">Do zapłaty</span>
        <output className="inv-amount" data-testid="total-gross">
          {formatMoney(totalGross, currency)}
        </output>
      </div>
    </section>
  )
}
