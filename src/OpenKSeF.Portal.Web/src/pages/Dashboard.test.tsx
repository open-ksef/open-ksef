// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { getDashboardOverview } from '@/api/endpoints/dashboard'
import { DashboardPage } from './Dashboard'

vi.mock('@/api/endpoints/dashboard', () => ({
  getDashboardOverview: vi.fn(),
}))

function flushPromises() {
  return new Promise<void>((resolve) => setTimeout(resolve, 0))
}

async function waitFor(assertion: () => boolean, timeoutMs = 1000): Promise<void> {
  const start = Date.now()
  while (Date.now() - start < timeoutMs) {
    if (assertion()) return
    await flushPromises()
  }

  throw new Error('Condition not met within timeout')
}

describe('DashboardPage', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders tenant overview cards with sync status and counts', async () => {
    vi.mocked(getDashboardOverview).mockResolvedValue([
      {
        tenantId: 'tenant-1',
        nip: '1234567890',
        displayName: 'Tenant A',
        lastSyncedAt: '2026-02-01T10:00:00Z',
        lastSuccessfulSync: '2026-02-01T09:00:00Z',
        totalInvoices: 12,
        invoicesLast7Days: 3,
        invoicesLast30Days: 8,
        syncStatus: 'Success',
      },
    ])

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    await act(async () => {
      root.render(
        <QueryClientProvider client={client}>
          <MemoryRouter>
            <DashboardPage />
          </MemoryRouter>
        </QueryClientProvider>,
      )
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => (container.textContent ?? '').includes('Tenant A'))
    })

    expect(container.textContent).toContain('OK')
    expect(container.textContent).toContain('12')
    expect(container.textContent).toContain('3')
    expect(container.textContent).toContain('8')
    expect(container.querySelector('[data-testid="dashboard-link-invoices"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="dashboard-link-tenants"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="dashboard-link-credentials"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="dashboard-refresh-button"]')).toBeTruthy()
  })

  it('renders empty state when there are no tenants', async () => {
    vi.mocked(getDashboardOverview).mockResolvedValue([])
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    await act(async () => {
      root.render(
        <QueryClientProvider client={client}>
          <MemoryRouter>
            <DashboardPage />
          </MemoryRouter>
        </QueryClientProvider>,
      )
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => (container.textContent ?? '').includes('Brak skonfigurowanych firm'))
    })

    expect(container.textContent).toContain('Dodaj pierwszą firmę, aby rozpocząć synchronizację faktur i monitorowanie statusu.')
  })
})
