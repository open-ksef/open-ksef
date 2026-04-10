// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import { VatSummaryTable, type VatBreakdownRow } from './VatSummaryTable'

const breakdown: VatBreakdownRow[] = [
  {
    vatRate: '23%',
    net: { amount: 1000, currency: 'PLN' },
    vat: { amount: 230, currency: 'PLN' },
    gross: { amount: 1230, currency: 'PLN' },
  },
  {
    vatRate: '8%',
    net: { amount: 500, currency: 'PLN' },
    vat: { amount: 40, currency: 'PLN' },
    gross: { amount: 540, currency: 'PLN' },
  },
]

describe('VatSummaryTable', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders a row for each VAT rate', async () => {
    await act(async () => {
      root.render(<VatSummaryTable breakdown={breakdown} />)
    })

    const rows = container.querySelectorAll('tbody tr')
    expect(rows.length).toBe(2)
    expect(rows[0]?.textContent).toContain('23%')
    expect(rows[1]?.textContent).toContain('8%')
  })

  it('renders column headers for net, vat, gross', async () => {
    await act(async () => {
      root.render(<VatSummaryTable breakdown={breakdown} />)
    })

    const headers = container.querySelectorAll('th')
    const headerTexts = [...headers].map((h) => h.textContent)
    expect(headerTexts.some((t) => t?.includes('Netto'))).toBe(true)
    expect(headerTexts.some((t) => t?.includes('VAT'))).toBe(true)
    expect(headerTexts.some((t) => t?.includes('Brutto'))).toBe(true)
  })

  it('renders nothing when breakdown is empty', async () => {
    await act(async () => {
      root.render(<VatSummaryTable breakdown={[]} />)
    })

    const table = container.querySelector('table')
    expect(table).toBeNull()
  })

  it('uses a semantic table with caption', async () => {
    await act(async () => {
      root.render(<VatSummaryTable breakdown={breakdown} />)
    })

    expect(container.querySelector('table')).not.toBeNull()
    expect(container.querySelector('caption')).not.toBeNull()
  })
})
