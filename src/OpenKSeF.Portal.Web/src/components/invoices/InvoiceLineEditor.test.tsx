// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { InvoiceLineFormValue } from './InvoiceLineEditor'
import { InvoiceLineEditor } from './InvoiceLineEditor'

const emptyLine: InvoiceLineFormValue = {
  description: '',
  quantity: 1,
  unitOfMeasure: '',
  pricingMode: 'Net',
  unitPrice: 0,
  discountPercent: null,
  vatRate: '23%',
}

describe('InvoiceLineEditor', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders one row per line', async () => {
    const lines = [emptyLine, emptyLine]
    await act(async () => {
      root.render(<InvoiceLineEditor value={lines} onChange={() => {}} mode="create" pricingMode="Net" />)
    })

    const rows = container.querySelectorAll('.invoice-line-editor__row')
    expect(rows.length).toBe(2)
  })

  it('shows an "Dodaj pozycję" button', async () => {
    await act(async () => {
      root.render(<InvoiceLineEditor value={[emptyLine]} onChange={() => {}} mode="create" pricingMode="Net" />)
    })

    const addButton = [...container.querySelectorAll('button')].find((b) => b.textContent?.includes('Dodaj'))
    expect(addButton).toBeTruthy()
  })

  it('calls onChange with a new empty line when add button is clicked', async () => {
    const onChange = vi.fn()
    await act(async () => {
      root.render(<InvoiceLineEditor value={[emptyLine]} onChange={onChange} mode="create" pricingMode="Net" />)
    })

    const addButton = [...container.querySelectorAll('button')].find((b) => b.textContent?.includes('Dodaj'))
    await act(async () => {
      addButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    })

    expect(onChange).toHaveBeenCalled()
    const nextLines: InvoiceLineFormValue[] = onChange.mock.calls[0][0]
    expect(nextLines.length).toBe(2)
  })

  it('calls onChange with line removed when delete button is clicked', async () => {
    const onChange = vi.fn()
    const twoLines = [
      { ...emptyLine, description: 'Linia 1' },
      { ...emptyLine, description: 'Linia 2' },
    ]
    await act(async () => {
      root.render(<InvoiceLineEditor value={twoLines} onChange={onChange} mode="create" pricingMode="Net" />)
    })

    const deleteButtons = [...container.querySelectorAll('button')].filter((b) => b.textContent?.includes('Usuń'))
    await act(async () => {
      deleteButtons[0]?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    })

    expect(onChange).toHaveBeenCalled()
    const nextLines: InvoiceLineFormValue[] = onChange.mock.calls[0][0]
    expect(nextLines.length).toBe(1)
    expect(nextLines[0]?.description).toBe('Linia 2')
  })

  it('shows before/after labels in correction mode', async () => {
    const correctionLine: InvoiceLineFormValue = {
      ...emptyLine,
      correctionBefore: { ...emptyLine, description: 'Przed' },
    }
    await act(async () => {
      root.render(
        <InvoiceLineEditor value={[correctionLine]} onChange={() => {}} mode="correction" pricingMode="Net" />,
      )
    })

    expect(container.textContent).toMatch(/przed|korekty/i)
  })
})
