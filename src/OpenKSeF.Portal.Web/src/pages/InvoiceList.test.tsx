// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { listInvoices } from '@/api/endpoints/invoices'
import { listTenants } from '@/api/endpoints/tenants'
import { InvoiceListPage } from './InvoiceList'

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn(),
}))

vi.mock('@/api/endpoints/invoices', () => ({
  listInvoices: vi.fn(),
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

describe('InvoiceListPage', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders invoice table with required data-testids', async () => {
    vi.mocked(listTenants).mockResolvedValue([
      {
        id: 'tenant-1',
        nip: '1234567890',
        displayName: 'Tenant A',
        notificationEmail: null,
        createdAt: new Date().toISOString(),
      },
    ])

    vi.mocked(listInvoices).mockResolvedValue({
      items: [
        {
          id: 'inv-1',
          ksefInvoiceNumber: 'KSEF-001',
          ksefReferenceNumber: 'REF-001',
          invoiceNumber: 'FV/2026/001',
          vendorName: 'Vendor A',
          vendorNip: '9876543210',
          buyerName: null,
          buyerNip: null,
          amountNet: 162.2,
          amountVat: 37.3,
          amountGross: 199.5,
          currency: 'PLN',
          issueDate: '2026-02-01T00:00:00Z',
          acquisitionDate: null,
          invoiceType: null,
          firstSeenAt: '2026-02-01T12:00:00Z',
          isPaid: false,
          paidAt: null,
        },
      ],
      page: 1,
      pageSize: 10,
      totalCount: 1,
      totalPages: 1,
    })

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter(
      [
        {
          path: '/invoices',
          element: (
            <QueryClientProvider client={client}>
              <InvoiceListPage />
            </QueryClientProvider>
          ),
        },
      ],
      { initialEntries: ['/invoices?tenantId=tenant-1&page=1&pageSize=10'] },
    )

    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="invoice-table"]') !== null)
    })

    expect(container.querySelector('[data-testid="invoice-tenant-filter"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="invoice-date-from"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="invoice-date-to"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="invoice-page-size"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="invoice-apply-filters"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="invoice-refresh-button"]')).toBeTruthy()

    expect(container.querySelector('[data-testid="invoice-table"]')).toBeTruthy()
    expect(container.querySelectorAll('[data-testid="invoice-row"]').length).toBe(1)
    expect(container.querySelector('[data-testid="invoice-ksef-number"]')?.textContent).toContain('KSEF-001')
    expect(container.querySelector('[data-testid="invoice-view-details"]')).toBeTruthy()

    expect(listInvoices).toHaveBeenCalledWith('tenant-1', expect.objectContaining({ page: 1, pageSize: 10 }))
  })

  it('applies filters by updating URL and resetting page to 1', async () => {
    vi.mocked(listTenants).mockResolvedValue([
      {
        id: 'tenant-1',
        nip: '1234567890',
        displayName: 'Tenant A',
        notificationEmail: null,
        createdAt: new Date().toISOString(),
      },
    ])

    vi.mocked(listInvoices).mockResolvedValue({
      items: [],
      page: 3,
      pageSize: 25,
      totalCount: 0,
      totalPages: 0,
    })

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter(
      [
        {
          path: '/invoices',
          element: (
            <QueryClientProvider client={client}>
              <InvoiceListPage />
            </QueryClientProvider>
          ),
        },
      ],
      { initialEntries: ['/invoices?tenantId=tenant-1&dateFrom=2026-01-01&dateTo=2026-01-31&page=3&pageSize=25'] },
    )

    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    const applyButton = container.querySelector('[data-testid="invoice-apply-filters"]') as HTMLButtonElement

    await act(async () => {
      applyButton.click()
      await flushPromises()
      await flushPromises()
    })

    expect(router.state.location.search).toContain('page=1')
    expect(router.state.location.search).toContain('dateFrom=2026-01-01')
    expect(router.state.location.search).toContain('dateTo=2026-01-31')
  })

  it('shows error banner when invoice query fails', async () => {
    vi.mocked(listTenants).mockResolvedValue([
      {
        id: 'tenant-1',
        nip: '1234567890',
        displayName: 'Tenant A',
        notificationEmail: null,
        createdAt: new Date().toISOString(),
      },
    ])

    vi.mocked(listInvoices).mockRejectedValue(new Error('Network failed'))

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter(
      [
        {
          path: '/invoices',
          element: (
            <QueryClientProvider client={client}>
              <InvoiceListPage />
            </QueryClientProvider>
          ),
        },
      ],
      { initialEntries: ['/invoices?tenantId=tenant-1'] },
    )

    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => (container.textContent ?? '').includes('Network failed'))
    })

    expect(container.textContent).toContain('Network failed')
    expect(container.textContent).toContain('Retry')
  })
})
