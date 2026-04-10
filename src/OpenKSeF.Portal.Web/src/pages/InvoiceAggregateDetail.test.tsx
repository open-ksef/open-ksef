// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { InvoiceValidationError, getAggregateInvoice, reopenInvoice } from '@/api/invoicesAggregateApi'
import { listTenants } from '@/api/endpoints/tenants'
import { InvoiceAggregateDetailPage } from './InvoiceAggregateDetail'

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn(),
}))

vi.mock('@/api/invoicesAggregateApi', () => ({
  getAggregateInvoice: vi.fn(),
  reopenInvoice: vi.fn(),
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

const approvedInvoice = {
  id: 'inv-uuid-001',
  tenantId: 'tenant-1',
  kind: 'VatInvoice' as const,
  status: 'Approved' as const,
  buyerKind: 'Business' as const,
  ksefSubmissionRequirement: 'Required' as const,
  ksefSubmissionState: 'NotPlanned' as const,
  seller: { name: 'Seller Sp. z o.o.', nip: '1111111111' },
  buyer: { name: 'Buyer SA', nip: '2222222222' },
  issueDate: '2026-03-01',
  saleDate: null,
  dueDate: '2026-03-15',
  approvedAt: '2026-03-01T12:00:00Z',
  submittedToKsefAt: null,
  acceptedByKsefAt: null,
  currency: 'PLN',
  totalNet: { amount: 1000, currency: 'PLN' },
  totalVat: { amount: 230, currency: 'PLN' },
  totalGross: { amount: 1230, currency: 'PLN' },
  documentNumber: 'FV/2026/03/001',
  externalReference: null,
  paymentMethod: 'Przelew',
  publicNotes: null,
  ksefDocumentNumber: null,
  ksefReferenceNumber: null,
  ksefRejectionReason: null,
  correctionReference: null,
  lines: [
    {
      lineNumber: 1,
      description: 'Usługa',
      quantity: 2,
      unitOfMeasure: 'godz.',
      pricingMode: 'Net' as const,
      unitPrice: { amount: 500, currency: 'PLN' },
      discountPercent: null,
      vatRate: '23%',
      netAmount: { amount: 1000, currency: 'PLN' },
      vatAmount: { amount: 230, currency: 'PLN' },
      grossAmount: { amount: 1230, currency: 'PLN' },
      correctionRole: null,
    },
  ],
  advanceDocumentIds: [],
  settledAdvanceAllocations: [],
  duplicateIssuances: [],
}

function renderPage(invoiceId = 'inv-uuid-001') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [
      {
        path: '/invoices/aggregate/:id',
        element: (
          <QueryClientProvider client={client}>
            <InvoiceAggregateDetailPage />
          </QueryClientProvider>
        ),
      },
      { path: '/invoices/aggregate/:id/edit', element: <div data-testid="edit-page">edit</div> },
      { path: '/invoices/aggregate/:id/approve', element: <div data-testid="approve-page">approve</div> },
      { path: '/invoices/aggregate/:id/submit', element: <div data-testid="submit-page">submit</div> },
      { path: '/invoices/aggregate/:id/corrections/new', element: <div data-testid="correction-page">correction</div> },
    ],
    { initialEntries: [`/invoices/aggregate/${invoiceId}?tenantId=tenant-1`] },
  )
  return { client, router }
}

describe('InvoiceAggregateDetailPage', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('UID-001 renders all sections for an approved invoice', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(getAggregateInvoice).mockResolvedValue(approvedInvoice)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="aggregate-invoice-detail"]') !== null)
    })

    expect(container.textContent).toContain('FV/2026/03/001')
    expect(container.textContent).toContain('Seller Sp. z o.o.')
    expect(container.textContent).toContain('Buyer SA')
    expect(container.textContent).toContain('Usługa')
    expect(container.textContent).toContain('1')
    expect(container.textContent).toContain('230')
  })

  it('UID-002 draft status shows Edit and Approve buttons, not Submit', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(getAggregateInvoice).mockResolvedValue({ ...approvedInvoice, status: 'Draft', approvedAt: null })

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="aggregate-invoice-detail"]') !== null)
    })

    expect(container.textContent).toContain('Edytuj')
    expect(container.textContent).toContain('Zatwierdź')
    expect(container.textContent).not.toContain('Wyślij do KSeF')
  })

  it('UID-003 approved status shows Submit and Reopen buttons, not Edit', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(getAggregateInvoice).mockResolvedValue(approvedInvoice)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="aggregate-invoice-detail"]') !== null)
    })

    expect(container.textContent).toContain('Wyślij do KSeF')
    expect(container.textContent).toContain('Odblokuj do edycji')
    expect(container.textContent).not.toContain('Edytuj')
  })

  it('UIX-006 proforma accepted status hides Utwórz korekte button', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(getAggregateInvoice).mockResolvedValue({
      ...approvedInvoice,
      kind: 'Proforma',
      status: 'AcceptedByKsef',
      ksefSubmissionState: 'Accepted',
      ksefDocumentNumber: 'KSeF-001',
      ksefReferenceNumber: 'REF-001',
    })

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="aggregate-invoice-detail"]') !== null)
    })

    expect(container.textContent).not.toContain('Utwórz korektę')
    expect(container.textContent).toContain('Drukuj')
  })

  it('UID-006 accepted status shows print button and KSeF identifiers', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(getAggregateInvoice).mockResolvedValue({
      ...approvedInvoice,
      status: 'AcceptedByKsef',
      ksefSubmissionState: 'Accepted',
      ksefDocumentNumber: 'KSeF-DOC-001',
      ksefReferenceNumber: 'KSeF-REF-001',
      acceptedByKsefAt: '2026-03-02T10:00:00Z',
    })

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="aggregate-invoice-detail"]') !== null)
    })

    expect(container.textContent).toContain('KSeF-DOC-001')
    expect(container.textContent).toContain('Drukuj')
  })

  it('UID-007 rejected status shows rejection reason and re-approve button', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(getAggregateInvoice).mockResolvedValue({
      ...approvedInvoice,
      status: 'RejectedByKsef',
      ksefSubmissionState: 'Rejected',
      ksefRejectionReason: 'Nieprawidłowy format dokumentu',
    })

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="aggregate-invoice-detail"]') !== null)
    })

    expect(container.textContent).toContain('Nieprawidłowy format dokumentu')
    expect(container.textContent).toContain('Zatwierdź ponownie')
  })

  it('UID-008 correction invoice shows CorrectionReferenceCard', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(getAggregateInvoice).mockResolvedValue({
      ...approvedInvoice,
      kind: 'CorrectionInvoice',
      correctionReference: {
        originalInvoiceId: 'orig-uuid-001',
        originalDocumentNumber: 'FV/2026/01/001',
        reasonKind: 'ValueChange',
        reasonDescription: null,
      },
    })

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="aggregate-invoice-detail"]') !== null)
    })

    expect(container.textContent).toContain('FV/2026/01/001')
  })

  it('UID-009 final invoice shows advance allocations', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(getAggregateInvoice).mockResolvedValue({
      ...approvedInvoice,
      kind: 'FinalInvoice',
      settledAdvanceAllocations: [
        {
          advanceInvoiceId: 'adv-uuid-001',
          advanceDocumentNumber: 'ZAL/2026/01/001',
          settledAmount: { amount: 500, currency: 'PLN' },
        },
        {
          advanceInvoiceId: 'adv-uuid-002',
          advanceDocumentNumber: 'ZAL/2026/02/001',
          settledAmount: { amount: 300, currency: 'PLN' },
        },
      ],
    })

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="aggregate-invoice-detail"]') !== null)
    })

    expect(container.textContent).toContain('ZAL/2026/01/001')
    expect(container.textContent).toContain('ZAL/2026/02/001')
  })

  it('UIA-003 reopen happy path: button enabled when reopenAllowed, calls reopenInvoice on click', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(getAggregateInvoice).mockResolvedValue({ ...approvedInvoice, reopenAllowed: true })
    vi.mocked(reopenInvoice).mockResolvedValue({ ...approvedInvoice, status: 'Draft', approvedAt: null, reopenAllowed: false })

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="aggregate-invoice-detail"]') !== null)
    })

    const reopenBtn = container.querySelector<HTMLButtonElement>('[data-testid="reopen-button"]')
    expect(reopenBtn).not.toBeNull()
    expect(reopenBtn?.disabled).toBe(false)

    await act(async () => {
      reopenBtn?.click()
      await flushPromises()
    })

    expect(vi.mocked(reopenInvoice)).toHaveBeenCalledWith('tenant-1', 'inv-uuid-001')
  })

  it('UIA-004 reopen blocked: button disabled when reopenAllowed is false', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(getAggregateInvoice).mockResolvedValue({ ...approvedInvoice, reopenAllowed: false })

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="aggregate-invoice-detail"]') !== null)
    })

    const reopenBtn = container.querySelector<HTMLButtonElement>('[data-testid="reopen-button"]')
    expect(reopenBtn?.disabled).toBe(true)
    expect(reopenBtn?.title).toContain('INV-VAL-102')
  })

  it('UID-010 duplicate banner rendered when duplicates exist', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(getAggregateInvoice).mockResolvedValue({
      ...approvedInvoice,
      status: 'AcceptedByKsef',
      ksefSubmissionState: 'Accepted',
      ksefDocumentNumber: 'KSeF-001',
      ksefReferenceNumber: 'REF-001',
      duplicateIssuances: [
        { issuedAt: '2026-03-10T10:00:00Z', issuedBy: 'user@example.com' },
        { issuedAt: '2026-03-12T14:00:00Z', issuedBy: null },
      ],
    })

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="aggregate-invoice-detail"]') !== null)
    })

    expect(container.textContent).toContain('user@example.com')
    expect(container.textContent).toMatch(/duplikat/i)
  })
})
