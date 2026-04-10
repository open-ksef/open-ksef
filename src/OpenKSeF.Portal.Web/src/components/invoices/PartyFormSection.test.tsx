// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { PartyFormSection } from './PartyFormSection'

describe('PartyFormSection', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders editable party fields and optional NIP input', async () => {
    await act(async () => {
      root.render(
        <PartyFormSection
          title="Nabywca"
          value={{ name: 'Buyer', nip: '1234563218' }}
          onChange={() => undefined}
          showNip
        />,
      )
    })

    expect(container.textContent).toContain('Nabywca')
    expect(container.querySelectorAll('input').length).toBe(2)
  })

  it('emits name changes through the shared party shape', async () => {
    const onChange = vi.fn<(value: { name: string; nip: string | null }) => void>()

    await act(async () => {
      root.render(<PartyFormSection value={{ name: 'Old', nip: null }} onChange={onChange} showNip={false} />)
    })

    const input = container.querySelector('input') as HTMLInputElement
    const setValue = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set

    await act(async () => {
      setValue?.call(input, 'New Name')
      input.dispatchEvent(new Event('input', { bubbles: true }))
    })

    expect(onChange).toHaveBeenCalledWith({ name: 'New Name', nip: null })
  })
})
