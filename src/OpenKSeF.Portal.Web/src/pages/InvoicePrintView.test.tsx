// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { getInvoicePrint } from '@/api/invoicesAggregateApi'
import type { InvoicePrintModel } from '@/api/schemas/invoice'
import { InvoicePrintViewPage } from './InvoicePrintView'

vi.mock('@/api/invoicesAggregateApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api/invoicesAggregateApi')>()
  return {
    ...actual,
    getInvoicePrint: vi.fn(),
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

const polishLabels = {
  invoiceTitle: 'Faktura VAT',
  sellerLabel: 'Sprzedawca',
  buyerLabel: 'Nabywca',
  issueDateLabel: 'Data wystawienia',
  saleDateLabel: 'Data sprzedaży',
  dueDateLabel: 'Termin płatności',
  documentNumberLabel: 'Nr dokumentu',
  currencyLabel: 'Waluta',
  totalNetLabel: 'Wartość netto',
  totalVatLabel: 'VAT',
  totalGrossLabel: 'Kwota brutto',
  lineDescriptionLabel: 'Opis',
  lineQuantityLabel: 'Ilość',
  lineUnitPriceLabel: 'Cena jedn.',
  lineNetAmountLabel: 'Netto',
  lineVatRateLabel: 'VAT %',
  lineVatAmountLabel: 'VAT kwota',
  lineGrossAmountLabel: 'Brutto',
  duplicateLabel: 'DUPLIKAT',
}

const englishLabels = {
  invoiceTitle: 'VAT Invoice',
  sellerLabel: 'Seller',
  buyerLabel: 'Buyer',
  issueDateLabel: 'Issue Date',
  saleDateLabel: 'Sale Date',
  dueDateLabel: 'Due Date',
  documentNumberLabel: 'Document Number',
  currencyLabel: 'Currency',
  totalNetLabel: 'Net Total',
  totalVatLabel: 'VAT',
  totalGrossLabel: 'Gross Total',
  lineDescriptionLabel: 'Description',
  lineQuantityLabel: 'Qty',
  lineUnitPriceLabel: 'Unit Price',
  lineNetAmountLabel: 'Net',
  lineVatRateLabel: 'VAT%',
  lineVatAmountLabel: 'VAT Amount',
  lineGrossAmountLabel: 'Gross',
  duplicateLabel: 'DUPLICATE',
}

function makePrintModel(overrides: Partial<InvoicePrintModel> = {}): InvoicePrintModel {
  return {
    variant: 'Standard',
    labels: polishLabels,
    duplicateInfo: null,
    invoiceData: {
      id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      tenantId: 'tenant-1',
      kind: 'VatInvoice',
      status: 'AcceptedByKsef',
      buyerKind: 'Business',
      ksefSubmissionRequirement: 'Required',
      ksefSubmissionState: 'Accepted',
      seller: { name: 'Sprzedawca Sp. z o.o.', nip: '1111111111' },
      buyer: { name: 'Nabywca SA', nip: '2222222222' },
      issueDate: '2026-04-01',
      saleDate: null,
      dueDate: null,
      approvedAt: '2026-04-01T10:00:00Z',
      submittedToKsefAt: '2026-04-01T11:00:00Z',
      acceptedByKsefAt: '2026-04-01T12:00:00Z',
      currency: 'PLN',
      totalNet: { amount: 500, currency: 'PLN' },
      totalVat: { amount: 115, currency: 'PLN' },
      totalGross: { amount: 615, currency: 'PLN' },
      documentNumber: 'FV/2026/04/001',
      externalReference: null,
      paymentMethod: null,
      publicNotes: null,
      ksefDocumentNumber: 'KSeF-12345',
      ksefReferenceNumber: 'REF-67890',
      ksefRejectionReason: null,
      reopenAllowed: false,
      correctionReference: null,
      lines: [
        {
          lineNumber: 1,
          description: 'Usługa konsultingowa',
          quantity: 2,
          unitOfMeasure: 'godz.',
          pricingMode: 'Net',
          unitPrice: { amount: 250, currency: 'PLN' },
          discountPercent: null,
          vatRate: '23%',
          netAmount: { amount: 500, currency: 'PLN' },
          vatAmount: { amount: 115, currency: 'PLN' },
          grossAmount: { amount: 615, currency: 'PLN' },
          correctionRole: null,
        },
      ],
      advanceDocumentIds: [],
      settledAdvanceAllocations: [],
      duplicateIssuances: [],
    },
    ...overrides,
  }
}

function renderPage(invoiceId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [
      {
        path: '/invoices/aggregate/:id/print',
        element: (
          <QueryClientProvider client={client}>
            <InvoicePrintViewPage />
          </QueryClientProvider>
        ),
      },
    ],
    { initialEntries: [`/invoices/aggregate/${invoiceId}/print?tenantId=tenant-1`] },
  )
  return { client, router }
}

describe('InvoicePrintViewPage', () => {
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

  it('UIP-001 standard variant is loaded by default', async () => {
    vi.mocked(getInvoicePrint).mockResolvedValue(makePrintModel())

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="print-view"]') !== null)
    })

    expect(vi.mocked(getInvoicePrint)).toHaveBeenCalledWith(
      'tenant-1',
      'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      'Standard',
    )
  })

  it('UIP-002 switching to English calls getInvoicePrint with English variant', async () => {
    vi.mocked(getInvoicePrint)
      .mockResolvedValueOnce(makePrintModel())
      .mockResolvedValueOnce(makePrintModel({ variant: 'English', labels: englishLabels }))

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="print-view"]') !== null)
    })

    // Click English variant radio
    const englishRadio = [...container.querySelectorAll<HTMLInputElement>('input[type="radio"]')].find(
      (r) => r.value === 'English',
    )!
    await act(async () => {
      englishRadio.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => vi.mocked(getInvoicePrint).mock.calls.length >= 2)
    })

    expect(vi.mocked(getInvoicePrint)).toHaveBeenCalledWith(
      'tenant-1',
      'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      'English',
    )
  })

  it('UIP-003 labels rendered in the view come from backend PrintLabels', async () => {
    vi.mocked(getInvoicePrint).mockResolvedValue(makePrintModel({ variant: 'English', labels: englishLabels }))

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="print-view"]') !== null)
    })

    // Labels rendered in the view come from the backend's labels object
    expect(container.textContent).toContain('VAT Invoice')
    expect(container.textContent).toContain('Seller')
    expect(container.textContent).toContain('Buyer')
  })

  it('UIP-004 duplicate variant is disabled for draft invoice', async () => {
    vi.mocked(getInvoicePrint).mockResolvedValue(
      makePrintModel({
        invoiceData: {
          ...makePrintModel().invoiceData,
          status: 'Draft',
          ksefSubmissionState: 'NotPlanned',
          approvedAt: null,
          submittedToKsefAt: null,
          acceptedByKsefAt: null,
          ksefDocumentNumber: null,
          ksefReferenceNumber: null,
        },
      }),
    )

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="print-view"]') !== null)
    })

    const duplicateRadio = [...container.querySelectorAll<HTMLInputElement>('input[type="radio"]')].find(
      (r) => r.value === 'Duplicate',
    )
    expect(duplicateRadio?.disabled).toBe(true)
  })

  it('UIP-007 print layout root has print-only CSS class', async () => {
    vi.mocked(getInvoicePrint).mockResolvedValue(makePrintModel())

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="print-view"]') !== null)
    })

    expect(container.querySelector('.invoice-print-view')).not.toBeNull()
  })

  it('UIP-008 print view renders document number and totals from invoice data', async () => {
    vi.mocked(getInvoicePrint).mockResolvedValue(makePrintModel())

    const { router } = renderPage()
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="print-view"]') !== null)
    })

    expect(container.textContent).toContain('FV/2026/04/001')
    expect(container.textContent).toContain('500')
    expect(container.textContent).toContain('615')
    expect(container.textContent).toContain('Sprzedawca Sp. z o.o.')
    expect(container.textContent).toContain('Nabywca SA')
  })
})
