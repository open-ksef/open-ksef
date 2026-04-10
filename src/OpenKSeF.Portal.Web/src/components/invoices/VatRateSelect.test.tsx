// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { VatRateSelect } from './VatRateSelect'

describe('VatRateSelect', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders the supported policy-aware VAT rate options', async () => {
    await act(async () => {
      root.render(<VatRateSelect value="23%" onChange={() => undefined} />)
    })

    const options = Array.from(container.querySelectorAll('option')).map((option) => option.textContent)

    expect(options).toEqual(['23%', '8%', '5%', '0%', 'zw', 'np'])
  })

  it('emits the selected VAT rate', async () => {
    const onChange = vi.fn<(value: string) => void>()

    await act(async () => {
      root.render(<VatRateSelect value="23%" onChange={onChange} />)
    })

    const select = container.querySelector('select') as HTMLSelectElement
    const setValue = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')?.set

    await act(async () => {
      setValue?.call(select, 'zw')
      select.dispatchEvent(new Event('change', { bubbles: true }))
    })

    expect(onChange).toHaveBeenCalledWith('zw')
  })
})
