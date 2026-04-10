// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const { toastError } = vi.hoisted(() => ({
  toastError: vi.fn(),
}))

vi.mock('react-hot-toast', () => ({
  default: {
    error: toastError,
    success: vi.fn(),
  },
}))

vi.mock('@/api/invoicesAggregateApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api/invoicesAggregateApi')>()
  return {
    ...actual,
    getAggregateInvoice: vi.fn(),
    updateInvoiceDraft: vi.fn(),
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
  id: '11111111-1111-4111-8111-111111111111',
  tenantId: '22222222-2222-4222-8222-222222222222',
  kind: 'VatInvoice' as const,
  status: 'Draft' as const,
  buyerKind: 'Business' as const,
  ksefSubmissionRequirement: 'Required' as const,
  ksefSubmissionState: 'NotPlanned' as const,
  seller: { name: 'Seller Sp. z o.o.', nip: '1111111111' },
  buyer: { name: 'Buyer SA', nip: '5252344078' },
  issueDate: '2026-04-10',
  saleDate: '2026-04-10',
  dueDate: '2026-04-20',
  approvedAt: null,
  submittedToKsefAt: null,
  acceptedByKsefAt: null,
  currency: 'PLN',
  totalNet: { amount: 300, currency: 'PLN' },
  totalVat: { amount: 69, currency: 'PLN' },
  totalGross: { amount: 369, currency: 'PLN' },
  documentNumber: 'FV/2026/04/010',
  externalReference: 'ERP-44',
  paymentMethod: 'Przelew',
  publicNotes: 'Public note',
  internalNotes: 'Internal note',
  ksefDocumentNumber: null,
  ksefReferenceNumber: null,
  ksefRejectionReason: null,
  correctionReference: null,
  lines: [
    {
      lineNumber: 1,
      description: 'Line A',
      quantity: 1,
      unitOfMeasure: 'szt.',
      pricingMode: 'Net' as const,
      unitPrice: { amount: 100, currency: 'PLN' },
      discountPercent: null,
      vatRate: '23%',
      netAmount: { amount: 100, currency: 'PLN' },
      vatAmount: { amount: 23, currency: 'PLN' },
      grossAmount: { amount: 123, currency: 'PLN' },
      correctionRole: null,
    },
    {
      lineNumber: 2,
      description: 'Line B',
      quantity: 1,
      unitOfMeasure: 'szt.',
      pricingMode: 'Net' as const,
      unitPrice: { amount: 100, currency: 'PLN' },
      discountPercent: null,
      vatRate: '23%',
      netAmount: { amount: 100, currency: 'PLN' },
      vatAmount: { amount: 23, currency: 'PLN' },
      grossAmount: { amount: 123, currency: 'PLN' },
      correctionRole: null,
    },
    {
      lineNumber: 3,
      description: 'Line C',
      quantity: 1,
      unitOfMeasure: 'szt.',
      pricingMode: 'Net' as const,
      unitPrice: { amount: 100, currency: 'PLN' },
      discountPercent: null,
      vatRate: '23%',
      netAmount: { amount: 100, currency: 'PLN' },
      vatAmount: { amount: 23, currency: 'PLN' },
      grossAmount: { amount: 123, currency: 'PLN' },
      correctionRole: null,
    },
  ],
  advanceDocumentIds: [],
  settledAdvanceAllocations: [],
  duplicateIssuances: [],
}

async function renderPage(status: 'Draft' | 'Approved' = 'Draft') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const { InvoiceDraftEditPage } = await import('./InvoiceDraftEdit')
  const router = createMemoryRouter(
    [
      {
        path: '/invoices/aggregate/:id/edit',
        element: (
          <QueryClientProvider client={client}>
            <InvoiceDraftEditPage />
          </QueryClientProvider>
        ),
      },
      {
        path: '/invoices/aggregate/:id',
        element: <div data-testid="detail-page">detail</div>,
      },
    ],
    {
      initialEntries: [`/invoices/aggregate/${draftInvoice.id}/edit?tenantId=${draftInvoice.tenantId}&status=${status}`],
    },
  )

  return { client, router }
}

describe('InvoiceDraftEditPage', () => {
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
    act(() => {
      root.unmount()
    })
    document.body.removeChild(container)
  })

  it('UIE-001 prefills fields from aggregate invoice response', async () => {
    const { getAggregateInvoice } = await import('@/api/invoicesAggregateApi')
    vi.mocked(getAggregateInvoice).mockResolvedValue(draftInvoice)

    const { router } = await renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="edit-public-notes"]') !== null)
    })

    expect(container.querySelector<HTMLInputElement>('[data-testid="edit-document-number"]')?.value).toBe('FV/2026/04/010')
    expect(container.querySelector<HTMLInputElement>('[data-testid="edit-payment-method"]')?.value).toBe('Przelew')
    expect(container.querySelector<HTMLTextAreaElement>('[data-testid="edit-public-notes"]')?.value).toBe('Public note')
    expect(container.querySelector<HTMLTextAreaElement>('[data-testid="edit-internal-notes"]')?.value).toBe('Internal note')
    expect(container.textContent).toContain('Seller Sp. z o.o.')
    expect(container.textContent).toContain('Buyer SA')
    expect(container.querySelector<HTMLInputElement>('#line-after-0-description')?.value).toBe('Line A')
    expect(container.querySelector<HTMLInputElement>('#line-after-2-description')?.value).toBe('Line C')
  })

  it('UIE-002 redirects non-draft invoice to detail and shows toast', async () => {
    const { getAggregateInvoice } = await import('@/api/invoicesAggregateApi')
    vi.mocked(getAggregateInvoice).mockResolvedValue({ ...draftInvoice, status: 'Approved' })

    const { router } = await renderPage('Approved')
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => toastError.mock.calls.length > 0)
    })

    expect(toastError).toHaveBeenCalledWith('Faktura nie jest w stanie roboczym.')
    expect(router.state.location.pathname).toBe(`/invoices/aggregate/${draftInvoice.id}`)
  })

  it('UIE-003 sends only changed fields in patch payload', async () => {
    const { getAggregateInvoice, updateInvoiceDraft } = await import('@/api/invoicesAggregateApi')
    vi.mocked(getAggregateInvoice).mockResolvedValue(draftInvoice)
    vi.mocked(updateInvoiceDraft).mockResolvedValue({ ...draftInvoice, publicNotes: 'Changed public note' })

    const { router } = await renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="edit-public-notes"]') !== null)
    })

    const publicNotes = container.querySelector<HTMLTextAreaElement>('[data-testid="edit-public-notes"]')!
    await act(async () => {
      publicNotes.value = 'Changed public note'
      publicNotes.dispatchEvent(new Event('input', { bubbles: true }))
    })

    const submit = container.querySelector<HTMLButtonElement>('[data-testid="edit-submit-button"]')!
    await act(async () => {
      submit.click()
      await flushPromises()
      await flushPromises()
    })

    expect(updateInvoiceDraft).toHaveBeenCalledOnce()
    expect(vi.mocked(updateInvoiceDraft).mock.calls[0][0]).toBe(draftInvoice.tenantId)
    expect(vi.mocked(updateInvoiceDraft).mock.calls[0][1]).toBe(draftInvoice.id)
    expect(vi.mocked(updateInvoiceDraft).mock.calls[0][2]).toEqual({
      publicNotes: 'Changed public note',
    })
  })

  it('UIE-004 shows state transition modal when server rejects concurrent edit', async () => {
    const { InvoiceValidationError } = await import('@/api/invoicesAggregateApi')
    const { getAggregateInvoice, updateInvoiceDraft } = await import('@/api/invoicesAggregateApi')
    vi.mocked(getAggregateInvoice).mockResolvedValue(draftInvoice)
    vi.mocked(updateInvoiceDraft).mockRejectedValue(
      new InvoiceValidationError(409, {
        stage: 'Draft',
        messages: [
          {
            code: 'INV-VAL-101',
            severity: 'Error',
            field: null,
            messagePl: 'Faktura została w międzyczasie zatwierdzona',
            messageTechnical: 'Invoice became immutable.',
          },
        ],
      }),
    )

    const { router } = await renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="edit-public-notes"]') !== null)
    })

    const publicNotes = container.querySelector<HTMLTextAreaElement>('[data-testid="edit-public-notes"]')!
    await act(async () => {
      publicNotes.value = 'Changed public note'
      publicNotes.dispatchEvent(new Event('input', { bubbles: true }))
    })

    const submit = container.querySelector<HTMLButtonElement>('[data-testid="edit-submit-button"]')!
    await act(async () => {
      submit.click()
      await flushPromises()
      await flushPromises()
    })

    expect(container.querySelector('[role="alertdialog"]')?.textContent).toContain('Faktura została w międzyczasie zatwierdzona')
    expect(container.textContent).toContain('INV-VAL-101')
  })

  it('UIE-005 cancel navigates back to detail without API call', async () => {
    const { getAggregateInvoice, updateInvoiceDraft } = await import('@/api/invoicesAggregateApi')
    vi.mocked(getAggregateInvoice).mockResolvedValue(draftInvoice)

    const { router } = await renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="edit-cancel-button"]') !== null)
    })

    const cancel = container.querySelector<HTMLAnchorElement>('[data-testid="edit-cancel-button"]')!
    await act(async () => {
      cancel.click()
      await flushPromises()
    })

    expect(updateInvoiceDraft).not.toHaveBeenCalled()
    expect(container.querySelector('[data-testid="detail-page"]')).not.toBeNull()
  })

  it('UIE-006 reorders lines and persists line numbers in the payload', async () => {
    const { getAggregateInvoice, updateInvoiceDraft } = await import('@/api/invoicesAggregateApi')
    vi.mocked(getAggregateInvoice).mockResolvedValue(draftInvoice)
    vi.mocked(updateInvoiceDraft).mockResolvedValue(draftInvoice)

    const { router } = await renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="edit-public-notes"]') !== null)
    })

    const moveLineCUp = container.querySelector<HTMLButtonElement>('[data-testid="line-move-up-2"]')!
    await act(async () => {
      moveLineCUp.click()
      await flushPromises()
    })

    const moveLineCAgain = container.querySelector<HTMLButtonElement>('[data-testid="line-move-up-1"]')!
    await act(async () => {
      moveLineCAgain.click()
      await flushPromises()
    })

    const submit = container.querySelector<HTMLButtonElement>('[data-testid="edit-submit-button"]')!
    await act(async () => {
      submit.click()
      await flushPromises()
      await flushPromises()
    })

    expect(vi.mocked(updateInvoiceDraft).mock.calls[0][2]).toEqual({
      lines: [
        expect.objectContaining({ lineNumber: 1, description: 'Line C' }),
        expect.objectContaining({ lineNumber: 2, description: 'Line A' }),
        expect.objectContaining({ lineNumber: 3, description: 'Line B' }),
      ],
    })
  })
})
