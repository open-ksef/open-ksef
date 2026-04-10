import type { ChangeEvent, ReactElement } from 'react'

import { MoneyDisplay, type MoneyValue } from './MoneyDisplay'

export interface AdvanceOption {
  id: string
  documentNumber: string
  grossAmount: MoneyValue
}

interface AdvanceAllocationPickerProps {
  advances: AdvanceOption[]
  selected: string[]
  onChange: (selected: string[]) => void
}

export function AdvanceAllocationPicker({ advances, selected, onChange }: AdvanceAllocationPickerProps): ReactElement {
  const currency = advances[0]?.grossAmount.currency ?? 'PLN'

  const runningTotal = advances
    .filter((a) => selected.includes(a.id))
    .reduce((sum, a) => sum + a.grossAmount.amount, 0)

  function handleToggle(id: string, checked: boolean): void {
    if (checked) {
      onChange([...selected, id])
    } else {
      onChange(selected.filter((s) => s !== id))
    }
  }

  return (
    <fieldset className="advance-allocation-picker">
      <legend className="advance-allocation-picker__legend">Wybierz zaliczki</legend>
      <ul className="advance-allocation-picker__list">
        {advances.map((advance) => (
          <li key={advance.id} className="advance-allocation-picker__item">
            <label className="advance-allocation-picker__label">
              <input
                type="checkbox"
                checked={selected.includes(advance.id)}
                onChange={(e: ChangeEvent<HTMLInputElement>) => handleToggle(advance.id, e.target.checked)}
              />
              <span className="advance-allocation-picker__number">{advance.documentNumber}</span>
              <span className="advance-allocation-picker__amount">
                <MoneyDisplay value={advance.grossAmount} />
              </span>
            </label>
          </li>
        ))}
      </ul>
      <p className="advance-allocation-picker__total" aria-live="polite">
        Łącznie:{' '}
        <MoneyDisplay value={{ amount: runningTotal, currency }} />
      </p>
    </fieldset>
  )
}
