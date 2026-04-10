// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import { TotalsSummaryCard } from './TotalsSummaryCard'

describe('TotalsSummaryCard', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders net, vat and gross amounts with labels', async () => {
    await act(async () => {
      root.render(
        <TotalsSummaryCard
          net={{ amount: 1000, currency: 'PLN' }}
          vat={{ amount: 230, currency: 'PLN' }}
          gross={{ amount: 1230, currency: 'PLN' }}
          currency="PLN"
        />,
      )
    })

    expect(container.textContent).toContain('Netto')
    expect(container.textContent).toContain('VAT')
    expect(container.textContent).toContain('Brutto')
    expect(container.textContent).toContain('1')
    expect(container.textContent).toContain('230')
    expect(container.textContent).toContain('PLN')
  })

  it('uses the section role with accessible label', async () => {
    await act(async () => {
      root.render(
        <TotalsSummaryCard
          net={{ amount: 100, currency: 'EUR' }}
          vat={{ amount: 23, currency: 'EUR' }}
          gross={{ amount: 123, currency: 'EUR' }}
          currency="EUR"
        />,
      )
    })

    const section = container.querySelector('section')
    expect(section?.getAttribute('aria-label')).toBeTruthy()
  })
})
