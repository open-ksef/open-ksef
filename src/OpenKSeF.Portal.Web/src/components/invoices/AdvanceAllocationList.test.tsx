// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import type { AdvanceAllocation } from './AdvanceAllocationList'
import { AdvanceAllocationList } from './AdvanceAllocationList'

const allocations: AdvanceAllocation[] = [
  {
    advanceInvoiceId: '11111111-1111-1111-1111-111111111111',
    advanceDocumentNumber: 'ZAL/2026/03/001',
    settledAmount: { amount: 500, currency: 'PLN' },
  },
  {
    advanceInvoiceId: '22222222-2222-2222-2222-222222222222',
    advanceDocumentNumber: 'ZAL/2026/03/002',
    settledAmount: { amount: 300, currency: 'PLN' },
  },
]

describe('AdvanceAllocationList', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders one row per allocation', async () => {
    await act(async () => {
      root.render(<AdvanceAllocationList allocations={allocations} />)
    })

    const rows = container.querySelectorAll('[data-testid="advance-row"]')
    expect(rows.length).toBe(2)
  })

  it('shows document numbers and amounts', async () => {
    await act(async () => {
      root.render(<AdvanceAllocationList allocations={allocations} />)
    })

    expect(container.textContent).toContain('ZAL/2026/03/001')
    expect(container.textContent).toContain('ZAL/2026/03/002')
    expect(container.textContent).toContain('500')
    expect(container.textContent).toContain('300')
  })

  it('renders nothing when list is empty', async () => {
    await act(async () => {
      root.render(<AdvanceAllocationList allocations={[]} />)
    })

    expect(container.querySelector('section')).toBeNull()
  })
})
