// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import { PartyCard } from './PartyCard'

describe('PartyCard', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders name and NIP in a read-only card', async () => {
    await act(async () => {
      root.render(<PartyCard title="Sprzedawca" party={{ name: 'Acme', nip: '1234563218' }} />)
    })

    expect(container.textContent).toContain('Sprzedawca')
    expect(container.textContent).toContain('Acme')
    expect(container.textContent).toContain('1234563218')
  })

  it('falls back to em dash when NIP is missing', async () => {
    await act(async () => {
      root.render(<PartyCard party={{ name: 'Jan Kowalski', nip: null }} />)
    })

    expect(container.textContent).toContain('—')
  })
})
