import type { ChangeEvent, KeyboardEvent, ReactElement } from 'react'

export interface InvoiceLineFormValue {
  description: string
  quantity: number
  unitOfMeasure: string
  pricingMode: 'Net' | 'Gross'
  unitPrice: number
  discountPercent: number | null
  vatRate: string
  correctionBefore?: InvoiceLineFormValue
}

interface InvoiceLineEditorProps {
  value: InvoiceLineFormValue[]
  onChange: (lines: InvoiceLineFormValue[]) => void
  mode: 'create' | 'correction'
  pricingMode: 'Net' | 'Gross'
  allowReorder?: boolean
}

const newEmptyLine = (pricingMode: 'Net' | 'Gross'): InvoiceLineFormValue => ({
  description: '',
  quantity: 1,
  unitOfMeasure: '',
  pricingMode,
  unitPrice: 0,
  discountPercent: null,
  vatRate: '23%',
})

export function InvoiceLineEditor({
  value,
  onChange,
  mode,
  pricingMode,
  allowReorder = false,
}: InvoiceLineEditorProps): ReactElement {
  function handleAdd(): void {
    onChange([...value, newEmptyLine(pricingMode)])
  }

  function handleRemove(index: number): void {
    onChange(value.filter((_, i) => i !== index))
  }

  function handleChange(index: number, updated: InvoiceLineFormValue): void {
    onChange(value.map((line, i) => (i === index ? updated : line)))
  }

  function handleMove(index: number, direction: -1 | 1): void {
    const nextIndex = index + direction
    if (nextIndex < 0 || nextIndex >= value.length) {
      return
    }

    const nextLines = [...value]
    const [moved] = nextLines.splice(index, 1)
    nextLines.splice(nextIndex, 0, moved)
    onChange(nextLines)
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
    if (event.key === 'Enter' && event.target instanceof HTMLElement && event.target.tagName !== 'BUTTON') {
      event.preventDefault()
      handleAdd()
    }
  }

  return (
    <div className="invoice-line-editor" role="group" aria-label="Pozycje faktury" onKeyDown={handleKeyDown}>
      {value.map((line, index) => (
        <div className="invoice-line-editor__row" key={index}>
          {mode === 'correction' && line.correctionBefore ? (
            <div className="invoice-line-editor__correction-before">
              <span className="invoice-line-editor__correction-label">Przed korekta</span>
              <LineFields
                line={line.correctionBefore}
                index={index}
                prefix="before"
                disabled
                onChange={() => {}}
              />
            </div>
          ) : null}
          <div className={mode === 'correction' ? 'invoice-line-editor__correction-after' : undefined}>
            {mode === 'correction' ? (
              <span className="invoice-line-editor__correction-label">Po korekcie</span>
            ) : null}
            <LineFields
              line={line}
              index={index}
              prefix="after"
              disabled={false}
              onChange={(updated) => handleChange(index, updated)}
            />
          </div>
          <button
            type="button"
            className="invoice-line-editor__remove"
            onClick={() => handleRemove(index)}
            aria-label={`Usun pozycje ${index + 1}`}
          >
            Usun
          </button>
          {allowReorder ? (
            <div className="invoice-line-editor__reorder">
              <button
                type="button"
                data-testid={`line-move-up-${index}`}
                onClick={() => handleMove(index, -1)}
                disabled={index === 0}
              >
                W gore
              </button>
              <button
                type="button"
                data-testid={`line-move-down-${index}`}
                onClick={() => handleMove(index, 1)}
                disabled={index === value.length - 1}
              >
                W dol
              </button>
            </div>
          ) : null}
        </div>
      ))}
      <button type="button" className="invoice-line-editor__add" onClick={handleAdd}>
        Dodaj pozycje
      </button>
    </div>
  )
}

interface LineFieldsProps {
  line: InvoiceLineFormValue
  index: number
  prefix: string
  disabled: boolean
  onChange: (line: InvoiceLineFormValue) => void
}

function LineFields({ line, index, prefix, disabled, onChange }: LineFieldsProps): ReactElement {
  const id = (field: string): string => `line-${prefix}-${index}-${field}`

  function update(patch: Partial<InvoiceLineFormValue>): void {
    onChange({ ...line, ...patch })
  }

  return (
    <div className="invoice-line-editor__fields">
      <label htmlFor={id('description')}>
        Opis
        <input
          id={id('description')}
          type="text"
          value={line.description}
          disabled={disabled}
          onChange={(event: ChangeEvent<HTMLInputElement>) => update({ description: event.target.value })}
        />
      </label>
      <label htmlFor={id('quantity')}>
        Ilosc
        <input
          id={id('quantity')}
          type="text"
          inputMode="decimal"
          value={line.quantity}
          disabled={disabled}
          onChange={(event: ChangeEvent<HTMLInputElement>) => {
            const parsed = Number(event.target.value)
            if (!Number.isNaN(parsed)) update({ quantity: parsed })
          }}
        />
      </label>
      <label htmlFor={id('unitOfMeasure')}>
        J.m.
        <input
          id={id('unitOfMeasure')}
          type="text"
          value={line.unitOfMeasure}
          disabled={disabled}
          onChange={(event: ChangeEvent<HTMLInputElement>) => update({ unitOfMeasure: event.target.value })}
        />
      </label>
      <label htmlFor={id('unitPrice')}>
        Cena jedn.
        <input
          id={id('unitPrice')}
          type="text"
          inputMode="decimal"
          value={line.unitPrice}
          disabled={disabled}
          onChange={(event: ChangeEvent<HTMLInputElement>) => {
            const parsed = Number(event.target.value.replace(',', '.'))
            if (!Number.isNaN(parsed)) update({ unitPrice: parsed })
          }}
        />
      </label>
      <label htmlFor={id('vatRate')}>
        Stawka VAT
        <select
          id={id('vatRate')}
          value={line.vatRate}
          disabled={disabled}
          onChange={(event: ChangeEvent<HTMLSelectElement>) => update({ vatRate: event.target.value })}
        >
          {['23%', '8%', '5%', '0%', 'zw', 'np'].map((rate) => (
            <option key={rate} value={rate}>
              {rate}
            </option>
          ))}
        </select>
      </label>
    </div>
  )
}
