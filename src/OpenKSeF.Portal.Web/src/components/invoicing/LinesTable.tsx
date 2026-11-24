import { useQuery } from '@tanstack/react-query'
import { useEffect, useState, type ReactElement, type ReactNode } from 'react'

import { listLineSuggestions } from '@/api/endpoints/issuedInvoices'
import type { BuyerKind, VatRateCode } from '@/api/types'
import { Icon } from '@/components/Icon'
import { emptyLine, parseDecimal, type InvoiceLineFormValue } from '@/invoicing/form'
import { formatMoney } from '@/invoicing/format'
import { computeTotals, VAT_RATES, vatRateLabel } from '@/invoicing/totals'
import { VatRateSelect } from './VatRateSelect'

interface LinesTableProps {
  lines: InvoiceLineFormValue[]
  currency: string
  rates?: typeof VAT_RATES
  newLineRate?: VatRateCode
  onChange: (lines: InvoiceLineFormValue[]) => void
  fieldErrors?: Record<string, string>
  children?: ReactNode
  title?: string
  suggest?: { tenantId: string; buyerKind: BuyerKind }
}

const SUGGEST_DELAY_MS = 300

export function LinesTable({ lines, currency, rates, newLineRate, onChange, fieldErrors = {}, children, title = 'Pozycje', suggest }: LinesTableProps): ReactElement {
  const [active, setActive] = useState<number | null>(null)
  const activeName = active === null ? '' : (lines[active]?.name.trim() ?? '')
  const [term, setTerm] = useState('')

  useEffect(() => {
    const timer = setTimeout(() => setTerm(activeName), SUGGEST_DELAY_MS)
    return () => clearTimeout(timer)
  }, [activeName])

  const suggestionsQuery = useQuery({
    queryKey: ['line-suggestions', suggest?.tenantId, suggest?.buyerKind, term],
    queryFn: () => listLineSuggestions(suggest!.tenantId, term, suggest!.buyerKind),
    enabled: Boolean(suggest) && term.length >= 2,
  })
  const allowedRates = rates ?? VAT_RATES
  const suggestions = term.length >= 2 ? (suggestionsQuery.data ?? []).filter((item) => allowedRates.some((rate) => rate.code === item.vatRate)) : []

  const { lineNet } = computeTotals(
    lines.map((line) => ({ quantity: parseDecimal(line.quantity), unitNetPrice: parseDecimal(line.unitNetPrice), vatRate: line.vatRate })),
  )

  function update(index: number, patch: Partial<InvoiceLineFormValue>): void {
    onChange(lines.map((line, i) => (i === index ? { ...line, ...patch } : line)))
  }

  function updateName(index: number, value: string): void {
    const picked = suggestions.find((item) => item.name === value)
    update(index, picked ? { name: picked.name, unit: picked.unit, unitNetPrice: String(picked.unitNetPrice).replace('.', ','), vatRate: picked.vatRate } : { name: value })
  }

  function field(index: number, name: keyof InvoiceLineFormValue, label: string, place: string, numeric = false) {
    const id = `line-${index}-${name}`
    const error = fieldErrors[`lines[${index}].${name}`]
    const suggestName = name === 'name' && suggest !== undefined
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
          list={suggestName ? 'line-suggestions' : undefined}
          autoComplete={suggestName ? 'off' : undefined}
          value={lines[index][name]}
          onFocus={suggestName ? () => setActive(index) : undefined}
          onChange={(event) => (suggestName ? updateName(index, event.target.value) : update(index, { [name]: event.target.value }))}
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
      <h2 id="lines-title">{title}</h2>
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
                rates={rates}
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
              {formatMoney(Math.round((net + vat) * 100) / 100, currency)}
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
        <button type="button" className="ui-button ui-button--outline ui-button--sm" onClick={() => onChange([...lines, emptyLine(newLineRate)])}>
          <Icon name="plus" /> Dodaj pozycję
        </button>
        <p className="inv-help">VAT w wierszu jest poglądowy. Podatek liczymy od sumy netto w każdej stawce.</p>
      </div>
      {suggest ? (
        <datalist id="line-suggestions">
          {suggestions.map((item) => (
            <option key={item.name} value={item.name} label={`${item.unit} · ${formatMoney(item.unitNetPrice, currency)} · ${vatRateLabel(item.vatRate)}`} />
          ))}
        </datalist>
      ) : null}
      {fieldErrors.lines ? <span className="inv-field-error">{fieldErrors.lines}</span> : null}
      {children}
    </section>
  )
}
