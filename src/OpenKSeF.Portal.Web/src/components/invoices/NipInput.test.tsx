// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { NipInput } from './NipInput'

describe('NipInput', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('marks invalid values with aria attributes and inline error text', async () => {
    await act(async () => {
      root.render(<NipInput value="1234567890" onChange={() => undefined} />)
    })

    const input = container.querySelector('input')
    const error = container.querySelector('.nip-input__error')

    expect(input?.getAttribute('aria-invalid')).toBe('true')
    expect(input?.getAttribute('aria-describedby')).toBeTruthy()
    expect(error?.textContent).toContain('Wprowadź poprawny numer NIP.')
  })

  it('normalizes user input to digits only and max 10 chars', async () => {
    const onChange = vi.fn<(value: string) => void>()

    await act(async () => {
      root.render(<NipInput value="" onChange={onChange} />)
    })

    const input = container.querySelector('input') as HTMLInputElement
    const setValue = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set

    await act(async () => {
      setValue?.call(input, '123-456-32-18abc')
      input.dispatchEvent(new Event('change', { bubbles: true }))
    })

    expect(onChange).toHaveBeenCalledWith('1234563218')
  })
})
