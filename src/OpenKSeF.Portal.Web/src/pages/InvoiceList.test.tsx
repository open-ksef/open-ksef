// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { listAggregateInvoices } from '@/api/invoicesAggregateApi'
import { listInvoices } from '@/api/endpoints/invoices'
import { listTenants } from '@/api/endpoints/tenants'
import { InvoiceListPage } from './InvoiceList'

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn(),
}))

vi.mock('@/api/endpoints/invoices', () => ({
  listInvoices: vi.fn(),
}))

vi.mock('@/api/invoicesAggregateApi', () => ({
  listAggregateInvoices: vi.fn(),
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

const emptyAggregatePage = {
  items: [],
  page: 1,
  pageSize: 25,
  totalCount: 0,
  totalPages: 0,
}

function renderList(initialEntries = ['/invoices?tenantId=tenant-1&page=1&pageSize=10']) {
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
      { path: '/invoices/new', element: <div data-testid="create-draft-page">Nowa faktura</div> },
      { path: '/invoices/final-from-advances', element: <div data-testid="final-advances-page">Finalna z zaliczek</div> },
    ],
    { initialEntries },
  )

  return { client, router }
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
      pageSize: 10,
      totalCount: 1,
      totalPages: 1,
    })
    vi.mocked(listAggregateInvoices).mockResolvedValue(emptyAggregatePage)

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

    expect(container.querySelector('[data-testid="invoice-table"]')).toBeTruthy()
    expect(container.querySelectorAll('[data-testid="invoice-row"]').length).toBeGreaterThanOrEqual(1)
    expect(container.querySelector('[data-testid="invoice-number"]')?.textContent).toContain('FV/2026/001')
    expect(container.querySelector('[data-testid="invoice-ksef-number"]')?.textContent).toContain('KSEF-001')
    expect(container.querySelector('[data-testid="invoice-view-details"]')).toBeTruthy()

    expect(listInvoices).toHaveBeenCalledWith('tenant-1', expect.objectContaining({ page: 1, pageSize: 10 }))
  })

  it('UIL-001 renders merged aggregate and synced rows with SourceChip', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listInvoices).mockResolvedValue({
      items: [
        {
          id: 'synced-1',
          ksefInvoiceNumber: 'KSEF-001',
          ksefReferenceNumber: 'REF-001',
          invoiceNumber: null,
          vendorName: 'Vendor',
          vendorNip: '1234567890',
          buyerName: null,
          buyerNip: null,
          amountNet: 100,
          amountVat: 23,
          amountGross: 123,
          currency: 'PLN',
          issueDate: '2026-01-01T00:00:00Z',
          acquisitionDate: null,
          invoiceType: null,
          firstSeenAt: '2026-01-01T00:00:00Z',
          isPaid: false,
          paidAt: null,
        },
        {
          id: 'synced-2',
          ksefInvoiceNumber: 'KSEF-002',
          ksefReferenceNumber: 'REF-002',
          invoiceNumber: null,
          vendorName: 'Vendor',
          vendorNip: '1234567890',
          buyerName: null,
          buyerNip: null,
          amountNet: 200,
          amountVat: 46,
          amountGross: 246,
          currency: 'PLN',
          issueDate: '2026-01-02T00:00:00Z',
          acquisitionDate: null,
          invoiceType: null,
          firstSeenAt: '2026-01-02T00:00:00Z',
          isPaid: false,
          paidAt: null,
        },
      ],
      page: 1,
      pageSize: 25,
      totalCount: 2,
      totalPages: 1,
    })
    vi.mocked(listAggregateInvoices).mockResolvedValue({
      items: [
        {
          id: 'agg-1',
          tenantId: 'tenant-1',
          kind: 'VatInvoice',
          status: 'Draft',
          buyerKind: 'Business',
          ksefSubmissionRequirement: 'Required',
          ksefSubmissionState: 'NotPlanned',
          seller: { name: 'Seller', nip: '1234567890' },
          buyer: { name: 'Buyer', nip: '0987654321' },
          issueDate: '2026-01-03',
          saleDate: null,
          dueDate: null,
          approvedAt: null,
          submittedToKsefAt: null,
          acceptedByKsefAt: null,
          currency: 'PLN',
          totalNet: { amount: 500, currency: 'PLN' },
          totalVat: { amount: 115, currency: 'PLN' },
          totalGross: { amount: 615, currency: 'PLN' },
          documentNumber: 'FV/2026/01/001',
          externalReference: null,
          paymentMethod: null,
          publicNotes: null,
          ksefDocumentNumber: null,
          ksefReferenceNumber: null,
          ksefRejectionReason: null,
          correctionReference: null,
          lines: [],
          advanceDocumentIds: [],
          settledAdvanceAllocations: [],
          duplicateIssuances: [],
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
      await waitFor(() => container.querySelectorAll('[data-testid="invoice-row"]').length >= 1)
    })

    const rows = container.querySelectorAll('[data-testid="invoice-row"]')
    expect(rows.length).toBe(3)

    const text = container.textContent ?? ''
    expect(text).toMatch(/Aggregate|Sync|Zsynchronizowana/i)
  })

  it('UIL-004 aggregate row links to /invoices/aggregate/:id', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listInvoices).mockResolvedValue(emptySyncedPage)
    vi.mocked(listAggregateInvoices).mockResolvedValue({
      items: [
        {
          id: 'agg-uuid-123',
          tenantId: 'tenant-1',
          kind: 'VatInvoice',
          status: 'Draft',
          buyerKind: 'Business',
          ksefSubmissionRequirement: 'Required',
          ksefSubmissionState: 'NotPlanned',
          seller: { name: 'Seller', nip: '1234567890' },
          buyer: { name: 'Buyer', nip: null },
          issueDate: '2026-01-01',
          saleDate: null,
          dueDate: null,
          approvedAt: null,
          submittedToKsefAt: null,
          acceptedByKsefAt: null,
          currency: 'PLN',
          totalNet: { amount: 100, currency: 'PLN' },
          totalVat: { amount: 23, currency: 'PLN' },
          totalGross: { amount: 123, currency: 'PLN' },
          documentNumber: 'FV-001',
          externalReference: null,
          paymentMethod: null,
          publicNotes: null,
          ksefDocumentNumber: null,
          ksefReferenceNumber: null,
          ksefRejectionReason: null,
          correctionReference: null,
          lines: [],
          advanceDocumentIds: [],
          settledAdvanceAllocations: [],
          duplicateIssuances: [],
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
      await waitFor(() => container.querySelector('[data-testid="invoice-view-details"]') !== null)
    })

    const link = container.querySelector<HTMLAnchorElement>('[data-testid="invoice-view-details"]')
    expect(link?.href).toContain('/invoices/aggregate/agg-uuid-123')
  })

  it('UIL-005 header "Nowa faktura" navigates to /invoices/new', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listInvoices).mockResolvedValue(emptySyncedPage)
    vi.mocked(listAggregateInvoices).mockResolvedValue(emptyAggregatePage)

    const { router } = renderList()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
    })

    const newBtn = [...container.querySelectorAll('a')].find((a) => a.textContent?.includes('Nowa faktura'))
    expect(newBtn).toBeTruthy()
    expect(newBtn?.getAttribute('href')).toContain('/invoices/new')
  })

  it('applies filters by updating URL and resetting page to 1', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listInvoices).mockResolvedValue(emptySyncedPage)
    vi.mocked(listAggregateInvoices).mockResolvedValue(emptyAggregatePage)

    const { router } = renderList([
      '/invoices?tenantId=tenant-1&dateFrom=2026-01-01&dateTo=2026-01-31&page=3&pageSize=25',
    ])

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
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listInvoices).mockRejectedValue(new Error('Network failed'))
    vi.mocked(listAggregateInvoices).mockResolvedValue(emptyAggregatePage)

    const { router } = renderList(['/invoices?tenantId=tenant-1'])

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
