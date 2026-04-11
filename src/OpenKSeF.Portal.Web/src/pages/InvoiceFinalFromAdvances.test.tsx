// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { InvoiceValidationError, createFinalInvoiceFromAdvances, listAggregateInvoices } from '@/api/invoicesAggregateApi'
import { listTenants } from '@/api/endpoints/tenants'
import { InvoiceFinalFromAdvancesPage } from './InvoiceFinalFromAdvances'

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn(),
}))

vi.mock('@/api/invoicesAggregateApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api/invoicesAggregateApi')>()
  return {
    ...actual,
    createFinalInvoiceFromAdvances: vi.fn(),
    listAggregateInvoices: vi.fn(),
  }
})

function flushPromises() {
  return new Promise<void>((resolve) => setTimeout(resolve, 0))
}

async function waitFor(assertion: () => boolean, timeoutMs = 2000): Promise<void> {
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
    nip: '1111111111',
    displayName: 'Tenant A',
    notificationEmail: null,
    createdAt: new Date().toISOString(),
  },
]

function makeAdvanceInvoice(overrides: { id: string; buyerNip: string; buyerName: string; amount: number; docNum: string }) {
  return {
    id: overrides.id,
    tenantId: 'tenant-1',
    kind: 'AdvanceInvoice' as const,
    status: 'AcceptedByKsef' as const,
    buyerKind: 'Business' as const,
    ksefSubmissionRequirement: 'Required' as const,
    ksefSubmissionState: 'Accepted' as const,
    seller: { name: 'Sprzedawca Sp. z o.o.', nip: '1111111111' },
    buyer: { name: overrides.buyerName, nip: overrides.buyerNip },
    issueDate: '2026-01-15',
    saleDate: null,
    dueDate: null,
    approvedAt: '2026-01-15T10:00:00Z',
    submittedToKsefAt: '2026-01-15T11:00:00Z',
    acceptedByKsefAt: '2026-01-15T12:00:00Z',
    currency: 'PLN',
    totalNet: { amount: overrides.amount, currency: 'PLN' },
    totalVat: { amount: Math.round(overrides.amount * 0.23), currency: 'PLN' },
    totalGross: { amount: overrides.amount + Math.round(overrides.amount * 0.23), currency: 'PLN' },
    documentNumber: overrides.docNum,
    externalReference: null,
    paymentMethod: null,
    publicNotes: null,
    ksefDocumentNumber: `KSeF-${overrides.id}`,
    ksefReferenceNumber: `REF-${overrides.id}`,
    ksefRejectionReason: null,
    correctionReference: null,
    lines: [],
    advanceDocumentIds: [],
    settledAdvanceAllocations: [],
    duplicateIssuances: [],
  }
}

const buyerAAdvance1 = makeAdvanceInvoice({ id: '11111111-1111-4111-8111-111111111111', buyerNip: '2222222222', buyerName: 'Nabywca A', amount: 300, docNum: 'ZAL/2026/01' })
const buyerAAdvance2 = makeAdvanceInvoice({ id: '22222222-2222-4222-8222-222222222222', buyerNip: '2222222222', buyerName: 'Nabywca A', amount: 500, docNum: 'ZAL/2026/02' })
const buyerBAdvance1 = makeAdvanceInvoice({ id: '33333333-3333-4333-8333-333333333333', buyerNip: '3333333333', buyerName: 'Nabywca B', amount: 200, docNum: 'ZAL/2026/03' })

const finalDraft = {
  ...buyerAAdvance1,
  id: '44444444-4444-4444-8444-444444444444',
  kind: 'FinalInvoice' as const,
  status: 'Draft' as const,
  ksefSubmissionState: 'NotPlanned' as const,
  approvedAt: null,
  submittedToKsefAt: null,
  acceptedByKsefAt: null,
  ksefDocumentNumber: null,
  ksefReferenceNumber: null,
  documentNumber: null,
  settledAdvanceAllocations: [
    { advanceInvoiceId: '11111111-1111-4111-8111-111111111111', advanceDocumentNumber: 'ZAL/2026/01', settledAmount: { amount: 369, currency: 'PLN' } },
    { advanceInvoiceId: '22222222-2222-4222-8222-222222222222', advanceDocumentNumber: 'ZAL/2026/02', settledAmount: { amount: 615, currency: 'PLN' } },
  ],
}

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [
      {
        path: '/invoices/final-from-advances',
        element: (
          <QueryClientProvider client={client}>
            <InvoiceFinalFromAdvancesPage />
          </QueryClientProvider>
        ),
      },
      { path: '/invoices/aggregate/:id', element: <div data-testid="detail-page">detail</div> },
    ],
    { initialEntries: ['/invoices/final-from-advances?tenantId=tenant-1'] },
  )
  return { client, router }
}

describe('InvoiceFinalFromAdvancesPage', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    document.body.removeChild(container)
  })

  it('UIF-001 buyer picker lists only buyers that have advances', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listAggregateInvoices).mockResolvedValue({
      items: [buyerAAdvance1, buyerAAdvance2, buyerBAdvance1],
      page: 1,
      pageSize: 100,
      totalCount: 3,
      totalPages: 1,
    })

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="final-advances-form"]') !== null)
    })

    const buyerSelect = container.querySelector<HTMLSelectElement>('[data-testid="buyer-select"]')
    expect(buyerSelect).not.toBeNull()

    const optionTexts = [...(buyerSelect?.options ?? [])].map((o) => o.text)
    expect(optionTexts.some((t) => t.includes('Nabywca A'))).toBe(true)
    expect(optionTexts.some((t) => t.includes('Nabywca B'))).toBe(true)
  })

  it('UIF-002 advance picker shows running total when advances selected', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listAggregateInvoices).mockResolvedValue({
      items: [buyerAAdvance1, buyerAAdvance2],
      page: 1,
      pageSize: 100,
      totalCount: 2,
      totalPages: 1,
    })

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="final-advances-form"]') !== null)
    })

    // Select buyer A
    const buyerSelect = container.querySelector<HTMLSelectElement>('[data-testid="buyer-select"]')!
    await act(async () => {
      const nativeInputValueSetter = Object.getOwnPropertyDescriptor(window.HTMLSelectElement.prototype, 'value')?.set
      nativeInputValueSetter?.call(buyerSelect, '2222222222')
      buyerSelect.dispatchEvent(new Event('change', { bubbles: true }))
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('.advance-allocation-picker') !== null)
    })

    // Check both advances listed
    expect(container.textContent).toContain('ZAL/2026/01')
    expect(container.textContent).toContain('ZAL/2026/02')
  })

  it('UIF-003 submitting without advances shows zod error, no API call', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listAggregateInvoices).mockResolvedValue({
      items: [buyerAAdvance1],
      page: 1,
      pageSize: 100,
      totalCount: 1,
      totalPages: 1,
    })

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="final-advances-form"]') !== null)
    })

    const submitBtn = container.querySelector<HTMLButtonElement>('[data-testid="create-final-button"]')!
    await act(async () => {
      submitBtn.click()
      await flushPromises()
    })

    expect(vi.mocked(createFinalInvoiceFromAdvances)).not.toHaveBeenCalled()
    expect(container.querySelector('[role="alert"]')).not.toBeNull()
  })

  it('UIF-004 server INV-VAL-072 error is rendered', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listAggregateInvoices).mockResolvedValue({
      items: [buyerAAdvance1],
      page: 1,
      pageSize: 100,
      totalCount: 1,
      totalPages: 1,
    })
    vi.mocked(createFinalInvoiceFromAdvances).mockRejectedValue(
      new InvoiceValidationError(422, {
        stage: 'Draft',
        messages: [
          {
            code: 'INV-VAL-072',
            severity: 'Error',
            field: null,
            messagePl: 'Suma zaliczek przekracza wartość faktury finalnej.',
            messageTechnical: 'Advances total exceeds invoice gross.',
          },
        ],
      }),
    )

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="final-advances-form"]') !== null)
    })

    // Select buyer
    const buyerSelect = container.querySelector<HTMLSelectElement>('[data-testid="buyer-select"]')!
    await act(async () => {
      const nativeSetter = Object.getOwnPropertyDescriptor(window.HTMLSelectElement.prototype, 'value')?.set
      nativeSetter?.call(buyerSelect, '2222222222')
      buyerSelect.dispatchEvent(new Event('change', { bubbles: true }))
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('.advance-allocation-picker') !== null)
    })

    // Set issue date (date inputs fire 'change', not 'input')
    const issueDateInput = container.querySelector<HTMLInputElement>('#final-issue-date')!
    await act(async () => {
      const nativeValueSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')?.set
      nativeValueSetter?.call(issueDateInput, '2026-04-10')
      issueDateInput.dispatchEvent(new Event('change', { bubbles: true }))
      await flushPromises()
    })

    // Check advance checkbox
    const checkbox = container.querySelector<HTMLInputElement>('input[type="checkbox"]')!
    await act(async () => {
      checkbox.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      await flushPromises()
    })
    await act(async () => { await flushPromises() })

    const submitBtn = container.querySelector<HTMLButtonElement>('[data-testid="create-final-button"]')!
    await act(async () => {
      submitBtn.click()
      await flushPromises()
      await flushPromises()
    })


    expect(container.textContent).toContain('INV-VAL-072')
  })

  it('UIF-006 success navigates to final draft detail', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(listAggregateInvoices).mockResolvedValue({
      items: [buyerAAdvance1, buyerAAdvance2],
      page: 1,
      pageSize: 100,
      totalCount: 2,
      totalPages: 1,
    })
    vi.mocked(createFinalInvoiceFromAdvances).mockResolvedValue(finalDraft)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="final-advances-form"]') !== null)
    })

    // Select buyer
    const buyerSelect = container.querySelector<HTMLSelectElement>('[data-testid="buyer-select"]')!
    await act(async () => {
      const nativeSetter = Object.getOwnPropertyDescriptor(window.HTMLSelectElement.prototype, 'value')?.set
      nativeSetter?.call(buyerSelect, '2222222222')
      buyerSelect.dispatchEvent(new Event('change', { bubbles: true }))
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('.advance-allocation-picker') !== null)
    })

    // Set issue date (date inputs fire 'change', not 'input')
    const issueDateInput = container.querySelector<HTMLInputElement>('#final-issue-date')!
    await act(async () => {
      const nativeValueSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')?.set
      nativeValueSetter?.call(issueDateInput, '2026-04-10')
      issueDateInput.dispatchEvent(new Event('change', { bubbles: true }))
      await flushPromises()
    })

    // Select all checkboxes
    const checkboxes = container.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')
    for (const cb of checkboxes) {
      await act(async () => {
        cb.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await flushPromises()
      })
      await act(async () => { await flushPromises() })
    }

    const submitBtn = container.querySelector<HTMLButtonElement>('[data-testid="create-final-button"]')!
    await act(async () => {
      submitBtn.click()
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="detail-page"]') !== null)
    })

    expect(vi.mocked(createFinalInvoiceFromAdvances)).toHaveBeenCalledTimes(1)
  })
})
