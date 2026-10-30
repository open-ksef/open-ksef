import type { ReactElement, ReactNode } from 'react'

import { emptyLine, parseDecimal, type InvoiceLineFormValue } from '@/invoicing/form'
import { formatMoney } from '@/invoicing/format'
import { computeTotals } from '@/invoicing/totals'
import { VatRateSelect } from './VatRateSelect'

interface LinesTableProps {
  lines: InvoiceLineFormValue[]
  onChange: (lines: InvoiceLineFormValue[]) => void
  fieldErrors?: Record<string, string>
  children?: ReactNode
}

export function LinesTable({ lines, onChange, fieldErrors = {}, children }: LinesTableProps): ReactElement {
  const { lineNet } = computeTotals(
    lines.map((line) => ({ quantity: parseDecimal(line.quantity), unitNetPrice: parseDecimal(line.unitNetPrice), vatRate: line.vatRate })),
  )

  function update(index: number, patch: Partial<InvoiceLineFormValue>): void {
    onChange(lines.map((line, i) => (i === index ? { ...line, ...patch } : line)))
  }

  function field(index: number, name: keyof InvoiceLineFormValue, label: string, extra?: { inputMode?: 'decimal' }) {
    const id = `line-${index}-${name}`
    const error = fieldErrors[`lines[${index}].${name}`]
    return (
      <>
        <input
          id={id}
          type="text"
          inputMode={extra?.inputMode}
          aria-label={`${label}, pozycja ${index + 1}`}
          aria-invalid={error ? 'true' : 'false'}
          aria-describedby={error ? `${id}-error` : undefined}
          className={extra?.inputMode ? 'num' : undefined}
          value={lines[index][name]}
          onChange={(event) => update(index, { [name]: event.target.value })}
        />
        {error ? (
          <span className="inv-field-error" id={`${id}-error`}>
            {error}
          </span>
        ) : null}
      </>
    )
  }

  return (
    <section className="inv-card" aria-labelledby="lines-title">
      <h2 id="lines-title">Pozycje</h2>
      <div className="inv-table-wrap">
        <table className="inv-table">
          <thead>
            <tr>
              <th scope="col">Lp.</th>
              <th scope="col">Nazwa</th>
              <th scope="col">J.m.</th>
              <th scope="col" className="num">Ilość</th>
              <th scope="col" className="num">Cena netto</th>
              <th scope="col">VAT</th>
              <th scope="col" className="num">Netto</th>
              <th scope="col" className="num">Kwota VAT</th>
              <th scope="col" className="num">Brutto</th>
              <th scope="col">
                <span className="visually-hidden">Akcje</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {lines.map((line, index) => {
              const net = lineNet[index]
              const vat = computeTotals([{ quantity: 1, unitNetPrice: net, vatRate: line.vatRate }]).totalVat
              const rateError = fieldErrors[`lines[${index}].vatRate`]
              return (
                <tr key={index} data-testid={`line-${index}`}>
                  <td>{index + 1}</td>
                  <td className="inv-col-name">{field(index, 'name', 'Nazwa')}</td>
                  <td>{field(index, 'unit', 'Jednostka miary')}</td>
                  <td>{field(index, 'quantity', 'Ilość', { inputMode: 'decimal' })}</td>
                  <td>{field(index, 'unitNetPrice', 'Cena netto', { inputMode: 'decimal' })}</td>
                  <td>
                    <VatRateSelect
                      id={`line-${index}-vatRate`}
                      label={`Stawka VAT, pozycja ${index + 1}`}
                      value={line.vatRate}
                      describedBy={rateError ? `line-${index}-vatRate-error` : undefined}
                      onChange={(vatRate) => update(index, { vatRate })}
                    />
                    {rateError ? (
                      <span className="inv-field-error" id={`line-${index}-vatRate-error`}>
                        {rateError}
                      </span>
                    ) : null}
                  </td>
                  <td className="num">{formatMoney(net)}</td>
                  <td className="num muted">{formatMoney(vat)}</td>
                  <td className="num">{formatMoney(Math.round((net + vat) * 100) / 100)}</td>
                  <td>
                    <button
                      type="button"
                      aria-label={`Usuń pozycję ${index + 1}`}
                      disabled={lines.length === 1}
                      onClick={() => onChange(lines.filter((_, i) => i !== index))}
                    >
                      ✕
                    </button>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
      <div>
        <button type="button" className="ui-button ui-button--outline ui-button--sm" onClick={() => onChange([...lines, emptyLine()])}>
          + Dodaj pozycję
        </button>
      </div>
      {fieldErrors.lines ? <span className="inv-field-error">{fieldErrors.lines}</span> : null}
      <p className="inv-help">VAT w wierszu jest poglądowy. Podatek liczymy od sumy netto w każdej stawce.</p>
      {children}
    </section>
  )
}
