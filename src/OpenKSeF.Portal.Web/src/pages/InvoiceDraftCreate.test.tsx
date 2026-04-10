// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { createInvoiceDraft } from '@/api/invoicesAggregateApi'
import { listTenants } from '@/api/endpoints/tenants'
import { InvoiceDraftCreatePage } from './InvoiceDraftCreate'

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn(),
}))

vi.mock('@/api/invoicesAggregateApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api/invoicesAggregateApi')>()
  return {
    ...actual,
    createInvoiceDraft: vi.fn(),
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
    displayName: 'Sprzedawca Sp. z o.o.',
    notificationEmail: null,
    createdAt: new Date().toISOString(),
  },
]

const createdInvoice = {
  id: 'new-inv-001',
  tenantId: 'tenant-1',
  kind: 'VatInvoice' as const,
  status: 'Draft' as const,
  buyerKind: 'Business' as const,
  ksefSubmissionRequirement: 'Required' as const,
  ksefSubmissionState: 'NotPlanned' as const,
  seller: { name: 'Sprzedawca Sp. z o.o.', nip: '1111111111' },
  buyer: { name: 'Kupujący SA', nip: '5252344078' },
  issueDate: '2026-04-10',
  saleDate: null,
  dueDate: null,
  approvedAt: null,
  submittedToKsefAt: null,
  acceptedByKsefAt: null,
  currency: 'PLN',
  totalNet: { amount: 100, currency: 'PLN' },
  totalVat: { amount: 23, currency: 'PLN' },
  totalGross: { amount: 123, currency: 'PLN' },
  documentNumber: null,
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

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [
      {
        path: '/invoices/new',
        element: (
          <QueryClientProvider client={client}>
            <InvoiceDraftCreatePage />
          </QueryClientProvider>
        ),
      },
      {
        path: '/invoices/aggregate/:id',
        element: <div data-testid="detail-page">detail</div>,
      },
    ],
    { initialEntries: ['/invoices/new?tenantId=tenant-1'] },
  )
  return { client, router }
}

describe('InvoiceDraftCreatePage', () => {
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

  it('UIC-001 valid submission calls createInvoiceDraft and navigates to detail', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(createInvoiceDraft).mockResolvedValue(createdInvoice)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="buyer-name"]') !== null)
    })

    const buyerNameInput = container.querySelector<HTMLInputElement>('[data-testid="buyer-name"]')!
    await act(async () => {
      buyerNameInput.value = 'Kupujący SA'
      buyerNameInput.dispatchEvent(new Event('input', { bubbles: true }))
    })

    const buyerNipInput = container.querySelector<HTMLInputElement>('[data-testid="buyer-nip"]')!
    await act(async () => {
      buyerNipInput.value = '5252344078'
      buyerNipInput.dispatchEvent(new Event('input', { bubbles: true }))
    })

    const addLineButton = container.querySelector<HTMLButtonElement>('.invoice-line-editor__add')!
    await act(async () => {
      addLineButton.click()
    })

    const submitButton = container.querySelector<HTMLButtonElement>('[data-testid="submit-button"]')!
    await act(async () => {
      submitButton.click()
      await flushPromises()
      await flushPromises()
    })

    expect(createInvoiceDraft).toHaveBeenCalledOnce()
    expect(vi.mocked(createInvoiceDraft).mock.calls[0][0]).toBe('tenant-1')

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="detail-page"]') !== null)
    })
  })

  it('UIC-002 missing seller NIP blocks submission and shows error', async () => {
    vi.mocked(listTenants).mockResolvedValue([{ ...defaultTenant[0], nip: '' }])
    vi.mocked(createInvoiceDraft).mockResolvedValue(createdInvoice)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="submit-button"]') !== null)
    })

    const addLineButton = container.querySelector<HTMLButtonElement>('.invoice-line-editor__add')
    if (addLineButton) {
      await act(async () => { addLineButton.click() })
    }

    const submitButton = container.querySelector<HTMLButtonElement>('[data-testid="submit-button"]')!
    await act(async () => {
      submitButton.click()
      await flushPromises()
    })

    expect(createInvoiceDraft).not.toHaveBeenCalled()
    expect(container.textContent).toMatch(/NIP/i)
  })

  it('UIC-003 invalid NIP checksum blocks submission and shows error', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(createInvoiceDraft).mockResolvedValue(createdInvoice)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="buyer-nip"]') !== null)
    })

    const buyerNipInput = container.querySelector<HTMLInputElement>('[data-testid="buyer-nip"]')!
    await act(async () => {
      buyerNipInput.value = '1234567890'
      buyerNipInput.dispatchEvent(new Event('input', { bubbles: true }))
    })

    const buyerNameInput = container.querySelector<HTMLInputElement>('[data-testid="buyer-name"]')!
    await act(async () => {
      buyerNameInput.value = 'Kupujący SA'
      buyerNameInput.dispatchEvent(new Event('input', { bubbles: true }))
    })

    const addLineButton = container.querySelector<HTMLButtonElement>('.invoice-line-editor__add')!
    await act(async () => { addLineButton.click() })

    const submitButton = container.querySelector<HTMLButtonElement>('[data-testid="submit-button"]')!
    await act(async () => {
      submitButton.click()
      await flushPromises()
    })

    expect(createInvoiceDraft).not.toHaveBeenCalled()
    expect(container.textContent).toMatch(/NIP/i)
  })

  it('UIC-004 buyer kind change updates KSeF requirement banner', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="buyer-kind-select"]') !== null)
    })

    // Fill buyer NIP so Business + NIP = Required
    const buyerNipInput = container.querySelector<HTMLInputElement>('[data-testid="buyer-nip"]')!
    await act(async () => {
      buyerNipInput.value = '5252344078'
      buyerNipInput.dispatchEvent(new Event('input', { bubbles: true }))
    })

    expect(container.textContent).toContain('wymagana')

    // Change to Consumer → Optional
    const buyerKindSelect = container.querySelector<HTMLSelectElement>('[data-testid="buyer-kind-select"]')!
    await act(async () => {
      buyerKindSelect.value = 'Consumer'
      buyerKindSelect.dispatchEvent(new Event('change', { bubbles: true }))
    })

    expect(container.textContent).toContain('opcjonalna')
  })

  it('UIC-005 adding a line renders TotalsSummaryCard with preview', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('.invoice-line-editor__add') !== null)
    })

    const addButton = container.querySelector<HTMLButtonElement>('.invoice-line-editor__add')!
    await act(async () => { addButton.click() })

    // Line editor should now show a row
    const lineRows = container.querySelectorAll('.invoice-line-editor__row')
    expect(lineRows.length).toBeGreaterThan(0)

    // TotalsSummaryCard is rendered
    expect(container.querySelector('.totals-summary-card')).not.toBeNull()
    expect(container.textContent).toContain('Netto')
    expect(container.textContent).toContain('Brutto')
  })

  it('UIC-006 server validation error shows ValidationMessageList', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    const { InvoiceValidationError } = await import('@/api/invoicesAggregateApi')
    vi.mocked(createInvoiceDraft).mockRejectedValue(
      new InvoiceValidationError(422, {
        stage: 'Draft',
        messages: [
          {
            code: 'INV-VAL-060',
            severity: 'Error',
            field: null,
            messagePl: 'Nieprawidłowa stawka VAT',
            messageTechnical: 'Invalid VAT rate',
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
      await waitFor(() => container.querySelector('[data-testid="buyer-name"]') !== null)
    })

    const buyerNameInput = container.querySelector<HTMLInputElement>('[data-testid="buyer-name"]')!
    await act(async () => {
      buyerNameInput.value = 'Kupujący SA'
      buyerNameInput.dispatchEvent(new Event('input', { bubbles: true }))
    })

    const buyerNipInput = container.querySelector<HTMLInputElement>('[data-testid="buyer-nip"]')!
    await act(async () => {
      buyerNipInput.value = '5252344078'
      buyerNipInput.dispatchEvent(new Event('input', { bubbles: true }))
    })

    const addButton = container.querySelector<HTMLButtonElement>('.invoice-line-editor__add')!
    await act(async () => { addButton.click() })

    const submitButton = container.querySelector<HTMLButtonElement>('[data-testid="submit-button"]')!
    await act(async () => {
      submitButton.click()
      await flushPromises()
      await flushPromises()
    })

    expect(container.textContent).toContain('INV-VAL-060')
    expect(container.textContent).toContain('Nieprawidłowa stawka VAT')
  })

  it('UIC-007 successful creation navigates to detail page', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(createInvoiceDraft).mockResolvedValue(createdInvoice)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="buyer-name"]') !== null)
    })

    const buyerNameInput = container.querySelector<HTMLInputElement>('[data-testid="buyer-name"]')!
    await act(async () => {
      buyerNameInput.value = 'Kupujący SA'
      buyerNameInput.dispatchEvent(new Event('input', { bubbles: true }))
    })

    const buyerNipInput = container.querySelector<HTMLInputElement>('[data-testid="buyer-nip"]')!
    await act(async () => {
      buyerNipInput.value = '5252344078'
      buyerNipInput.dispatchEvent(new Event('input', { bubbles: true }))
    })

    const addButton = container.querySelector<HTMLButtonElement>('.invoice-line-editor__add')!
    await act(async () => { addButton.click() })

    const submitButton = container.querySelector<HTMLButtonElement>('[data-testid="submit-button"]')!
    await act(async () => {
      submitButton.click()
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="detail-page"]') !== null)
    })

    expect(container.querySelector('[data-testid="detail-page"]')).not.toBeNull()
  })

  it('UIC-008 proforma kind shows NotApplicable KSeF banner', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="kind-select"]') !== null)
    })

    const kindSelect = container.querySelector<HTMLSelectElement>('[data-testid="kind-select"]')!
    await act(async () => {
      kindSelect.value = 'Proforma'
      kindSelect.dispatchEvent(new Event('change', { bubbles: true }))
    })

    expect(container.textContent).toContain('nie ma zastosowania')
  })

  it('UIC-009 zero lines blocks submission with inline error', async () => {
    vi.mocked(listTenants).mockResolvedValue(defaultTenant)
    vi.mocked(createInvoiceDraft).mockResolvedValue(createdInvoice)

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="buyer-name"]') !== null)
    })

    const buyerNameInput = container.querySelector<HTMLInputElement>('[data-testid="buyer-name"]')!
    await act(async () => {
      buyerNameInput.value = 'Kupujący SA'
      buyerNameInput.dispatchEvent(new Event('input', { bubbles: true }))
    })

    const buyerNipInput = container.querySelector<HTMLInputElement>('[data-testid="buyer-nip"]')!
    await act(async () => {
      buyerNipInput.value = '5252344078'
      buyerNipInput.dispatchEvent(new Event('input', { bubbles: true }))
    })

    // Submit with no lines
    const submitButton = container.querySelector<HTMLButtonElement>('[data-testid="submit-button"]')!
    await act(async () => {
      submitButton.click()
      await flushPromises()
    })

    expect(createInvoiceDraft).not.toHaveBeenCalled()
    expect(container.textContent).toContain('Dodaj przynajmniej jedną pozycję')
  })
})
