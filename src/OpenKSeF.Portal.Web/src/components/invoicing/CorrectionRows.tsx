import type { ReactElement } from 'react'

import { formatDecimal, formatMoney } from '@/invoicing/format'
import { vatRateLabel } from '@/invoicing/totals'

interface CorrectionRowsProps {
  rows: readonly {
    rowNo: number
    isBefore: boolean
    name: string
    unit: string
    quantity: number
    unitNetPrice: number
    vatRate: string
    netAmount: number
  }[]
  currency: string
}

export function CorrectionRows({ rows, currency }: CorrectionRowsProps): ReactElement {
  return rows.length === 0 ? (
    <p className="inv-help">Bez zmian w pozycjach.</p>
  ) : (
    <div className="inv-kor-rows">
      <table className="inv-sum" data-testid="correction-rows">
        <caption className="visually-hidden">Pozycje przed i po korekcie, kwoty w {currency}</caption>
        <thead>
          <tr>
            <th scope="col">Lp.</th>
            <th scope="col">Pozycja</th>
            <th scope="col">Stan</th>
            <th scope="col">Ilość</th>
            <th scope="col">Cena netto</th>
            <th scope="col">Stawka</th>
            <th scope="col">Netto</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.rowNo} className={row.isBefore ? 'inv-kor-before' : undefined}>
              <td>{row.rowNo}</td>
              <td className="inv-kor-name">{row.name}</td>
              <td>{row.isBefore ? 'przed' : 'po'}</td>
              <td>
                {formatDecimal(row.quantity, 0)} {row.unit}
              </td>
              <td>{formatDecimal(row.unitNetPrice, 2)}</td>
              <td>{vatRateLabel(row.vatRate)}</td>
              <td>{formatMoney(row.netAmount, currency)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
