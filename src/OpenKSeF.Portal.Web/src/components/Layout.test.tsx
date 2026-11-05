// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { listTenants } from '@/api/endpoints/tenants'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { Layout } from './Layout'

vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))

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
    vi.stubGlobal('localStorage', memoryStorage())
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
    vi.mocked(listTenants).mockResolvedValue([
      { id: 't-1', nip: '1111111111', displayName: 'Firma Testowa', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' },
    ])
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  async function render(path: string): Promise<void> {
    await act(async () => {
      root.render(
        <QueryClientProvider client={new QueryClient()}>
          <MemoryRouter initialEntries={[path]}>
            <CompanyProvider>
              <Layout />
            </CompanyProvider>
          </MemoryRouter>
        </QueryClientProvider>,
      )
    })
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 10))
    })
  }

  const currentItems = () =>
    [...container.querySelectorAll('[aria-current="page"]')].map((item) => item.getAttribute('aria-label') ?? item.textContent)

  it('has at most 6 top-level menu items', async () => {
    await render('/')

    const items = [...container.querySelectorAll('[data-testid="mobile-nav"] a')].map((link) => link.textContent)
    expect(items).toEqual(['Start', 'Otrzymane', 'Wystawione', 'Firma', 'Telefon'])
    expect(items.length + container.querySelectorAll('[data-testid="nav-settings"]').length).toBeLessThanOrEqual(6)
  })

  it.each([
    ['/', 'Start'],
    ['/invoices/KSEF-1', 'Otrzymane'],
    ['/issued/new', 'Wystawione'],
    ['/company', 'Firma'],
    ['/company/invoice-data', 'Firma'],
    ['/company/token', 'Firma'],
    ['/company/details', 'Firma'],
    ['/mobile-setup', 'Telefon'],
    ['/settings', 'Ustawienia'],
  ])('marks exactly one item on %s', async (path, item) => {
    await render(path)

    expect(currentItems()).toEqual([item])
  })

  it('renders every icon from the sprite, with no glyphs', async () => {
    await render('/')

    const hrefs = [...container.querySelectorAll('use')].map((use) => use.getAttribute('href'))
    expect(hrefs).toContain('/icons.svg#i-house')
    expect(hrefs).toContain('/icons.svg#i-log-out')
    expect(container.textContent).not.toMatch(/[⚡⊞≡✎▤◉⚿◈⬡⚙↪☰]/u)
  })

  it('shows the current company and a sign-out control', async () => {
    await render('/')

    expect(container.querySelector('[data-testid="company-card"]')?.textContent).toContain('Firma Testowa')
    expect(container.querySelector('[data-testid="company-card"]')?.textContent).toContain('NIP 1111111111')
    expect(container.querySelector('[data-testid="nav-logout"]')?.getAttribute('aria-label')).toBe('Wyloguj')
  })
})
