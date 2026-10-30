import type { ReactElement } from 'react'

import type { VatSummaryRow } from '@/api/types'
import { formatMoney } from '@/invoicing/format'
import { vatRateLabel } from '@/invoicing/totals'

interface VatSummaryProps {
  rows: VatSummaryRow[]
  totalNet: number
  totalVat: number
  totalGross: number
  currency?: string
}

export function VatSummary({ rows, totalNet, totalVat, totalGross, currency = 'PLN' }: VatSummaryProps): ReactElement {
  return (
    <section className="inv-card" aria-labelledby="vat-summary-title">
      <h2 id="vat-summary-title">Podsumowanie</h2>
      <div className="inv-table-wrap">
        <table className="inv-table">
          <thead>
            <tr>
              <th scope="col">Stawka</th>
              <th scope="col" className="num">Netto</th>
              <th scope="col" className="num">VAT</th>
              <th scope="col" className="num">Brutto</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.vatRate}>
                <td>{vatRateLabel(row.vatRate)}</td>
                <td className="num">{formatMoney(row.net, currency)}</td>
                <td className="num">{formatMoney(row.vat, currency)}</td>
                <td className="num">{formatMoney(Math.round((row.net + row.vat) * 100) / 100, currency)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr>
              <td>Razem</td>
              <td className="num">{formatMoney(totalNet, currency)}</td>
              <td className="num">{formatMoney(totalVat, currency)}</td>
              <td className="num">{formatMoney(totalGross, currency)}</td>
            </tr>
          </tfoot>
        </table>
      </div>
      <div className="inv-total">
        <span>Do zapłaty</span>
        <output className="inv-total__value" data-testid="total-gross">
          {formatMoney(totalGross, currency)}
        </output>
      </div>
    </section>
  )
}
