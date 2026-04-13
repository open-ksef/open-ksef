// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { listAggregateInvoices } from '@/api/invoicesAggregateApi'
import { listTenants } from '@/api/endpoints/tenants'
import { SalesInvoiceListPage } from './SalesInvoiceList'

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn(),
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

const emptyAggregatePage = {
  items: [],
  page: 1,
  pageSize: 25,
  totalCount: 0,
  totalPages: 0,
}

const aggInvoice = {
  id: 'agg-uuid-1',
  tenantId: 'tenant-1',
  kind: 'VatInvoice' as const,
  status: 'Draft' as const,
  buyerKind: 'Business' as const,
  ksefSubmissionRequirement: 'Required' as const,
  ksefSubmissionState: 'NotPlanned' as const,
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
}

function renderList(initialEntries = ['/invoices/sales?tenantId=tenant-1&page=1&pageSize=25']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [
      {
        path: '/invoices/sales',
        element: (
          <QueryClientProvider client={client}>
            <SalesInvoiceListPage />
          </QueryClientProvider>
        ),
      },
      { path: '/invoices/new', element: <div data-testid="create-draft-page">Nowa faktura</div> },
      { path: '/invoices/final-from-advances', element: <div data-testid="final-advances-page">Finalna z zaliczek</div> },
      { path: '/invoices/aggregate/:id', element: <div data-testid="aggregate-detail">detail</div> },
    ],
    { initialEntries },
  )
  return { client, router }
}

describe('SalesInvoiceListPage', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders sales invoice table with aggregate data only', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listAggregateInvoices).mockResolvedValue({
      items: [aggInvoice],
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

    expect(container.querySelector('[data-testid="invoice-number"]')?.textContent).toContain('FV/2026/01/001')
    expect(container.querySelector('[data-testid="invoice-view-details"]')).toBeTruthy()
    expect(listAggregateInvoices).toHaveBeenCalledWith('tenant-1', expect.objectContaining({ page: 1, pageSize: 25 }))
  })

  it('shows "Faktury sprzedaży" title and action buttons', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listAggregateInvoices).mockResolvedValue(emptyAggregatePage)

    const { router } = renderList()

    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
    })

    expect(container.textContent).toContain('Faktury sprzedaży')
    const newBtn = [...container.querySelectorAll('a')].find((a) => a.textContent?.includes('Nowa faktura'))
    expect(newBtn).toBeTruthy()
  })

  it('renders kind tab bar', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listAggregateInvoices).mockResolvedValue(emptyAggregatePage)

    const { router } = renderList()

    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
    })

    expect(container.querySelector('[data-testid="kind-tab-bar"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="kind-tab-All"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="kind-tab-VatInvoice"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="kind-tab-CorrectionInvoice"]')).toBeTruthy()
  })

  it('kind tab click updates URL kind param', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listAggregateInvoices).mockResolvedValue(emptyAggregatePage)

    const { router } = renderList()

    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
    })

    const vatTab = container.querySelector<HTMLButtonElement>('[data-testid="kind-tab-VatInvoice"]')

    await act(async () => {
      vatTab?.click()
      await flushPromises()
    })

    expect(router.state.location.search).toContain('kind=VatInvoice')
  })

  it('aggregate row links to /invoices/aggregate/:id', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listAggregateInvoices).mockResolvedValue({
      items: [aggInvoice],
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
    expect(link?.href).toContain('/invoices/aggregate/agg-uuid-1')
  })
})
