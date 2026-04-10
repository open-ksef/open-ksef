// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { AdvanceOption } from './AdvanceAllocationPicker'
import { AdvanceAllocationPicker } from './AdvanceAllocationPicker'

const advances: AdvanceOption[] = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    documentNumber: 'ZAL/2026/03/001',
    grossAmount: { amount: 500, currency: 'PLN' },
  },
  {
    id: '22222222-2222-2222-2222-222222222222',
    documentNumber: 'ZAL/2026/03/002',
    grossAmount: { amount: 300, currency: 'PLN' },
  },
]

describe('AdvanceAllocationPicker', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders a checkbox for each advance', async () => {
    await act(async () => {
      root.render(<AdvanceAllocationPicker advances={advances} selected={[]} onChange={() => {}} />)
    })

    const checkboxes = container.querySelectorAll('input[type="checkbox"]')
    expect(checkboxes.length).toBe(2)
  })

  it('checks the selected advances', async () => {
    await act(async () => {
      root.render(
        <AdvanceAllocationPicker
          advances={advances}
          selected={['11111111-1111-1111-1111-111111111111']}
          onChange={() => {}}
        />,
      )
    })

    const checkboxes = container.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')
    expect(checkboxes[0]?.checked).toBe(true)
    expect(checkboxes[1]?.checked).toBe(false)
  })

  it('calls onChange with the toggled selection', async () => {
    const onChange = vi.fn()
    await act(async () => {
      root.render(<AdvanceAllocationPicker advances={advances} selected={[]} onChange={onChange} />)
    })

    const checkboxes = container.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')
    await act(async () => {
      checkboxes[0]?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    })

    expect(onChange).toHaveBeenCalledWith(['11111111-1111-1111-1111-111111111111'])
  })

  it('shows a running total for selected advances', async () => {
    await act(async () => {
      root.render(
        <AdvanceAllocationPicker
          advances={advances}
          selected={['11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222']}
          onChange={() => {}}
        />,
      )
    })

    expect(container.textContent).toContain('800')
  })
})
