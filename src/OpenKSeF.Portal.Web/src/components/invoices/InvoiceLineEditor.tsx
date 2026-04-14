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
      <div className="invoice-line-editor__header" aria-hidden="true">
        <span className="invoice-line-editor__header-cell">Opis</span>
        <span className="invoice-line-editor__header-cell">Ilość</span>
        <span className="invoice-line-editor__header-cell">J.m.</span>
        <span className="invoice-line-editor__header-cell">Cena jedn.</span>
        <span className="invoice-line-editor__header-cell">Stawka VAT</span>
        <span />
      </div>
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
          <div className={mode === 'correction' ? 'invoice-line-editor__correction-after' : 'invoice-line-editor__fields'}>
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
          <div className="invoice-line-editor__actions">
            <button
              type="button"
              className="invoice-line-editor__remove"
              onClick={() => handleRemove(index)}
              aria-label={`Usuń pozycję ${index + 1}`}
            >
              ✕
            </button>
            {allowReorder ? (
              <div className="invoice-line-editor__reorder">
                <button
                  type="button"
                  data-testid={`line-move-up-${index}`}
                  onClick={() => handleMove(index, -1)}
                  disabled={index === 0}
                  aria-label="Przesuń wyżej"
                >
                  ↑
                </button>
                <button
                  type="button"
                  data-testid={`line-move-down-${index}`}
                  onClick={() => handleMove(index, 1)}
                  disabled={index === value.length - 1}
                  aria-label="Przesuń niżej"
                >
                  ↓
                </button>
              </div>
            ) : null}
          </div>
        </div>
      ))}
      <button type="button" className="invoice-line-editor__add" onClick={handleAdd}>
        + Dodaj pozycję
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
    <>
      <input
        id={id('description')}
        aria-label="Opis"
        type="text"
        placeholder="Opis towaru lub usługi"
        value={line.description}
        disabled={disabled}
        onChange={(event: ChangeEvent<HTMLInputElement>) => update({ description: event.target.value })}
      />
      <input
        id={id('quantity')}
        aria-label="Ilość"
        type="text"
        inputMode="decimal"
        value={line.quantity}
        disabled={disabled}
        onChange={(event: ChangeEvent<HTMLInputElement>) => {
          const parsed = Number(event.target.value)
          if (!Number.isNaN(parsed)) update({ quantity: parsed })
        }}
      />
      <input
        id={id('unitOfMeasure')}
        aria-label="Jednostka miary"
        type="text"
        placeholder="szt."
        value={line.unitOfMeasure}
        disabled={disabled}
        onChange={(event: ChangeEvent<HTMLInputElement>) => update({ unitOfMeasure: event.target.value })}
      />
      <input
        id={id('unitPrice')}
        aria-label="Cena jednostkowa"
        type="text"
        inputMode="decimal"
        value={line.unitPrice}
        disabled={disabled}
        onChange={(event: ChangeEvent<HTMLInputElement>) => {
          const parsed = Number(event.target.value.replace(',', '.'))
          if (!Number.isNaN(parsed)) update({ unitPrice: parsed })
        }}
      />
      <select
        id={id('vatRate')}
        aria-label="Stawka VAT"
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
    </>
  )
}
