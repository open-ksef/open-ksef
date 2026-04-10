// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import { KsefIdentifiersCard } from './KsefIdentifiersCard'

describe('KsefIdentifiersCard', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders both identifiers when available', async () => {
    await act(async () => {
      root.render(<KsefIdentifiersCard ksefDocumentNumber="KSEF-1" ksefReferenceNumber="REF-1" />)
    })

    expect(container.textContent).toContain('Identyfikatory KSeF')
    expect(container.textContent).toContain('KSEF-1')
    expect(container.textContent).toContain('REF-1')
  })

  it('returns nothing when no identifiers are present', async () => {
    await act(async () => {
      root.render(<KsefIdentifiersCard />)
    })

    expect(container.innerHTML).toBe('')
  })
})
