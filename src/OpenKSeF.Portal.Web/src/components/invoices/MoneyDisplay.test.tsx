// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import { MoneyDisplay } from './MoneyDisplay'

describe('MoneyDisplay', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('formats amount with currency for visible text and screen readers', async () => {
    await act(async () => {
      root.render(<MoneyDisplay value={{ amount: 199.5, currency: 'PLN' }} />)
    })

    const element = container.querySelector('.money-display')

    expect(element?.textContent).toContain('199,50')
    expect(element?.textContent).toContain('PLN')
    expect(element?.getAttribute('aria-label')).toContain('PLN')
  })
})
