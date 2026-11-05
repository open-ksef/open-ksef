// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import axe from 'axe-core'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { listTenants } from '@/api/endpoints/tenants'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { Layout } from '@/components/Layout'

vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))

vi.mock('@/auth/useAuth', () => ({
  useAuth: vi.fn().mockReturnValue({
    user: { profile: { name: 'Portal User' } },
    isLoading: false,
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
    getAccessToken: vi.fn(),
  }),
}))

describe('accessibility checks', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.stubGlobal('localStorage', memoryStorage())
    vi.mocked(listTenants).mockResolvedValue([
      { id: 't-1', nip: '1111111111', displayName: 'Firma Testowa', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' },
    ])
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('has no critical axe violations for layout shell', async () => {
    await act(async () => {
      root.render(
        <QueryClientProvider client={new QueryClient()}>
          <MemoryRouter>
            <CompanyProvider>
              <Layout />
            </CompanyProvider>
          </MemoryRouter>
        </QueryClientProvider>,
      )
    })

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0))
    })

    const results = await axe.run(container, {
      rules: {
        'color-contrast': { enabled: false },
      },
    })

    const critical = results.violations.filter((violation) => violation.impact === 'critical')
    expect(critical).toHaveLength(0)
  })
})
