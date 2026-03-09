// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { getInvoiceByKSeFNumber } from '@/api/endpoints/invoices'
import { listTenants } from '@/api/endpoints/tenants'
import { ApiError } from '@/api/errors'
import { InvoiceDetailsPage } from './InvoiceDetails'

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn(),
}))

vi.mock('@/api/endpoints/invoices', () => ({
  getInvoiceByKSeFNumber: vi.fn(),
  getTransferDetails: vi.fn(),
  setInvoicePaid: vi.fn(),
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

describe('InvoiceDetailsPage', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders details card with required fields', async () => {
    vi.mocked(listTenants).mockResolvedValue([
      {
        id: 'tenant-1',
        nip: '1234567890',
        displayName: 'Tenant A',
        notificationEmail: null,
        createdAt: new Date().toISOString(),
      },
    ])

    vi.mocked(getInvoiceByKSeFNumber).mockResolvedValue({
      id: 'inv-1',
      ksefInvoiceNumber: 'KSEF-001',
      ksefReferenceNumber: 'REF-001',
      invoiceNumber: 'FV/2026/001',
      vendorName: 'Vendor A',
      vendorNip: '9876543210',
      buyerName: 'Buyer B',
      buyerNip: '0987654321',
      amountNet: 162.2,
      amountVat: 37.3,
      amountGross: 199.5,
      currency: 'PLN',
      issueDate: '2026-02-01T00:00:00Z',
      acquisitionDate: '2026-02-01T00:05:00Z',
      invoiceType: 'VAT',
      firstSeenAt: '2026-02-01T12:00:00Z',
      isPaid: false,
      paidAt: null,
    })

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter(
      [
        {
          path: '/invoices/:ksefInvoiceNumber',
          element: (
            <QueryClientProvider client={client}>
              <InvoiceDetailsPage />
            </QueryClientProvider>
          ),
        },
      ],
      { initialEntries: ['/invoices/KSEF-001?tenantId=tenant-1'] },
    )

    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="invoice-details-card"]') !== null)
    })

    expect(container.querySelector('[data-testid="invoice-detail-ksef-number"]')?.textContent).toContain('KSEF-001')
    expect(container.querySelector('[data-testid="invoice-detail-vendor-name"]')?.textContent).toContain('Vendor A')
    expect(container.querySelector('[data-testid="invoice-detail-vendor-nip"]')?.textContent).toContain('9876543210')
    expect(container.querySelector('[data-testid="invoice-detail-issue-date"]')?.textContent).toContain('2026')
    expect(container.querySelector('[data-testid="invoice-detail-amount"]')?.textContent).toContain('199')
    expect(container.querySelector('[data-testid="invoice-detail-amount"]')?.textContent).toContain('PLN')
    expect(container.querySelector('[data-testid="invoice-details-back-link"]')).toBeTruthy()
  })

  it('renders not found state for 404 errors', async () => {
    vi.mocked(listTenants).mockResolvedValue([
      {
        id: 'tenant-1',
        nip: '1234567890',
        displayName: 'Tenant A',
        notificationEmail: null,
        createdAt: new Date().toISOString(),
      },
    ])
    vi.mocked(getInvoiceByKSeFNumber).mockRejectedValue(new ApiError('Not found', 404))

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter(
      [
        {
          path: '/invoices/:ksefInvoiceNumber',
          element: (
            <QueryClientProvider client={client}>
              <InvoiceDetailsPage />
            </QueryClientProvider>
          ),
        },
      ],
      { initialEntries: ['/invoices/KSEF-404?tenantId=tenant-1'] },
    )

    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => (container.textContent ?? '').includes('Nie znaleziono faktury'))
    })

    expect(container.textContent).toContain('Nie znaleziono żądanej faktury.')
    expect(container.querySelector('[data-testid="invoice-details-back-link"]')).toBeTruthy()
  })
})
