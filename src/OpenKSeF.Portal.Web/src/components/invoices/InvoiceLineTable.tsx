import type { ReactElement } from 'react'

import { MoneyDisplay } from './MoneyDisplay'
import type { MoneyValue } from './MoneyDisplay'

export interface InvoiceLineReadDto {
  lineNumber: number
  description: string
  quantity: number
  unitOfMeasure: string | null
  pricingMode: 'Net' | 'Gross'
  unitPrice: MoneyValue
  discountPercent: number | null
  vatRate: string
  netAmount: MoneyValue
  vatAmount: MoneyValue
  grossAmount: MoneyValue
  correctionRole: string | null
}

interface InvoiceLineTableProps {
  lines: InvoiceLineReadDto[]
  showCorrectionColumns?: boolean
}

export function InvoiceLineTable({ lines, showCorrectionColumns = false }: InvoiceLineTableProps): ReactElement {
  return (
    <table className="invoice-line-table">
      <caption className="invoice-line-table__caption">Pozycje faktury</caption>
      <thead>
        <tr>
          <th scope="col">Lp.</th>
          <th scope="col">Opis</th>
          <th scope="col">Ilość</th>
          <th scope="col">J.m.</th>
          <th scope="col">Cena jedn.</th>
          <th scope="col">Rabat</th>
          <th scope="col">Stawka VAT</th>
          <th scope="col">Netto</th>
          <th scope="col">VAT</th>
          <th scope="col">Brutto</th>
          {showCorrectionColumns ? <th scope="col">Rola korekty</th> : null}
        </tr>
      </thead>
      <tbody>
        {lines.map((line) => (
          <tr key={line.lineNumber}>
            <td>{line.lineNumber}</td>
            <td>{line.description}</td>
            <td>{line.quantity}</td>
            <td>{line.unitOfMeasure ?? '—'}</td>
            <td>
              <MoneyDisplay value={line.unitPrice} />
            </td>
            <td>{line.discountPercent != null ? `${line.discountPercent}%` : '—'}</td>
            <td>{line.vatRate}</td>
            <td>
              <MoneyDisplay value={line.netAmount} />
            </td>
            <td>
              <MoneyDisplay value={line.vatAmount} />
            </td>
            <td>
              <MoneyDisplay value={line.grossAmount} />
            </td>
            {showCorrectionColumns ? <td>{line.correctionRole ?? '—'}</td> : null}
          </tr>
        ))}
      </tbody>
    </table>
  )
}
