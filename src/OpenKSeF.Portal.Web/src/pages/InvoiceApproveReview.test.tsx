// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { approveInvoice, getAggregateInvoice } from '@/api/invoicesAggregateApi'
import { InvoiceApproveReviewPage } from './InvoiceApproveReview'

vi.mock('@/api/invoicesAggregateApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api/invoicesAggregateApi')>()
  return {
    ...actual,
    approveInvoice: vi.fn(),
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

const draftInvoice = {
  id: 'inv-approve-001',
  tenantId: 'tenant-1',
  kind: 'VatInvoice' as const,
  status: 'Draft' as const,
  buyerKind: 'Business' as const,
  ksefSubmissionRequirement: 'Required' as const,
  ksefSubmissionState: 'NotPlanned' as const,
  seller: { name: 'Sprzedawca Sp. z o.o.', nip: '1111111111' },
  buyer: { name: 'Nabywca SA', nip: '2222222222' },
  issueDate: '2026-04-10',
  saleDate: null,
  dueDate: '2026-04-20',
  approvedAt: null,
  submittedToKsefAt: null,
  acceptedByKsefAt: null,
  currency: 'PLN',
  totalNet: { amount: 1000, currency: 'PLN' },
  totalVat: { amount: 230, currency: 'PLN' },
  totalGross: { amount: 1230, currency: 'PLN' },
  documentNumber: 'FV/2026/04/001',
  externalReference: null,
  paymentMethod: null,
  publicNotes: null,
  ksefDocumentNumber: null,
  ksefReferenceNumber: null,
  ksefRejectionReason: null,
  correctionReference: null,
  lines: [
    {
      lineNumber: 1,
      description: 'Usługa',
      quantity: 1,
      unitOfMeasure: null,
      pricingMode: 'Net' as const,
      unitPrice: { amount: 1000, currency: 'PLN' },
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

const approvedInvoice = { ...draftInvoice, status: 'Approved' as const, approvedAt: '2026-04-10T10:00:00Z' }

function renderPage(invoiceId = 'inv-approve-001') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [
      {
        path: '/invoices/aggregate/:id/approve',
        element: (
          <QueryClientProvider client={client}>
            <InvoiceApproveReviewPage />
          </QueryClientProvider>
        ),
      },
      { path: '/invoices/aggregate/:id', element: <div data-testid="detail-page">detail</div> },
    ],
    { initialEntries: [`/invoices/aggregate/${invoiceId}/approve?tenantId=tenant-1`] },
  )
  return { client, router }
}

describe('InvoiceApproveReviewPage', () => {
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

  it('UIA-001 approve happy path calls approveInvoice and navigates to detail', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(draftInvoice)
    vi.mocked(approveInvoice).mockResolvedValue(approvedInvoice)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="approve-button"]') !== null)
    })

    const approveButton = container.querySelector<HTMLButtonElement>('[data-testid="approve-button"]')!
    await act(async () => {
      approveButton.click()
      await flushPromises()
      await flushPromises()
    })

    expect(approveInvoice).toHaveBeenCalledWith('tenant-1', 'inv-approve-001')

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="detail-page"]') !== null)
    })
  })

  it('UIA-002 approve blocked by server validation renders ValidationMessageList', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(draftInvoice)
    vi.mocked(approveInvoice).mockRejectedValue(
      new (await import('@/api/invoicesAggregateApi').then((m) => m.InvoiceValidationError))(
        422,
        {
          stage: 'Approve',
          messages: [
            {
              code: 'INV-VAL-063',
              severity: 'Error',
              field: null,
              messagePl: 'Podsumowanie VAT jest niespójne z pozycjami.',
              messageTechnical: 'Vat breakdown mismatch.',
            },
          ],
        },
      ),
    )

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="approve-button"]') !== null)
    })

    const approveButton = container.querySelector<HTMLButtonElement>('[data-testid="approve-button"]')!
    await act(async () => {
      approveButton.click()
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('.validation-message-list') !== null)
    })

    expect(container.textContent).toContain('INV-VAL-063')
    expect(container.textContent).toContain('Podsumowanie VAT jest niespójne z pozycjami.')
    expect(container.querySelector('[data-testid="detail-page"]')).toBeNull()
  })

  it('UIA-005 groups validation errors by family in canonical order', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(draftInvoice)
    vi.mocked(approveInvoice).mockRejectedValue(
      new (await import('@/api/invoicesAggregateApi').then((m) => m.InvoiceValidationError))(
        422,
        {
          stage: 'Approve',
          messages: [
            {
              code: 'INV-VAL-102',
              severity: 'Error',
              field: null,
              messagePl: 'Ta konfiguracja nie pozwala edytować zatwierdzonego dokumentu.',
              messageTechnical: 'Approved->Draft forbidden by policy.',
            },
            {
              code: 'INV-VAL-063',
              severity: 'Error',
              field: null,
              messagePl: 'Podsumowanie VAT jest niespójne z pozycjami.',
              messageTechnical: 'Vat breakdown mismatch.',
            },
            {
              code: 'INV-VAL-013',
              severity: 'Error',
              field: 'Buyer.Nip',
              messagePl: 'Dla nabywcy B2B wymagany jest poprawny NIP.',
              messageTechnical: 'BuyerKind=Business but BuyerSnapshot.Nip missing/invalid.',
            },
          ],
        },
      ),
    )

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="approve-button"]') !== null)
    })

    const approveButton = container.querySelector<HTMLButtonElement>('[data-testid="approve-button"]')!
    await act(async () => {
      approveButton.click()
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelectorAll('.validation-message-list__group').length > 0)
    })

    const groupTitles = [...container.querySelectorAll('.validation-message-list__group-title')].map(
      (el) => el.textContent,
    )
    // Strony (parties=1) < VAT (vat=3) < Stan (state=7) in familyOrder
    expect(groupTitles.indexOf('Strony')).toBeLessThan(groupTitles.indexOf('VAT'))
    expect(groupTitles.indexOf('VAT')).toBeLessThan(groupTitles.indexOf('Stan'))
  })

  it('UIA-006 retry after fix navigates to detail when approve succeeds', async () => {
    vi.mocked(getAggregateInvoice).mockResolvedValue(draftInvoice)

    const { InvoiceValidationError: Err } = await import('@/api/invoicesAggregateApi')
    vi.mocked(approveInvoice)
      .mockRejectedValueOnce(
        new Err(422, {
          stage: 'Approve',
          messages: [
            {
              code: 'INV-VAL-013',
              severity: 'Error',
              field: 'Buyer.Nip',
              messagePl: 'Dla nabywcy B2B wymagany jest poprawny NIP.',
              messageTechnical: 'BuyerKind=Business but BuyerSnapshot.Nip missing/invalid.',
            },
          ],
        }),
      )
      .mockResolvedValueOnce(approvedInvoice)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="approve-button"]') !== null)
    })

    // First click — validation error shown
    const approveButton = container.querySelector<HTMLButtonElement>('[data-testid="approve-button"]')!
    await act(async () => {
      approveButton.click()
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.textContent?.includes('INV-VAL-013') === true)
    })

    // Second click — success
    await act(async () => {
      approveButton.click()
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="detail-page"]') !== null)
    })
  })
})
