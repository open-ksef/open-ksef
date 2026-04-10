// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { MoneyInput } from './MoneyInput'

describe('MoneyInput', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders a decimal input with pricing mode and currency suffix', async () => {
    await act(async () => {
      root.render(
        <MoneyInput value={{ amount: 123.45, currency: 'PLN' }} onChange={() => undefined} pricingMode="Net" />,
      )
    })

    const input = container.querySelector('input')

    expect(input?.getAttribute('inputmode')).toBe('decimal')
    expect(container.textContent).toContain('PLN')
    expect(container.textContent).toContain('netto')
  })

  it('normalizes comma decimals and emits a typed money value', async () => {
    const onChange = vi.fn<(value: { amount: number; currency: string }) => void>()

    await act(async () => {
      root.render(<MoneyInput value={{ amount: 10, currency: 'EUR' }} onChange={onChange} />)
    })

    const input = container.querySelector('input') as HTMLInputElement
    const setValue = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set

    await act(async () => {
      setValue?.call(input, '44,90')
      input.dispatchEvent(new Event('change', { bubbles: true }))
    })

    expect(onChange).toHaveBeenCalledWith({ amount: 44.9, currency: 'EUR' })
  })
})
