// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import { StatusBadge } from './StatusBadge'

describe('StatusBadge', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('maps warning status to warning css class', async () => {
    await act(async () => {
      root.render(<StatusBadge status="warning" label="Warning" />)
    })

    const badge = container.querySelector('.ui-status-badge')
    expect(badge?.className).toContain('ui-status-badge--warning')
    expect(badge?.textContent).toBe('Warning')
  })
})
