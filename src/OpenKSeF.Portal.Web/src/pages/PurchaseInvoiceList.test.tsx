// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { listInvoices } from '@/api/endpoints/invoices'
import { listTenants } from '@/api/endpoints/tenants'
import { PurchaseInvoiceListPage } from './PurchaseInvoiceList'

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

const defaultTenant = [
  {
    id: 'tenant-1',
    nip: '1234567890',
    displayName: 'Tenant A',
    notificationEmail: null,
    createdAt: new Date().toISOString(),
  },
]

const emptySyncedPage = {
  items: [],
  page: 1,
  pageSize: 25,
  totalCount: 0,
  totalPages: 0,
}

function renderList(initialEntries = ['/invoices/purchases?tenantId=tenant-1&page=1&pageSize=25']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [
      {
        path: '/invoices/purchases',
        element: (
          <QueryClientProvider client={client}>
            <PurchaseInvoiceListPage />
          </QueryClientProvider>
        ),
      },
      { path: '/invoices/:ksefInvoiceNumber', element: <div data-testid="synced-detail">detail</div> },
    ],
    { initialEntries },
  )
  return { client, router }
}

describe('PurchaseInvoiceListPage', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders purchase invoice table with synced data only', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
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
      pageSize: 25,
      totalCount: 1,
      totalPages: 1,
    })

    const { router } = renderList()

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

    expect(container.querySelector('[data-testid="invoice-number"]')?.textContent).toContain('FV/2026/001')
    expect(container.querySelector('[data-testid="invoice-ksef-number"]')?.textContent).toContain('KSEF-001')
    expect(container.querySelector('[data-testid="invoice-view-details"]')).toBeTruthy()

    expect(listInvoices).toHaveBeenCalledWith('tenant-1', expect.objectContaining({ page: 1, pageSize: 25 }))
  })

  it('shows "Faktury zakupu" title', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listInvoices).mockResolvedValue(emptySyncedPage)

    const { router } = renderList()

    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
    })

    expect(container.textContent).toContain('Faktury zakupu')
  })

  it('shows error banner when invoice query fails', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listInvoices).mockRejectedValue(new Error('Network failed'))

    const { router } = renderList(['/invoices/purchases?tenantId=tenant-1'])

    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => (container.textContent ?? '').includes('Network failed'))
    })

    expect(container.textContent).toContain('Network failed')
  })
})
