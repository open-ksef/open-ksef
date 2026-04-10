// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import { IssueDatesFieldset } from './IssueDatesFieldset'

describe('IssueDatesFieldset', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders the shared issue, sale, and due date inputs', async () => {
    await act(async () => {
      root.render(
        <IssueDatesFieldset
          value={{ issueDate: '2026-04-10', saleDate: '2026-04-11', dueDate: '2026-04-20' }}
          onChange={() => undefined}
        />,
      )
    })

    expect(container.textContent).toContain('Daty dokumentu')
    expect(container.querySelectorAll('input[type="date"]').length).toBe(3)
  })

  it('supports empty optional sale and due dates', async () => {
    await act(async () => {
      root.render(
        <IssueDatesFieldset
          value={{ issueDate: '2026-04-10', saleDate: null, dueDate: null }}
          onChange={() => undefined}
        />,
      )
    })

    const inputs = container.querySelectorAll('input[type="date"]') as NodeListOf<HTMLInputElement>
    expect(inputs[1]?.value).toBe('')
    expect(inputs[2]?.value).toBe('')
  })
})
