import type { ReactElement, ReactNode } from 'react'

import { Icon } from '@/components/Icon'
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

  function field(index: number, name: keyof InvoiceLineFormValue, label: string, place: string, numeric = false) {
    const id = `line-${index}-${name}`
    const error = fieldErrors[`lines[${index}].${name}`]
    return (
      <div className={`inv-line__field inv-line__${place}`}>
        <label htmlFor={id} className="inv-line__label">
          {label} pozycji {index + 1}
        </label>
        <input
          id={id}
          type="text"
          inputMode={numeric ? 'decimal' : undefined}
          aria-invalid={error ? 'true' : 'false'}
          aria-describedby={error ? `${id}-error` : undefined}
          className={numeric ? 'num' : undefined}
          value={lines[index][name]}
          onChange={(event) => update(index, { [name]: event.target.value })}
        />
        {error ? (
          <span className="inv-field-error" id={`${id}-error`}>
            {error}
          </span>
        ) : null}
      </div>
    )
  }

  return (
    <section className="inv-card" aria-labelledby="lines-title">
      <h2 id="lines-title">Pozycje</h2>
      <div className="inv-lines-head" aria-hidden="true">
        <span className="inv-line__name">Nazwa</span>
        <span className="inv-line__unit">J.m.</span>
        <span className="inv-line__qty">Ilość</span>
        <span className="inv-line__price">Cena netto</span>
        <span className="inv-line__rate">Stawka</span>
        <span className="inv-line__gross">Brutto</span>
      </div>
      {lines.map((line, index) => {
        const net = lineNet[index]
        const vat = computeTotals([{ quantity: 1, unitNetPrice: net, vatRate: line.vatRate }]).totalVat
        const rateError = fieldErrors[`lines[${index}].vatRate`]
        return (
          <div className="inv-line" key={index} data-testid={`line-${index}`}>
            <span className="inv-line__no">{index + 1}</span>
            {field(index, 'name', 'Nazwa', 'name')}
            {field(index, 'unit', 'J.m.', 'unit')}
            {field(index, 'quantity', 'Ilość', 'qty', true)}
            {field(index, 'unitNetPrice', 'Cena netto', 'price', true)}
            <div className="inv-line__field inv-line__rate">
              <label htmlFor={`line-${index}-vatRate`} className="inv-line__label">
                Stawka pozycji {index + 1}
              </label>
              <VatRateSelect
                id={`line-${index}-vatRate`}
                label={`Stawka pozycji ${index + 1}`}
                value={line.vatRate}
                describedBy={rateError ? `line-${index}-vatRate-error` : undefined}
                onChange={(vatRate) => update(index, { vatRate })}
              />
              {rateError ? (
                <span className="inv-field-error" id={`line-${index}-vatRate-error`}>
                  {rateError}
                </span>
              ) : null}
            </div>
            <div className="inv-line__gross">
              <span className="visually-hidden">Brutto pozycji {index + 1}: </span>
              {formatMoney(Math.round((net + vat) * 100) / 100)}
            </div>
            <div className="inv-line__del">
              <button type="button" aria-label={`Usuń pozycję ${index + 1}`} disabled={lines.length === 1} onClick={() => onChange(lines.filter((_, i) => i !== index))}>
                <Icon name="trash-2" />
              </button>
            </div>
          </div>
        )
      })}
      <div className="inv-lines-foot">
        <button type="button" className="ui-button ui-button--outline ui-button--sm" onClick={() => onChange([...lines, emptyLine()])}>
          <Icon name="plus" /> Dodaj pozycję
        </button>
        <p className="inv-help">VAT w wierszu jest poglądowy. Podatek liczymy od sumy netto w każdej stawce.</p>
      </div>
      {fieldErrors.lines ? <span className="inv-field-error">{fieldErrors.lines}</span> : null}
      {children}
    </section>
  )
}
