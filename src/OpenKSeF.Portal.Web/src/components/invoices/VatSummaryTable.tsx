import type { ReactElement } from 'react'

import { MoneyDisplay, type MoneyValue } from './MoneyDisplay'

export interface VatBreakdownRow {
  vatRate: string
  net: MoneyValue
  vat: MoneyValue
  gross: MoneyValue
}

interface VatSummaryTableProps {
  breakdown: VatBreakdownRow[]
}

export function VatSummaryTable({ breakdown }: VatSummaryTableProps): ReactElement | null {
  if (breakdown.length === 0) {
    return null
  }

  return (
    <table className="vat-summary-table">
      <caption className="vat-summary-table__caption">Zestawienie VAT</caption>
      <thead>
        <tr>
          <th scope="col">Stawka VAT</th>
          <th scope="col">Netto</th>
          <th scope="col">VAT</th>
          <th scope="col">Brutto</th>
        </tr>
      </thead>
      <tbody>
        {breakdown.map((row) => (
          <tr key={row.vatRate}>
            <td>{row.vatRate}</td>
            <td>
              <MoneyDisplay value={row.net} />
            </td>
            <td>
              <MoneyDisplay value={row.vat} />
            </td>
            <td>
              <MoneyDisplay value={row.gross} />
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
