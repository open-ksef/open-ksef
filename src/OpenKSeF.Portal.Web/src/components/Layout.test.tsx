// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { Layout } from './Layout'

vi.mock('@/auth/useAuth', () => ({
  useAuth: vi.fn().mockReturnValue({
    user: { profile: { name: 'Portal User' } },
    isLoading: false,
    isAuthenticated: true,
    login: vi.fn(),
    loginWithCredentials: vi.fn(),
    loginWithGoogle: vi.fn(),
    register: vi.fn(),
    logout: vi.fn(),
    getAccessToken: vi.fn(),
  }),
}))

describe('Layout', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('toggles mobile nav visibility using hamburger button', async () => {
    await act(async () => {
      root.render(
        <MemoryRouter>
          <Layout />
        </MemoryRouter>,
      )
    })

    const nav = container.querySelector('[data-testid="mobile-nav"]')
    expect(nav?.getAttribute('data-open')).toBe('false')

    const button = container.querySelector('[data-testid="mobile-nav-toggle"]') as HTMLButtonElement

    await act(async () => {
      button.click()
    })

    expect(nav?.getAttribute('data-open')).toBe('true')
  })

  it('shows Faktury nav group toggle button', async () => {
    await act(async () => {
      root.render(
        <MemoryRouter>
          <Layout />
        </MemoryRouter>,
      )
    })

    const toggle = container.querySelector('[data-testid="invoices-nav-toggle"]')
    expect(toggle).toBeTruthy()
    expect(toggle?.textContent).toContain('Faktury')
  })

  it('toggles Faktury children on click', async () => {
    await act(async () => {
      root.render(
        <MemoryRouter>
          <Layout />
        </MemoryRouter>,
      )
    })

    const toggle = container.querySelector<HTMLButtonElement>('[data-testid="invoices-nav-toggle"]')
    const children = container.querySelector('[data-testid="invoices-nav-children"]')

    expect(children?.className).not.toContain('--open')

    await act(async () => {
      toggle?.click()
    })

    expect(children?.className).toContain('--open')
  })

  it('shows Zakupy and Sprzedaż links inside Faktury group', async () => {
    await act(async () => {
      root.render(
        <MemoryRouter initialEntries={['/invoices/purchases']}>
          <Layout />
        </MemoryRouter>,
      )
    })

    const links = [...container.querySelectorAll('.sidebar-nav-group__child')]
    const labels = links.map((l) => l.textContent)
    expect(labels).toContain('Zakupy')
    expect(labels.some((l) => l?.includes('Sprzeda'))).toBe(true)
  })
})
