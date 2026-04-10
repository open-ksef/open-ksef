// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { CurrencySelect } from './CurrencySelect'

describe('CurrencySelect', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders PLN as the default option set and respects the selected value', async () => {
    await act(async () => {
      root.render(<CurrencySelect value="PLN" onChange={() => undefined} />)
    })

    const select = container.querySelector('select')
    const options = Array.from(container.querySelectorAll('option')).map((option) => option.textContent)

    expect(select?.value).toBe('PLN')
    expect(options[0]).toBe('PLN')
    expect(options).toContain('EUR')
  })

  it('emits the selected currency code', async () => {
    const onChange = vi.fn<(value: string) => void>()

    await act(async () => {
      root.render(<CurrencySelect value="PLN" onChange={onChange} />)
    })

    const select = container.querySelector('select') as HTMLSelectElement
    const setValue = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')?.set

    await act(async () => {
      setValue?.call(select, 'EUR')
      select.dispatchEvent(new Event('change', { bubbles: true }))
    })

    expect(onChange).toHaveBeenCalledWith('EUR')
  })
})
