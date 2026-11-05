// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { getOnboardingStatus } from '@/api/endpoints/account'
import { getSetupStatus } from '@/api/endpoints/system'
import { listTenants } from '@/api/endpoints/tenants'
import { useAuth } from '@/auth/useAuth'
import { memoryStorage } from '@/company/memoryStorage'
import { appRoutes } from '@/router'

vi.mock('@/auth/useAuth', () => ({ useAuth: vi.fn() }))
vi.mock('@/api/endpoints/system', () => ({ getSetupStatus: vi.fn() }))
vi.mock('@/api/endpoints/account', () => ({ getOnboardingStatus: vi.fn() }))
vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))
vi.mock('@/api/endpoints/sellerProfile', () => ({ getSellerProfile: vi.fn().mockRejectedValue(new Error('off')) }))
vi.mock('@/api/endpoints/credentials', () => ({ listCredentials: vi.fn().mockResolvedValue([]) }))
vi.mock('@/api/endpoints/dashboard', () => ({ getDashboardOverview: vi.fn().mockResolvedValue([]) }))
vi.mock('@/api/endpoints/devices', () => ({ listDevices: vi.fn().mockResolvedValue([]) }))

const flush = () => new Promise<void>((resolve) => setTimeout(resolve, 0))

describe('Firma routes', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.stubGlobal('localStorage', memoryStorage())
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    vi.mocked(useAuth).mockReturnValue({ user: { profile: { name: 'U' } }, isLoading: false, isAuthenticated: true, logout: vi.fn() } as never)
    vi.mocked(getSetupStatus).mockResolvedValue({ isInitialized: true })
    vi.mocked(getOnboardingStatus).mockResolvedValue({ isComplete: true } as never)
    vi.mocked(listTenants).mockResolvedValue([
      { id: 'a', nip: '1111111111', displayName: 'Firma A', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' },
    ])
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  async function open(path: string) {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter(appRoutes, { initialEntries: [path] })
    await act(async () =>
      root.render(
        <QueryClientProvider client={client}>
          <RouterProvider router={router} />
        </QueryClientProvider>,
      ),
    )
    for (let tick = 0; tick < 8; tick++) await act(flush)
    return router
  }

  it.each([
    ['/company', '/company/invoice-data', ''],
    ['/seller-profile?tenantId=a&next=new', '/company/invoice-data', '?tenantId=a&next=new'],
    ['/credentials?tenantId=a', '/company/token', '?tenantId=a'],
    ['/tenants', '/company/details', ''],
    ['/devices', '/mobile-setup', ''],
  ])('%s moves to its new place and keeps the query', async (from, pathname, search) => {
    const router = await open(from)
    expect(router.state.location.pathname).toBe(pathname)
    expect(router.state.location.search).toBe(search)
  })

  it('shows the three tabs, the current one marked, and the current company', async () => {
    await open('/company/token')
    const tabs = [...container.querySelectorAll('.ui-tabs a')]

    expect(tabs.map((tab) => [tab.textContent, tab.getAttribute('href')])).toEqual([
      ['Dane do faktur', '/company/invoice-data'],
      ['Token KSeF i pobieranie', '/company/token'],
      ['Dane firmy', '/company/details'],
    ])
    expect(tabs.filter((tab) => tab.getAttribute('aria-current') === 'page').map((tab) => tab.textContent)).toEqual(['Token KSeF i pobieranie'])
    expect(container.querySelector('.company-head__sub')?.textContent).toContain('NIP 1111111111')
  })
})
