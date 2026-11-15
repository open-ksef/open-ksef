// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { getSetupStatus } from '@/api/endpoints/system'
import { PrivacyPage } from './Privacy'

vi.mock('@/api/endpoints/system', () => ({ getSetupStatus: vi.fn() }))

describe('PrivacyPage', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  async function render(): Promise<HTMLElement> {
    await act(async () => root.render(<QueryClientProvider client={new QueryClient()}><PrivacyPage /></QueryClientProvider>))
    for (let i = 0; i < 50 && container.textContent?.includes('Ładowanie'); i++) {
      await act(() => new Promise((resolve) => setTimeout(resolve, 0)))
    }
    return container.querySelector<HTMLElement>('[data-testid="privacy-controller"]')!
  }

  it('names the controller and links an e-mail contact', async () => {
    vi.mocked(getSetupStatus).mockResolvedValue({ isInitialized: true, dataControllerName: 'Firma Testowa Sp. z o.o.', dataControllerContact: 'rodo@example.com' })
    const section = await render()

    expect(section.textContent).toContain('Administratorem danych osobowych w tej instancji OpenKSeF jest Firma Testowa Sp. z o.o.')
    expect(section.querySelector('a')?.getAttribute('href')).toBe('mailto:rodo@example.com')
    expect(container.querySelector('h1')?.textContent).toBe('Prywatność')
  })

  it('adds no second dot after a name ending in a dot', async () => {
    vi.mocked(getSetupStatus).mockResolvedValue({ isInitialized: true, dataControllerName: 'Firma Testowa Sp. z o.o.', dataControllerContact: null })
    const section = await render()

    expect(section.textContent).toContain('jest Firma Testowa Sp. z o.o.W sprawach')
    expect(section.textContent).not.toContain('o.o..')
  })

  it('shows a postal contact as plain text', async () => {
    vi.mocked(getSetupStatus).mockResolvedValue({ isInitialized: true, dataControllerName: 'Firma Testowa', dataControllerContact: 'ul. Przykładowa 1, 00-001 Warszawa' })
    const section = await render()

    expect(section.textContent).toContain('Kontakt: ul. Przykładowa 1, 00-001 Warszawa')
    expect(section.querySelector('a')).toBeNull()
  })

  it('falls back when the controller is not set, keeping the rest', async () => {
    vi.mocked(getSetupStatus).mockResolvedValue({ isInitialized: true, dataControllerName: null, dataControllerContact: null })
    const section = await render()

    expect(section.textContent).toContain('Administrator tej instancji nie podał jeszcze swoich danych.')
    expect(container.textContent).toContain('Jakie dane przechowuje ta instancja')
    expect(container.querySelector('a[href="https://open-ksef.pl/docs/polityka-prywatnosci"]')).not.toBeNull()
  })
})
