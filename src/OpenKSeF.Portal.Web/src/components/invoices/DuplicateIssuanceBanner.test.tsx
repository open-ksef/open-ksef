// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import type { DuplicateIssuance } from './DuplicateIssuanceBanner'
import { DuplicateIssuanceBanner } from './DuplicateIssuanceBanner'

const issuances: DuplicateIssuance[] = [
  { issuedAt: '2026-03-15T10:00:00Z', issuedBy: 'jan.kowalski@example.com' },
  { issuedAt: '2026-04-01T14:30:00Z', issuedBy: null },
]

describe('DuplicateIssuanceBanner', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders nothing when issuances array is empty', async () => {
    await act(async () => {
      root.render(<DuplicateIssuanceBanner issuances={[]} />)
    })

    expect(container.querySelector('[role="note"]')).toBeNull()
  })

  it('renders the banner when there are issuances', async () => {
    await act(async () => {
      root.render(<DuplicateIssuanceBanner issuances={issuances} />)
    })

    expect(container.querySelector('[role="note"]')).not.toBeNull()
  })

  it('shows the count of duplicate issuances', async () => {
    await act(async () => {
      root.render(<DuplicateIssuanceBanner issuances={issuances} />)
    })

    expect(container.textContent).toContain('2')
  })

  it('shows the issuer name when present', async () => {
    await act(async () => {
      root.render(<DuplicateIssuanceBanner issuances={issuances} />)
    })

    expect(container.textContent).toContain('jan.kowalski@example.com')
  })
})
