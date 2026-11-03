// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { getInvoiceByKSeFNumber, getTransferDetails } from '@/api/endpoints/invoices'
import { listTenants } from '@/api/endpoints/tenants'
import { ApiError } from '@/api/errors'
import { InvoiceDetailsPage } from './InvoiceDetails'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn(),
}))

vi.mock('@/api/endpoints/invoices', () => ({
  getInvoiceByKSeFNumber: vi.fn(),
  getTransferDetails: vi.fn(),
  setInvoicePaid: vi.fn(),
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

describe('InvoiceDetailsPage', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.stubGlobal('localStorage', memoryStorage())
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders details card with required fields', async () => {
    vi.mocked(listTenants).mockResolvedValue([
      {
        id: 'tenant-1',
        nip: '1234567890',
        displayName: 'Tenant A',
        notificationEmail: null,
        createdAt: new Date().toISOString(),
      },
    ])

    vi.mocked(getInvoiceByKSeFNumber).mockResolvedValue({
      id: 'inv-1',
      ksefInvoiceNumber: 'KSEF-001',
      ksefReferenceNumber: 'REF-001',
      invoiceNumber: 'FV/2026/001',
      vendorName: 'Vendor A',
      vendorNip: '9876543210',
      buyerName: 'Buyer B',
      buyerNip: '0987654321',
      amountNet: 162.2,
      amountVat: 37.3,
      amountGross: 199.5,
      currency: 'PLN',
      issueDate: '2026-02-01T00:00:00Z',
      acquisitionDate: '2026-02-01T00:05:00Z',
      invoiceType: 'VAT',
      firstSeenAt: '2026-02-01T12:00:00Z',
      isPaid: false,
      paidAt: null,
    })

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter(
      [
        {
          path: '/invoices/:ksefInvoiceNumber',
          element: (
            <QueryClientProvider client={client}>
              <CompanyProvider>
                <InvoiceDetailsPage />
              </CompanyProvider>
            </QueryClientProvider>
          ),
        },
      ],
      { initialEntries: ['/invoices/KSEF-001?tenantId=tenant-1'] },
    )

    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="invoice-details-card"]') !== null)
    })

    expect(container.querySelector('[data-testid="invoice-detail-ksef-number"]')?.textContent).toContain('KSEF-001')
    expect(container.querySelector('[data-testid="invoice-detail-vendor-name"]')?.textContent).toContain('Vendor A')
    expect(container.querySelector('[data-testid="invoice-detail-vendor-nip"]')?.textContent).toContain('9876543210')
    expect(container.querySelector('[data-testid="invoice-detail-issue-date"]')?.textContent).toContain('2026')
    expect(container.querySelector('[data-testid="invoice-detail-amount"]')?.textContent).toContain('199')
    expect(container.querySelector('[data-testid="invoice-detail-amount"]')?.textContent).toContain('PLN')
    expect(container.querySelector('[data-testid="invoice-details-back-link"]')).toBeTruthy()
  })

  describe('formats', () => {
    const plain = (id: string) => container.querySelector(`[data-testid="${id}"]`)?.textContent?.replace(/\s/g, ' ')

    async function renderDetails(entry: string | { pathname: string; search: string; state: unknown }): Promise<void> {
      vi.mocked(listTenants).mockResolvedValue([
        { id: 'tenant-1', nip: '1234567890', displayName: 'Tenant A', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' },
      ])
      vi.mocked(getInvoiceByKSeFNumber).mockResolvedValue({
        id: 'inv-1',
        ksefInvoiceNumber: 'KSEF-001',
        ksefReferenceNumber: 'REF-001',
        invoiceNumber: 'FV/2026/001',
        vendorName: 'Vendor A',
        vendorNip: '9876543210',
        buyerName: null,
        buyerNip: null,
        amountNet: 1200,
        amountVat: 276,
        amountGross: 1476,
        currency: 'PLN',
        issueDate: '2026-02-01T00:00:00Z',
        acquisitionDate: null,
        invoiceType: 'Vat',
        firstSeenAt: '2026-02-01T12:00:00Z',
        isPaid: false,
        paidAt: null,
        lines: [
          {
            lineNumber: 1,
            name: 'Usługa',
            unit: 'szt',
            quantity: 1500,
            unitPriceNet: 1234.5,
            unitPriceGross: 0.123456,
            amountNet: 1200,
            amountVat: 276,
            amountGross: 1476,
            vatRate: '23',
          },
        ],
      })
      vi.mocked(getTransferDetails).mockResolvedValue({
        recipientName: 'Vendor A',
        recipientAccount: 'PL12 3456 7890 1234 5678 9012 3456',
        recipientNip: '9876543210',
        amount: 1476,
        currency: 'PLN',
        title: 'FV/2026/001',
        transferText: 'text',
        qrCodeBase64: null,
        qrMessage: null,
      })

      const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
      const router = createMemoryRouter(
        [
          {
            path: '/invoices/:ksefInvoiceNumber',
            element: (
              <QueryClientProvider client={client}>
                <CompanyProvider>
                  <InvoiceDetailsPage />
                </CompanyProvider>
              </QueryClientProvider>
            ),
          },
        ],
        { initialEntries: [entry] },
      )

      await act(async () => {
        root.render(<RouterProvider router={router} />)
        await flushPromises()
      })
      await act(async () => {
        await waitFor(() => container.querySelector('[data-testid="transfer-account"]') !== null)
      })
    }

    it('groups money, NRB and quantities, and marks the VAT rate', async () => {
      await renderDetails('/invoices/KSEF-001?tenantId=tenant-1')

      expect(plain('invoice-detail-amount')).toBe('1 476,00 PLN')
      expect(plain('invoice-detail-amount-net')).toBe('1 200,00 PLN')
      expect(plain('invoice-detail-amount-vat')).toBe('276,00 PLN')
      expect(plain('transfer-amount')).toBe('1 476,00 PLN')
      expect(plain('transfer-account')).toBe('12 3456 7890 1234 5678 9012 3456')

      const cells = Array.from(container.querySelectorAll('[data-testid="invoice-lines"] tbody td')).map((cell) =>
        cell.textContent?.replace(/\s/g, ' '),
      )
      expect(cells.slice(3)).toEqual(['1 500', '1 234,50', '0,123456', '1 200,00 PLN', '276,00 PLN', '1 476,00 PLN', '23%'])
    })

    it('back link returns to the list search it came from', async () => {
      await renderDetails({
        pathname: '/invoices/KSEF-001',
        search: '?tenantId=tenant-1',
        state: { listSearch: '?tenantId=tenant-1&q=abc&page=2' },
      })

      expect(container.querySelector('[data-testid="invoice-details-back-link"]')?.getAttribute('href')).toBe(
        '/invoices?tenantId=tenant-1&q=abc&page=2',
      )
    })

    it('back link keeps the company on a deep link', async () => {
      await renderDetails('/invoices/KSEF-001?tenantId=tenant-1')

      expect(container.querySelector('[data-testid="invoice-details-back-link"]')?.getAttribute('href')).toBe(
        '/invoices?tenantId=tenant-1',
      )
    })
  })

  it('renders not found state for 404 errors', async () => {
    vi.mocked(listTenants).mockResolvedValue([
      {
        id: 'tenant-1',
        nip: '1234567890',
        displayName: 'Tenant A',
        notificationEmail: null,
        createdAt: new Date().toISOString(),
      },
    ])
    vi.mocked(getInvoiceByKSeFNumber).mockRejectedValue(new ApiError('Not found', 404))

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter(
      [
        {
          path: '/invoices/:ksefInvoiceNumber',
          element: (
            <QueryClientProvider client={client}>
              <CompanyProvider>
                <InvoiceDetailsPage />
              </CompanyProvider>
            </QueryClientProvider>
          ),
        },
      ],
      { initialEntries: ['/invoices/KSEF-404?tenantId=tenant-1'] },
    )

    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => (container.textContent ?? '').includes('Nie znaleziono faktury'))
    })

    expect(container.textContent).toContain('Nie znaleziono żądanej faktury.')
    expect(container.querySelector('[data-testid="invoice-details-back-link"]')).toBeTruthy()
  })

  describe('transfer card', () => {
    const qrImage = 'data:image/png;base64,iVBORw0KGgo='
    const transferText = 'Odbiorca: Vendor A\nKwota: 199,50 PLN'

    function testId(id: string): HTMLElement | null {
      return container.querySelector(`[data-testid="${id}"]`)
    }

    async function renderTransfer(qrCodeBase64: string | null, qrMessage: string | null): Promise<void> {
      vi.mocked(listTenants).mockResolvedValue([
        { id: 'tenant-1', nip: '1234567890', displayName: 'Tenant A', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' },
      ])
      vi.mocked(getInvoiceByKSeFNumber).mockResolvedValue({
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
        invoiceType: 'Vat',
        firstSeenAt: '2026-02-01T12:00:00Z',
        isPaid: false,
        paidAt: null,
      })
      vi.mocked(getTransferDetails).mockResolvedValue({
        recipientName: 'Vendor A',
        recipientAccount: null,
        recipientNip: '9876543210',
        amount: 199.5,
        currency: 'PLN',
        title: 'FV/2026/001',
        transferText,
        qrCodeBase64,
        qrMessage,
      })

      const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
      const router = createMemoryRouter(
        [
          {
            path: '/invoices/:ksefInvoiceNumber',
            element: (
              <QueryClientProvider client={client}>
                <CompanyProvider>
                  <InvoiceDetailsPage />
                </CompanyProvider>
              </QueryClientProvider>
            ),
          },
        ],
        { initialEntries: ['/invoices/KSEF-001?tenantId=tenant-1'] },
      )

      await act(async () => {
        root.render(<RouterProvider router={router} />)
        await flushPromises()
      })

      for (let i = 0; i < 10 && testId('copy-transfer-btn') === null; i++) {
        await act(flushPromises)
      }
    }

    it('shows only the image when there is a QR and no message', async () => {
      await renderTransfer(qrImage, null)

      expect(testId('qr-code-image')?.getAttribute('src')).toBe(qrImage)
      expect(testId('qr-message')).toBeNull()
    })

    it('shows the message and no image when QR is null, and copy still works', async () => {
      const message = 'Brak poprawnego numeru rachunku na fakturze.'
      const writeText = vi.fn().mockResolvedValue(undefined)
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })

      await renderTransfer(null, message)

      expect(testId('qr-code-section')?.querySelector('img')).toBeNull()
      expect(testId('qr-message')?.textContent).toBe(message)

      await act(async () => {
        testId('copy-transfer-btn')?.click()
        await flushPromises()
      })

      expect(writeText).toHaveBeenCalledWith(transferText)
      expect(testId('copy-transfer-btn')?.textContent).toBe('Skopiowano!')

      await act(async () => root.unmount())
    })

    it('shows both the image and the notice', async () => {
      const notice = 'Sprawdź, czy faktura nie wymaga podzielonej płatności (MPP).'

      await renderTransfer(qrImage, notice)

      expect(testId('qr-code-image')?.getAttribute('src')).toBe(qrImage)
      expect(testId('qr-message')?.textContent).toBe(notice)
    })
  })
})
