// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { FilterBar } from './FilterBar'

describe('FilterBar', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders child filters and apply/reset handlers', async () => {
    const onApply = vi.fn()
    const onReset = vi.fn()

    await act(async () => {
      root.render(
        <FilterBar onApply={onApply} onReset={onReset}>
          <input aria-label="Search" />
        </FilterBar>,
      )
    })

    expect(container.querySelector('input')).toBeTruthy()
    const buttons = container.querySelectorAll('button')
    ;(buttons[0] as HTMLButtonElement).click()
    ;(buttons[1] as HTMLButtonElement).click()

    expect(onApply).toHaveBeenCalledTimes(1)
    expect(onReset).toHaveBeenCalledTimes(1)
  })
})
