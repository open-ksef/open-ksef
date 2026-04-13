// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { InvoiceValidationError, getAggregateInvoice, submitInvoiceToKsef } from '@/api/invoicesAggregateApi'
import { type InvoiceReadDto } from '@/api/schemas/invoice'
import { InvoiceKsefSubmitPage } from './InvoiceKsefSubmit'

vi.mock('@/api/invoicesAggregateApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api/invoicesAggregateApi')>()
  return {
    ...actual,
    getAggregateInvoice: vi.fn(),
    submitInvoiceToKsef: vi.fn(),
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

function makeInvoice(overrides: Partial<InvoiceReadDto> = {}): InvoiceReadDto {
  return { ...baseInvoice(), ...overrides }
}

function baseInvoice(): InvoiceReadDto {
  return {
    id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    tenantId: 'tenant-1',
    kind: 'VatInvoice' as const,
    status: 'Approved' as const,
    buyerKind: 'Business' as const,
    ksefSubmissionRequirement: 'Required' as const,
    ksefSubmissionState: 'Ready' as const,
    seller: { name: 'Sprzedawca Sp. z o.o.', nip: '1111111111' },
    buyer: { name: 'Nabywca SA', nip: '2222222222' },
    issueDate: '2026-04-01',
    saleDate: null,
    dueDate: null,
    approvedAt: '2026-04-01T10:00:00Z',
    submittedToKsefAt: null,
    acceptedByKsefAt: null,
    currency: 'PLN',
    totalNet: { amount: 500, currency: 'PLN' },
    totalVat: { amount: 115, currency: 'PLN' },
    totalGross: { amount: 615, currency: 'PLN' },
    documentNumber: 'FV/2026/04/001',
    externalReference: null,
    paymentMethod: null,
    publicNotes: null,
    ksefDocumentNumber: null,
    ksefReferenceNumber: null,
    ksefRejectionReason: null,
    reopenAllowed: false,
    correctionReference: null,
    lines: [],
    advanceDocumentIds: [],
    settledAdvanceAllocations: [],
    duplicateIssuances: [],
  }
}

function renderPage(invoiceId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [
      {
        path: '/invoices/aggregate/:id/submit',
        element: (
          <QueryClientProvider client={client}>
            <InvoiceKsefSubmitPage />
          </QueryClientProvider>
        ),
      },
      { path: '/invoices/aggregate/:id/print', element: <div data-testid="print-page">print</div> },
      { path: '/invoices/aggregate/:id', element: <div data-testid="detail-page">detail</div> },
    ],
    { initialEntries: [`/invoices/aggregate/${invoiceId}/submit?tenantId=tenant-1`] },
  )
  return { client, router }
}

describe('InvoiceKsefSubmitPage', () => {
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

  it('UIK-001 submit button calls submitInvoiceToKsef and shows polling state', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(makeInvoice())
    vi.mocked(submitInvoiceToKsef).mockResolvedValue(
      makeInvoice({ status: 'SubmittedToKsef', ksefSubmissionState: 'Submitted' }),
    )

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="ksef-submit-view"]') !== null)
    })

    const submitBtn = container.querySelector<HTMLButtonElement>('[data-testid="submit-to-ksef-button"]')!
    await act(async () => {
      submitBtn.click()
      await flushPromises()
      await flushPromises()
    })

    expect(vi.mocked(submitInvoiceToKsef)).toHaveBeenCalledWith('tenant-1', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa')
    expect(container.querySelector('[data-testid="ksef-polling"]')).not.toBeNull()
  })

  it('UIK-002 accepted state renders KSeF identifiers and Drukuj button', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(
      makeInvoice({
        status: 'AcceptedByKsef',
        ksefSubmissionState: 'Accepted',
        acceptedByKsefAt: '2026-04-01T15:00:00Z',
        ksefDocumentNumber: 'KSeF-12345',
        ksefReferenceNumber: 'REF-67890',
      }),
    )

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="ksef-accepted"]') !== null)
    })

    expect(container.textContent).toContain('KSeF-12345')
    expect(container.querySelector('[data-testid="print-button"]')).not.toBeNull()
  })

  it('UIK-003 rejected state renders rejection reason and Utwórz korektę button', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(
      makeInvoice({
        status: 'RejectedByKsef' as const,
        ksefSubmissionState: 'Rejected',
        ksefRejectionReason: 'Błędny NIP nabywcy.',
      }),
    )

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="ksef-rejected"]') !== null)
    })

    expect(container.textContent).toContain('Błędny NIP nabywcy.')
    expect(container.querySelector('[data-testid="create-correction-button"]')).not.toBeNull()
  })

  it('UIK-004 INV-VAL-092 envelope is shown when credentials missing', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(makeInvoice())
    vi.mocked(submitInvoiceToKsef).mockRejectedValue(
      new InvoiceValidationError(422, {
        stage: 'SendToKsef',
        messages: [
          {
            code: 'INV-VAL-092',
            severity: 'Error',
            field: null,
            messagePl: 'Brak skonfigurowanych danych dostępowych KSeF.',
            messageTechnical: 'No KSeF credentials configured.',
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
      await waitFor(() => container.querySelector('[data-testid="ksef-submit-view"]') !== null)
    })

    const submitBtn = container.querySelector<HTMLButtonElement>('[data-testid="submit-to-ksef-button"]')!
    await act(async () => {
      submitBtn.click()
      await flushPromises()
      await flushPromises()
    })

    expect(container.querySelector('.validation-message-list')).not.toBeNull()
    expect(container.textContent).toContain('INV-VAL-092')
  })

  it('UIK-005 INV-VAL-111 validation failure shows rule code', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(makeInvoice())
    vi.mocked(submitInvoiceToKsef).mockRejectedValue(
      new InvoiceValidationError(422, {
        stage: 'SendToKsef',
        messages: [
          {
            code: 'INV-VAL-111',
            severity: 'Error',
            field: null,
            messagePl: 'Dokument nie przeszedł walidacji schematu KSeF.',
            messageTechnical: 'Schema validation failed.',
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
      await waitFor(() => container.querySelector('[data-testid="ksef-submit-view"]') !== null)
    })

    const submitBtn = container.querySelector<HTMLButtonElement>('[data-testid="submit-to-ksef-button"]')!
    await act(async () => {
      submitBtn.click()
      await flushPromises()
      await flushPromises()
    })

    expect(container.querySelector('.validation-message-list')).not.toBeNull()
    expect(container.textContent).toContain('INV-VAL-111')
  })

  it('UIK-006 INV-VAL-093 blocks re-sending an accepted document', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(makeInvoice())
    vi.mocked(submitInvoiceToKsef).mockRejectedValue(
      new InvoiceValidationError(422, {
        stage: 'SendToKsef',
        messages: [
          {
            code: 'INV-VAL-093',
            severity: 'Error',
            field: null,
            messagePl: 'Faktura została już zaakceptowana przez KSeF.',
            messageTechnical: 'Invoice already accepted by KSeF.',
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
      await waitFor(() => container.querySelector('[data-testid="ksef-submit-view"]') !== null)
    })

    const submitBtn = container.querySelector<HTMLButtonElement>('[data-testid="submit-to-ksef-button"]')!
    await act(async () => {
      submitBtn.click()
      await flushPromises()
      await flushPromises()
    })

    expect(container.querySelector('.validation-message-list')).not.toBeNull()
    expect(container.textContent).toContain('INV-VAL-093')
  })
})
