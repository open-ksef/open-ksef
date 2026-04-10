// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { PrintVariantSwitcher } from './PrintVariantSwitcher'

describe('PrintVariantSwitcher', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders three variant options', async () => {
    await act(async () => {
      root.render(<PrintVariantSwitcher variant="Standard" onChange={() => {}} />)
    })

    const radios = container.querySelectorAll('input[type="radio"]')
    expect(radios.length).toBe(3)
  })

  it('marks the current variant as checked', async () => {
    await act(async () => {
      root.render(<PrintVariantSwitcher variant="Duplicate" onChange={() => {}} />)
    })

    const radios = container.querySelectorAll<HTMLInputElement>('input[type="radio"]')
    const checked = [...radios].find((r) => r.checked)
    expect(checked?.value).toBe('Duplicate')
  })

  it('calls onChange when another variant is selected', async () => {
    const onChange = vi.fn()
    await act(async () => {
      root.render(<PrintVariantSwitcher variant="Standard" onChange={onChange} />)
    })

    const englishRadio = container.querySelector<HTMLInputElement>('input[value="English"]')
    await act(async () => {
      englishRadio?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    })

    expect(onChange).toHaveBeenCalledWith('English')
  })

  it('disables variants listed in disabledVariants', async () => {
    await act(async () => {
      root.render(<PrintVariantSwitcher variant="Standard" onChange={() => {}} disabledVariants={['Duplicate']} />)
    })

    const duplicateRadio = container.querySelector<HTMLInputElement>('input[value="Duplicate"]')
    expect(duplicateRadio?.disabled).toBe(true)
  })

  it('has role="radiogroup"', async () => {
    await act(async () => {
      root.render(<PrintVariantSwitcher variant="Standard" onChange={() => {}} />)
    })

    expect(container.querySelector('[role="radiogroup"]')).not.toBeNull()
  })
})
