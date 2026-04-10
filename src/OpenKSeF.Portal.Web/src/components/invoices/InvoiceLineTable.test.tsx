// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import type { InvoiceLineReadDto } from './InvoiceLineTable'
import { InvoiceLineTable } from './InvoiceLineTable'

const sampleLines: InvoiceLineReadDto[] = [
  {
    lineNumber: 1,
    description: 'Usługa doradcza',
    quantity: 2,
    unitOfMeasure: 'godz.',
    pricingMode: 'Net',
    unitPrice: { amount: 500, currency: 'PLN' },
    discountPercent: null,
    vatRate: '23%',
    netAmount: { amount: 1000, currency: 'PLN' },
    vatAmount: { amount: 230, currency: 'PLN' },
    grossAmount: { amount: 1230, currency: 'PLN' },
    correctionRole: null,
  },
  {
    lineNumber: 2,
    description: 'Licencja oprogramowania',
    quantity: 1,
    unitOfMeasure: 'szt.',
    pricingMode: 'Net',
    unitPrice: { amount: 2000, currency: 'PLN' },
    discountPercent: 10,
    vatRate: '8%',
    netAmount: { amount: 1800, currency: 'PLN' },
    vatAmount: { amount: 144, currency: 'PLN' },
    grossAmount: { amount: 1944, currency: 'PLN' },
    correctionRole: null,
  },
]

describe('InvoiceLineTable', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders one row per line', async () => {
    await act(async () => {
      root.render(<InvoiceLineTable lines={sampleLines} />)
    })

    const rows = container.querySelectorAll('tbody tr')
    expect(rows.length).toBe(2)
  })

  it('shows description and amounts', async () => {
    await act(async () => {
      root.render(<InvoiceLineTable lines={sampleLines} />)
    })

    expect(container.textContent).toContain('Usługa doradcza')
    expect(container.textContent).toContain('Licencja oprogramowania')
    expect(container.textContent).toContain('23%')
    expect(container.textContent).toContain('8%')
  })

  it('has a semantic table with caption', async () => {
    await act(async () => {
      root.render(<InvoiceLineTable lines={sampleLines} />)
    })

    expect(container.querySelector('table')).not.toBeNull()
    expect(container.querySelector('caption')).not.toBeNull()
  })

  it('does not show correction columns by default', async () => {
    await act(async () => {
      root.render(<InvoiceLineTable lines={sampleLines} />)
    })

    const headers = [...container.querySelectorAll('th')].map((h) => h.textContent)
    expect(headers.some((t) => t?.toLowerCase().includes('rola'))).toBe(false)
  })

  it('shows correction role column when showCorrectionColumns is true', async () => {
    const correctionLines: InvoiceLineReadDto[] = [
      { ...sampleLines[0]!, correctionRole: 'BeforeCorrection' },
      { ...sampleLines[1]!, correctionRole: 'AfterCorrection' },
    ]

    await act(async () => {
      root.render(<InvoiceLineTable lines={correctionLines} showCorrectionColumns />)
    })

    const headers = [...container.querySelectorAll('th')].map((h) => h.textContent)
    expect(headers.some((t) => t?.toLowerCase().includes('rola'))).toBe(true)
    expect(container.textContent).toContain('BeforeCorrection')
    expect(container.textContent).toContain('AfterCorrection')
  })
})
