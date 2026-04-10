// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { InvoiceValidationError, createCorrectionFromOriginal, getAggregateInvoice } from '@/api/invoicesAggregateApi'
import { InvoiceCorrectionCreatePage } from './InvoiceCorrectionCreate'

vi.mock('@/api/invoicesAggregateApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api/invoicesAggregateApi')>()
  return {
    ...actual,
    createCorrectionFromOriginal: vi.fn(),
    getAggregateInvoice: vi.fn(),
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

const acceptedInvoice = {
  id: 'inv-orig-001',
  tenantId: 'tenant-1',
  kind: 'VatInvoice' as const,
  status: 'AcceptedByKsef' as const,
  buyerKind: 'Business' as const,
  ksefSubmissionRequirement: 'Required' as const,
  ksefSubmissionState: 'Accepted' as const,
  seller: { name: 'Sprzedawca Sp. z o.o.', nip: '1111111111' },
  buyer: { name: 'Nabywca SA', nip: '2222222222' },
  issueDate: '2026-03-01',
  saleDate: null,
  dueDate: null,
  approvedAt: '2026-03-01T10:00:00Z',
  submittedToKsefAt: '2026-03-01T11:00:00Z',
  acceptedByKsefAt: '2026-03-01T12:00:00Z',
  currency: 'PLN',
  totalNet: { amount: 500, currency: 'PLN' },
  totalVat: { amount: 115, currency: 'PLN' },
  totalGross: { amount: 615, currency: 'PLN' },
  documentNumber: 'FV/2026/03/001',
  externalReference: null,
  paymentMethod: null,
  publicNotes: null,
  ksefDocumentNumber: 'KSeF-001',
  ksefReferenceNumber: 'REF-001',
  ksefRejectionReason: null,
  correctionReference: null,
  lines: [
    {
      lineNumber: 1,
      description: 'Usługa A',
      quantity: 1,
      unitOfMeasure: null,
      pricingMode: 'Net' as const,
      unitPrice: { amount: 300, currency: 'PLN' },
      discountPercent: null,
      vatRate: '23%',
      netAmount: { amount: 300, currency: 'PLN' },
      vatAmount: { amount: 69, currency: 'PLN' },
      grossAmount: { amount: 369, currency: 'PLN' },
      correctionRole: null,
    },
    {
      lineNumber: 2,
      description: 'Usługa B',
      quantity: 1,
      unitOfMeasure: null,
      pricingMode: 'Net' as const,
      unitPrice: { amount: 200, currency: 'PLN' },
      discountPercent: null,
      vatRate: '23%',
      netAmount: { amount: 200, currency: 'PLN' },
      vatAmount: { amount: 46, currency: 'PLN' },
      grossAmount: { amount: 246, currency: 'PLN' },
      correctionRole: null,
    },
  ],
  advanceDocumentIds: [],
  settledAdvanceAllocations: [],
  duplicateIssuances: [],
}

const correctionDraft = {
  ...acceptedInvoice,
  id: 'inv-corr-draft-001',
  kind: 'CorrectionInvoice' as const,
  status: 'Draft' as const,
  ksefSubmissionState: 'NotPlanned' as const,
  approvedAt: null,
  submittedToKsefAt: null,
  acceptedByKsefAt: null,
  ksefDocumentNumber: null,
  ksefReferenceNumber: null,
  correctionReference: {
    originalInvoiceId: 'inv-orig-001',
    originalDocumentNumber: 'FV/2026/03/001',
    reasonKind: 'ValueChange' as const,
    reasonDescription: 'Price correction',
  },
}

function renderPage(invoiceId = 'inv-orig-001') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [
      {
        path: '/invoices/aggregate/:id/corrections/new',
        element: (
          <QueryClientProvider client={client}>
            <InvoiceCorrectionCreatePage />
          </QueryClientProvider>
        ),
      },
      { path: '/invoices/aggregate/:id', element: <div data-testid="detail-page">detail</div> },
    ],
    { initialEntries: [`/invoices/aggregate/${invoiceId}/corrections/new?tenantId=tenant-1`] },
  )
  return { client, router }
}

describe('InvoiceCorrectionCreatePage', () => {
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

  it('UIX-001 correction screen loads original read-only data', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(acceptedInvoice)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="correction-create-form"]') !== null)
    })

    expect(container.textContent).toContain('FV/2026/03/001')
    expect(container.textContent).toContain('Nabywca SA')
    expect(container.textContent).toContain('500')
  })

  it('UIX-002 server INV-VAL-081 error is rendered', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(acceptedInvoice)
    vi.mocked(createCorrectionFromOriginal).mockRejectedValue(
      new InvoiceValidationError(422, {
        stage: 'Draft',
        messages: [
          {
            code: 'INV-VAL-081',
            severity: 'Error',
            field: 'CorrectionReference.ReasonDescription',
            messagePl: 'Opis powodu korekty jest wymagany.',
            messageTechnical: 'ReasonDescription is required.',
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
      await waitFor(() => container.querySelector('[data-testid="correction-create-form"]') !== null)
    })

    const submitBtn = container.querySelector<HTMLButtonElement>('[data-testid="create-correction-button"]')!
    await act(async () => {
      submitBtn.click()
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('.validation-message-list') !== null)
    })

    expect(container.textContent).toContain('INV-VAL-081')
  })

  it('UIX-003 original id from route is passed to createCorrectionFromOriginal', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(acceptedInvoice)
    vi.mocked(createCorrectionFromOriginal).mockResolvedValue(correctionDraft)

    const { router } = renderPage('inv-orig-001')
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="correction-create-form"]') !== null)
    })

    const submitBtn = container.querySelector<HTMLButtonElement>('[data-testid="create-correction-button"]')!
    await act(async () => {
      submitBtn.click()
      await flushPromises()
      await flushPromises()
    })

    expect(vi.mocked(createCorrectionFromOriginal)).toHaveBeenCalledWith(
      'tenant-1',
      'inv-orig-001',
      expect.any(Object),
    )
  })

  it('UIX-004 before/after line editor shows original lines as correction before', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(acceptedInvoice)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="correction-create-form"]') !== null)
    })

    // Both original lines appear in the before-correction labels
    const correctionBeforeEls = container.querySelectorAll('.invoice-line-editor__correction-before')
    expect(correctionBeforeEls.length).toBe(2)
    // After-correction columns also present
    const correctionAfterEls = container.querySelectorAll('.invoice-line-editor__correction-after')
    expect(correctionAfterEls.length).toBe(2)
  })

  it('UIX-005 server INV-VAL-082 error is rendered', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(acceptedInvoice)
    vi.mocked(createCorrectionFromOriginal).mockRejectedValue(
      new InvoiceValidationError(422, {
        stage: 'Draft',
        messages: [
          {
            code: 'INV-VAL-082',
            severity: 'Error',
            field: null,
            messagePl: 'Korekta nie może być identyczna z oryginałem.',
            messageTechnical: 'Correction lines identical to original.',
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
      await waitFor(() => container.querySelector('[data-testid="correction-create-form"]') !== null)
    })

    const submitBtn = container.querySelector<HTMLButtonElement>('[data-testid="create-correction-button"]')!
    await act(async () => {
      submitBtn.click()
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('.validation-message-list') !== null)
    })

    expect(container.textContent).toContain('INV-VAL-082')
  })

  it('UIX-007 reason kind select exposes all correction reason kinds', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(acceptedInvoice)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="correction-create-form"]') !== null)
    })

    const select = container.querySelector<HTMLSelectElement>('[data-testid="reason-kind-select"]')
    expect(select).not.toBeNull()

    const optionValues = [...(select?.options ?? [])].map((o) => o.value)
    expect(optionValues).toContain('Formal')
    expect(optionValues).toContain('ValueChange')
    expect(optionValues).toContain('QuantityChange')
    expect(optionValues).toContain('VatChange')
    expect(optionValues).toContain('BuyerDataChange')
    expect(optionValues).toContain('Other')
  })

  it('UIX-008 successful correction navigates to new draft detail', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(acceptedInvoice)
    vi.mocked(createCorrectionFromOriginal).mockResolvedValue(correctionDraft)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="correction-create-form"]') !== null)
    })

    const submitBtn = container.querySelector<HTMLButtonElement>('[data-testid="create-correction-button"]')!
    await act(async () => {
      submitBtn.click()
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="detail-page"]') !== null)
    })

    expect(vi.mocked(createCorrectionFromOriginal)).toHaveBeenCalledTimes(1)
  })
})
