// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { listTenants, createTenant, updateTenant, deleteTenant } from '@/api/endpoints/tenants'
import { TenantListPage } from './TenantList'

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn(),
  createTenant: vi.fn(),
  updateTenant: vi.fn(),
  deleteTenant: vi.fn(),
}))

describe('accessibility keyboard support', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true

    vi.mocked(listTenants).mockResolvedValue([
      {
        id: 'tenant-1',
        nip: '1234567890',
        displayName: 'Tenant A',
        notificationEmail: 'a@example.com',
        createdAt: '2026-01-01T00:00:00Z',
      },
    ])

    vi.mocked(createTenant).mockResolvedValue({
      id: 'tenant-2',
      nip: '0987654321',
      displayName: 'Tenant B',
      notificationEmail: null,
      createdAt: '2026-01-01T00:00:00Z',
    })

    vi.mocked(updateTenant).mockResolvedValue({
      id: 'tenant-1',
      nip: '1234567890',
      displayName: 'Tenant A',
      notificationEmail: 'a@example.com',
      createdAt: '2026-01-01T00:00:00Z',
    })

    vi.mocked(deleteTenant).mockResolvedValue()

    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('closes create tenant form with Escape key', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    await act(async () => {
      root.render(
        <QueryClientProvider client={client}>
          <TenantListPage />
        </QueryClientProvider>,
      )
      await new Promise((resolve) => setTimeout(resolve, 0))
      await new Promise((resolve) => setTimeout(resolve, 0))
    })

    await act(async () => {
      ;(container.querySelector('[data-testid="tenant-create-button"]') as HTMLButtonElement).click()
    })

    expect(container.querySelector('[data-testid="tenant-form-submit"]')).toBeTruthy()

    await act(async () => {
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    })

    expect(container.querySelector('[data-testid="tenant-form-submit"]')).toBeNull()
  })
})
