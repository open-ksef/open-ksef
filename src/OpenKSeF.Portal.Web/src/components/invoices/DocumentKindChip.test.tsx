// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import { DocumentKindChip } from './DocumentKindChip'

describe('DocumentKindChip', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it.each([
    ['VatInvoice', 'Faktura VAT', 'vat'],
    ['AdvanceInvoice', 'Faktura zaliczkowa', 'advance'],
    ['FinalInvoice', 'Faktura finalna', 'final'],
    ['Proforma', 'Pro forma', 'proforma'],
    ['CorrectionInvoice', 'Faktura korygująca', 'correction'],
  ] as const)('renders %s with a distinct Polish label', async (kind, label, tone) => {
    await act(async () => {
      root.render(<DocumentKindChip kind={kind} />)
    })

    const chip = container.querySelector('.invoice-kind-chip')

    expect(chip?.className).toContain(`invoice-kind-chip--${tone}`)
    expect(chip?.getAttribute('aria-label')).toBe(label)
    expect(chip?.textContent).toBe(label)
  })
})
