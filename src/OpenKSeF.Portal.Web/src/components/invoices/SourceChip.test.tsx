// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import { SourceChip } from './SourceChip'

describe('SourceChip', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it.each([
    ['Aggregate', 'Aggregate', 'aggregate'],
    ['Synced', 'Sync', 'synced'],
  ] as const)('renders %s source with the expected label', async (source, label, tone) => {
    await act(async () => {
      root.render(<SourceChip source={source} />)
    })

    const chip = container.querySelector('.invoice-source-chip')

    expect(chip?.className).toContain(`invoice-source-chip--${tone}`)
    expect(chip?.getAttribute('aria-label')).toBe(label)
    expect(chip?.textContent).toBe(label)
  })
})
