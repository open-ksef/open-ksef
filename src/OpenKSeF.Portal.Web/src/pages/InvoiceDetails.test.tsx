// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import toast from 'react-hot-toast'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { getInvoiceByKSeFNumber, getTransferDetails, setInvoicePaid } from '@/api/endpoints/invoices'
import { listTenants } from '@/api/endpoints/tenants'
import { ApiError } from '@/api/errors'
import type { InvoiceResponse, PagedResult, TransferDetailsResponse } from '@/api/types'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { InvoiceDetailsPage } from './InvoiceDetails'

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn(),
}))

vi.mock('@/api/endpoints/invoices', () => ({
  getInvoiceByKSeFNumber: vi.fn(),
  getTransferDetails: vi.fn(),
  setInvoicePaid: vi.fn(),
}))

vi.mock('react-hot-toast', () => ({ default: { error: vi.fn(), success: vi.fn() } }))

const QR_IMAGE = 'data:image/png;base64,iVBORw0KGgo='
const OTHER_QR_IMAGE = 'data:image/png;base64,OTHERQR='
const PAID_NOTE = 'Oznaczona w OpenKSeF. To nie jest potwierdzenie przelewu.'

function flushPromises() {
  return new Promise<void>((resolve) => setTimeout(resolve, 0))
}

function invoiceOf(overrides: Partial<InvoiceResponse> = {}): InvoiceResponse {
  return {
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
    ...overrides,
  }
}

function transferOf(overrides: Partial<TransferDetailsResponse> = {}): TransferDetailsResponse {
  return {
    recipientName: 'Vendor A',
    recipientAccount: '12345678901234567890123456',
    recipientNip: '9876543210',
    amount: 1476,
    currency: 'PLN',
    title: 'FV/2026/001',
    transferText: 'Odbiorca: Vendor A\nKwota: 1,476.00 PLN',
    qrCodeBase64: QR_IMAGE,
    qrMessage: null,
    ...overrides,
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

describe('InvoiceDetailsPage', () => {
  let root: Root
  let container: HTMLDivElement

  const testId = (id: string) => container.querySelector<HTMLElement>(`[data-testid="${id}"]`)
  const plain = (id: string) => testId(id)?.textContent?.replace(/\s/g, ' ')

  async function settle(): Promise<void> {
    for (let tick = 0; tick < 6; tick++) {
      await act(flushPromises)
    }
  }

  async function click(element: HTMLElement | null): Promise<void> {
    await act(async () => {
      element?.click()
      await flushPromises()
    })
    await settle()
  }

  async function render(
    entry: string | { pathname: string; search: string; state: unknown },
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
  ) {
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
      { initialEntries: ['/invoices', entry], initialIndex: 1 },
    )

    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
    })
    await settle()
    return { router, client }
  }

  function mockOne(invoice: InvoiceResponse = invoiceOf(), transfer: TransferDetailsResponse = transferOf()): void {
    vi.mocked(getInvoiceByKSeFNumber).mockResolvedValue(invoice)
    vi.mocked(getTransferDetails).mockResolvedValue(transfer)
  }

  function mockMany(entries: Array<{ invoice: InvoiceResponse; transfer?: TransferDetailsResponse }>): void {
    vi.mocked(getInvoiceByKSeFNumber).mockImplementation(async (_tenant, number) => {
      const found = entries.find((entry) => entry.invoice.ksefInvoiceNumber === number)
      if (!found) throw new ApiError('Not found', 404)
      return found.invoice
    })
    vi.mocked(getTransferDetails).mockImplementation(async (_tenant, id) => {
      return entries.find((entry) => entry.invoice.id === id)?.transfer ?? transferOf()
    })
  }

  beforeEach(() => {
    vi.stubGlobal('localStorage', memoryStorage())
    vi.resetAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    vi.mocked(listTenants).mockResolvedValue([
      { id: 'tenant-1', nip: '1234567890', displayName: 'Tenant A', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' },
    ])
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(async () => {
    await act(async () => root.unmount())
    container.remove()
  })

  it('renders details card with required fields', async () => {
    mockOne(
      invoiceOf({
        buyerName: 'Buyer B',
        buyerNip: '0987654321',
        amountGross: 199.5,
        acquisitionDate: '2026-02-03T00:05:00Z',
      }),
    )

    await render('/invoices/KSEF-001?tenantId=tenant-1')

    expect(testId('invoice-detail-ksef-number')?.textContent).toBe('KSEF-001')
    expect(testId('invoice-detail-invoice-number')?.textContent).toBe('FV/2026/001')
    expect(testId('invoice-detail-vendor-name')?.textContent).toBe('Vendor A')
    expect(testId('invoice-detail-vendor-nip')?.textContent).toBe('9876543210')
    expect(testId('invoice-detail-buyer-name')?.textContent).toBe('Buyer B')
    expect(testId('invoice-detail-buyer-nip')?.textContent).toBe('0987654321')
    expect(testId('invoice-detail-issue-date')?.textContent).toBe('01.02.2026')
    expect(testId('invoice-detail-acquisition-date')?.textContent).toBe('03.02.2026')
    expect(plain('invoice-detail-amount')).toBe('199,50 PLN')
    expect(testId('invoice-detail-title')?.textContent).toBe('FV/2026/001')
    expect(testId('invoice-details-back-link')?.textContent).toBe('Otrzymane')
  })

  it('has no payment due row until the API sends a due date', async () => {
    mockOne()

    await render('/invoices/KSEF-001?tenantId=tenant-1')

    expect(container.textContent).not.toContain('Termin płatności')
  })

  it('renders not found state for 404 errors', async () => {
    vi.mocked(getInvoiceByKSeFNumber).mockRejectedValue(new ApiError('Not found', 404))

    await render('/invoices/KSEF-404?tenantId=tenant-1')

    expect(container.textContent).toContain('Nie znaleziono żądanej faktury.')
    expect(testId('invoice-details-back-link')).toBeTruthy()
  })

  describe('formats', () => {
    const line = {
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
    }

    it('groups money, NRB and quantities, and marks the VAT rate', async () => {
      mockOne(invoiceOf({ lines: [line] }), transferOf({ recipientAccount: 'PL12 3456 7890 1234 5678 9012 3456' }))

      await render('/invoices/KSEF-001?tenantId=tenant-1')

      expect(plain('invoice-detail-amount')).toBe('1 476,00 PLN')
      expect(plain('invoice-detail-amount-net')).toBe('1 200,00 PLN')
      expect(plain('invoice-detail-amount-vat')).toBe('276,00 PLN')
      expect(plain('transfer-amount')).toBe('1 476,00 PLN')
      expect(plain('transfer-account')).toBe('12 3456 7890 1234 5678 9012 3456')

      const headers = Array.from(container.querySelectorAll('[data-testid="invoice-lines"] thead th')).map((cell) => cell.textContent)
      expect(headers).toEqual(['Ilość', 'Cena netto', 'Stawka', 'Brutto'])
      expect(testId('invoice-lines')?.querySelector('tbody th')?.textContent).toBe('Usługa')
      const cells = Array.from(container.querySelectorAll('[data-testid="invoice-lines"] tbody td')).map((cell) =>
        cell.textContent?.replace(/\s/g, ' '),
      )
      expect(cells).toEqual(['1 500 szt', '1 234,50', '23%', '1 476,00 PLN'])
      expect(testId('invoice-lines')?.querySelector('.invoice-lines-table-wrapper')).toBeNull()
    })

    it('keeps a dash for line values the invoice does not have', async () => {
      mockOne(
        invoiceOf({
          lines: [{ ...line, name: null, unit: null, quantity: null, unitPriceNet: null, vatRate: null, amountGross: null }],
        }),
      )

      await render('/invoices/KSEF-001?tenantId=tenant-1')

      expect(testId('invoice-lines')?.querySelector('tbody th')?.textContent).toBe('—')
      const cells = Array.from(container.querySelectorAll('[data-testid="invoice-lines"] tbody td')).map((cell) => cell.textContent)
      expect(cells).toEqual(['—', '—', '—', '—'])
    })

    it.each([
      ['empty', []],
      ['null', null],
      ['missing', undefined],
    ])('keeps the lines card with %s lines', async (_name, lines) => {
      mockOne(invoiceOf({ lines }))

      await render('/invoices/KSEF-001?tenantId=tenant-1')

      expect(testId('invoice-lines')?.querySelector('h2')?.textContent).toBe('Pozycje')
      expect(testId('invoice-lines')?.querySelector('table')).toBeNull()
      expect(testId('invoice-lines-empty')?.textContent).toBe('Brak pozycji w pobranych danych.')
    })

    it('back link returns to the list search it came from', async () => {
      mockOne()

      await render({
        pathname: '/invoices/KSEF-001',
        search: '?tenantId=tenant-1',
        state: { listSearch: '?tenantId=tenant-1&q=abc&page=2' },
      })

      expect(testId('invoice-details-back-link')?.getAttribute('href')).toBe('/invoices?tenantId=tenant-1&q=abc&page=2')
    })

    it('back link keeps the company on a deep link', async () => {
      mockOne()

      await render('/invoices/KSEF-001?tenantId=tenant-1')

      expect(testId('invoice-details-back-link')?.getAttribute('href')).toBe('/invoices?tenantId=tenant-1')
    })
  })

  describe('payment card rules', () => {
    const NO_ACCOUNT = 'Brak poprawnego numeru rachunku na fakturze.'
    const CURRENCY = 'Faktura w walucie EUR: kod QR obsługuje tylko przelew w PLN.'
    const CORRECTION = 'Faktura korygująca: kwota to różnica. Sprawdź, czy jest coś do zapłaty.'
    const UNKNOWN_TYPE = 'Nie można ustalić rodzaju faktury. Sprawdź kwotę na fakturze.'
    const MANUAL = 'Zaliczka/rozliczenie: wpisz kwotę ręcznie.'
    const MPP = 'Sprawdź, czy faktura nie wymaga podzielonej płatności (MPP).'

    it.each<[string, string | null, Partial<TransferDetailsResponse>, string, string]>([
      ['Vat with QR', 'Vat', {}, 'Zapłać tę fakturę', 'Do zapłaty'],
      ['Vat above 15 000', 'Vat', { qrMessage: MPP, amount: 20000 }, 'Zapłać tę fakturę', 'Do zapłaty'],
      ['Zal', 'Zal', { qrMessage: MANUAL }, 'Zapłać tę fakturę', 'Kwota faktury'],
      ['zal in lower case', 'zal', { qrMessage: MANUAL }, 'Zapłać tę fakturę', 'Kwota faktury'],
      ['Roz', 'Roz', { qrMessage: MANUAL }, 'Zapłać tę fakturę', 'Kwota faktury'],
      ['ROZ in upper case', 'ROZ', { qrMessage: MANUAL }, 'Zapłać tę fakturę', 'Kwota faktury'],
      ['Zal with no account', 'Zal', { qrCodeBase64: null, qrMessage: NO_ACCOUNT, recipientAccount: null }, 'Dane do przelewu', 'Kwota faktury'],
      ['no account', 'Vat', { qrCodeBase64: null, qrMessage: NO_ACCOUNT, recipientAccount: null }, 'Dane do przelewu', 'Do zapłaty'],
      ['other currency', 'Vat', { qrCodeBase64: null, qrMessage: CURRENCY, currency: 'EUR' }, 'Dane do przelewu', 'Do zapłaty'],
      ['Kor', 'Kor', { qrCodeBase64: null, qrMessage: CORRECTION }, 'Dane do przelewu', 'Kwota brutto (różnica)'],
      ['KorZal', 'KorZal', { qrCodeBase64: null, qrMessage: CORRECTION }, 'Dane do przelewu', 'Kwota brutto (różnica)'],
      ['KOR_ROZ in upper case', 'KOR_ROZ', { qrCodeBase64: null, qrMessage: CORRECTION }, 'Dane do przelewu', 'Kwota brutto (różnica)'],
      ['null type', null, { qrCodeBase64: null, qrMessage: UNKNOWN_TYPE }, 'Dane do przelewu', 'Kwota brutto'],
      ['blank type', '  ', { qrCodeBase64: null, qrMessage: UNKNOWN_TYPE }, 'Dane do przelewu', 'Kwota brutto'],
    ])('%s: title, label and server message', async (_name, invoiceType, transfer, title, label) => {
      const data = transferOf(transfer)
      mockOne(invoiceOf({ invoiceType }), data)

      await render('/invoices/KSEF-001?tenantId=tenant-1')

      expect(testId('pay-title')?.textContent).toBe(title)
      expect(testId('pay-amount-label')?.textContent).toBe(label)
      expect(testId('qr-code-image') !== null).toBe(data.qrCodeBase64 !== null)
      expect(testId('qr-message')?.textContent ?? null).toBe(data.qrMessage)
      expect(testId('toggle-paid-btn')?.textContent).toBe('Oznacz jako opłaconą')
      expect(testId('copy-transfer-btn')).not.toBeNull()
    })

    it('shows the amount in the transfer currency, not the invoice amount', async () => {
      mockOne(invoiceOf({ amountGross: 1, currency: 'PLN' }), transferOf({ amount: 250.5, currency: 'EUR', qrCodeBase64: null, qrMessage: CURRENCY }))

      await render('/invoices/KSEF-001?tenantId=tenant-1')

      expect(plain('transfer-amount')).toBe('250,50 EUR')
    })

    it.each([null, '', ' '])('links a type of %j with no QR to fetching', async (invoiceType) => {
      mockOne(invoiceOf({ invoiceType }), transferOf({ qrCodeBase64: null, qrMessage: UNKNOWN_TYPE }))

      await render('/invoices/KSEF-001?tenantId=tenant-1')

      expect(testId('pay-fetch-link')?.textContent).toBe('Przejdź do pobierania')
      expect(testId('pay-fetch-link')?.getAttribute('href')).toBe('/credentials')
      expect(testId('qr-message')?.textContent).toBe(UNKNOWN_TYPE)
    })

    it.each<[string, string | null, string | null]>([
      ['known type with no QR', 'Vat', null],
      ['null type with a QR', null, QR_IMAGE],
    ])('has no fetch link for %s', async (_name, invoiceType, qrCodeBase64) => {
      mockOne(invoiceOf({ invoiceType }), transferOf({ qrCodeBase64, qrMessage: NO_ACCOUNT }))

      await render('/invoices/KSEF-001?tenantId=tenant-1')

      expect(testId('pay-fetch-link')).toBeNull()
    })

    it('shows brak for no account, and a raw invalid account next to the server message', async () => {
      mockOne(invoiceOf(), transferOf({ recipientAccount: null, qrCodeBase64: null, qrMessage: NO_ACCOUNT }))
      await render('/invoices/KSEF-001?tenantId=tenant-1')
      expect(testId('transfer-account')?.textContent).toBe('brak')
      await act(async () => root.unmount())

      root = createRoot(container)
      mockOne(invoiceOf(), transferOf({ recipientAccount: 'DE12ABC', qrCodeBase64: null, qrMessage: NO_ACCOUNT }))
      await render('/invoices/KSEF-001?tenantId=tenant-1')
      expect(testId('transfer-account')?.textContent).toBe('DE12ABC')
      expect(testId('qr-message')?.textContent).toBe(NO_ACCOUNT)
    })

    it('renders the joined Zal and MPP text as one notice, unsplit', async () => {
      const joined = `${MANUAL} ${MPP}`
      mockOne(invoiceOf({ invoiceType: 'Zal' }), transferOf({ qrMessage: joined }))

      await render('/invoices/KSEF-001?tenantId=tenant-1')

      const notices = container.querySelectorAll('[role="status"]')
      expect(notices).toHaveLength(1)
      expect(notices[0].textContent).toBe(joined)
    })

    it('renders amount, account, title, QR image and copy action from the response', async () => {
      mockOne()

      await render('/invoices/KSEF-001?tenantId=tenant-1')

      expect(plain('transfer-amount')).toBe('1 476,00 PLN')
      expect(plain('transfer-account')).toBe('12 3456 7890 1234 5678 9012 3456')
      expect(testId('transfer-recipient')?.textContent).toBe('Vendor A')
      expect(testId('transfer-title')?.textContent).toBe('FV/2026/001')
      expect(testId('qr-code-image')?.getAttribute('src')).toBe(QR_IMAGE)
      expect(testId('qr-code-image')?.getAttribute('alt')).toBe('Kod QR do przelewu')
      expect(testId('copy-transfer-btn')?.textContent).toBe('Kopiuj dane przelewu')
      expect(testId('qr-message')).toBeNull()
    })

    it('copies the server text unchanged', async () => {
      const transferText = 'Odbiorca: Vendor A\nKwota: 1,476.00 PLN\nTytul: FV/2026/001'
      const writeText = vi.fn().mockResolvedValue(undefined)
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
      mockOne(invoiceOf(), transferOf({ transferText, qrCodeBase64: null, qrMessage: NO_ACCOUNT }))

      await render('/invoices/KSEF-001?tenantId=tenant-1')
      await click(testId('copy-transfer-btn'))

      expect(writeText).toHaveBeenCalledExactlyOnceWith(transferText)
      expect(testId('copy-transfer-btn')?.textContent).toBe('Skopiowano!')
    })

    it('keeps mark paid and offers no copy when the transfer request fails', async () => {
      vi.mocked(getInvoiceByKSeFNumber).mockResolvedValue(invoiceOf())
      vi.mocked(getTransferDetails).mockRejectedValue(new Error('boom'))

      await render('/invoices/KSEF-001?tenantId=tenant-1')

      expect(testId('toggle-paid-btn')?.textContent).toBe('Oznacz jako opłaconą')
      expect(testId('copy-transfer-btn')).toBeNull()
      expect(testId('pay-title')?.textContent).toBe('Dane do przelewu')
      expect(testId('transfer-details')?.querySelector('[role="alert"]')?.textContent).toBe('Nie udało się załadować danych przelewu.')
    })

    it('keeps mark paid and offers no copy while the transfer loads', async () => {
      vi.mocked(getInvoiceByKSeFNumber).mockResolvedValue(invoiceOf())
      vi.mocked(getTransferDetails).mockReturnValue(new Promise(() => undefined))

      await render('/invoices/KSEF-001?tenantId=tenant-1')

      expect(testId('toggle-paid-btn')).not.toBeNull()
      expect(testId('copy-transfer-btn')).toBeNull()
      expect(testId('pay-title')?.textContent).toBe('Dane do przelewu')
    })

    it('collapses transfer data, notice included, for a paid invoice', async () => {
      mockOne(
        invoiceOf({ isPaid: true, paidAt: '2026-03-04T10:00:00Z', invoiceType: 'Kor' }),
        transferOf({ qrMessage: MPP }),
      )

      await render('/invoices/KSEF-001?tenantId=tenant-1')

      expect(testId('pay-title')?.textContent).toBe('Opłacona')
      expect(testId('pay-amount-label')?.textContent).toBe('Kwota faktury')
      expect(plain('transfer-amount')).toBe('1 476,00 PLN')
      expect(testId('transfer-details')?.textContent).toContain(PAID_NOTE)
      expect(testId('invoice-detail-paid-date')?.textContent?.trim()).toBe('04.03.2026')
      expect(testId('toggle-paid-btn')?.textContent).toBe('Cofnij oznaczenie')
      expect(testId('invoice-detail-paid-status')?.textContent).toBe('Opłacona')

      const collapsed = testId('pay-collapsed') as HTMLDetailsElement
      expect(collapsed.tagName).toBe('DETAILS')
      expect(collapsed.open).toBe(false)
      expect(collapsed.querySelector('summary')?.textContent).toBe('Pokaż dane do przelewu')
      expect(collapsed.querySelector('[data-testid="qr-code-image"]')).not.toBeNull()
      expect(collapsed.querySelector('[data-testid="qr-message"]')?.textContent).toBe(MPP)
      expect(collapsed.querySelector('[data-testid="copy-transfer-btn"]')).not.toBeNull()
      expect(collapsed.querySelector('[data-testid="toggle-paid-btn"]')).toBeNull()
    })

    it('has no collapsed block and no paid note for an unpaid invoice', async () => {
      mockOne()

      await render('/invoices/KSEF-001?tenantId=tenant-1')

      expect(testId('pay-collapsed')).toBeNull()
      expect(container.textContent).not.toContain(PAID_NOTE)
      expect(testId('invoice-detail-paid-status')?.textContent).toBe('Nieopłacona')
    })
  })

  describe('mark paid', () => {
    const paid = invoiceOf({ isPaid: true, paidAt: '2026-03-04T10:00:00Z' })

    it('sends !isPaid, flips the status from the response, and can be undone', async () => {
      vi.mocked(getTransferDetails).mockResolvedValue(transferOf())
      vi.mocked(getInvoiceByKSeFNumber).mockResolvedValueOnce(invoiceOf())
      await render('/invoices/KSEF-001?tenantId=tenant-1')
      expect(testId('invoice-detail-paid-status')?.textContent).toBe('Nieopłacona')

      vi.mocked(setInvoicePaid).mockResolvedValueOnce(paid)
      await click(testId('toggle-paid-btn'))

      expect(setInvoicePaid).toHaveBeenCalledExactlyOnceWith('tenant-1', 'inv-1', true)
      expect(testId('invoice-detail-paid-status')?.textContent).toBe('Opłacona')
      expect(testId('toggle-paid-btn')?.textContent).toBe('Cofnij oznaczenie')

      vi.mocked(setInvoicePaid).mockResolvedValueOnce(invoiceOf())
      await click(testId('toggle-paid-btn'))

      expect(setInvoicePaid).toHaveBeenLastCalledWith('tenant-1', 'inv-1', false)
      expect(testId('invoice-detail-paid-status')?.textContent).toBe('Nieopłacona')
      expect(testId('pay-title')?.textContent).toBe('Zapłać tę fakturę')
    })

    it('shows the status the server saved, not an optimistic one', async () => {
      const save = deferred<InvoiceResponse>()
      mockOne()
      vi.mocked(setInvoicePaid).mockReturnValue(save.promise)
      await render('/invoices/KSEF-001?tenantId=tenant-1')

      await click(testId('toggle-paid-btn'))

      expect(testId('invoice-detail-paid-status')?.textContent).toBe('Nieopłacona')
      expect(testId('toggle-paid-btn')?.textContent).toBe('Zapisywanie...')
      expect((testId('toggle-paid-btn') as HTMLButtonElement).disabled).toBe(true)

      await act(async () => save.resolve(paid))
    })

    it('toasts on failure and leaves the status', async () => {
      mockOne()
      vi.mocked(setInvoicePaid).mockRejectedValue(new Error('boom'))
      await render('/invoices/KSEF-001?tenantId=tenant-1')

      await click(testId('toggle-paid-btn'))

      expect(toast.error).toHaveBeenCalledExactlyOnceWith('Nie udało się zmienić statusu płatności.')
      expect(testId('invoice-detail-paid-status')?.textContent).toBe('Nieopłacona')
      expect((testId('toggle-paid-btn') as HTMLButtonElement).disabled).toBe(false)
      expect(container.textContent).not.toContain('Nie udało się zmienić statusu płatności.')
    })

    it('patches the list row at once, in every cached page of this company', async () => {
      mockOne()
      vi.mocked(setInvoicePaid).mockResolvedValue(paid)
      const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
      const page = (id: string, flagged = false): PagedResult<InvoiceResponse> => ({
        items: [invoiceOf({ id, isPaid: flagged })],
        page: 1,
        pageSize: 25,
        totalCount: 1,
        totalPages: 1,
      })
      client.setQueryData(['invoices', 'tenant-1', '', '', '', 1], page('inv-1'))
      client.setQueryData(['invoices', 'tenant-1', 'abc', '', '', 2], page('inv-1'))
      client.setQueryData(['invoices', 'tenant-1', '', '', '', 3], page('inv-other'))
      client.setQueryData(['invoices', 'tenant-2', '', '', '', 1], page('inv-1'))

      await render('/invoices/KSEF-001?tenantId=tenant-1', client)
      await click(testId('toggle-paid-btn'))

      const row = (key: unknown[]) => client.getQueryData<PagedResult<InvoiceResponse>>(key)!.items[0]
      expect(row(['invoices', 'tenant-1', '', '', '', 1])).toMatchObject({ isPaid: true, paidAt: paid.paidAt })
      expect(row(['invoices', 'tenant-1', 'abc', '', '', 2])).toMatchObject({ isPaid: true, paidAt: paid.paidAt })
      expect(row(['invoices', 'tenant-1', '', '', '', 3]).isPaid).toBe(false)
      expect(row(['invoices', 'tenant-2', '', '', '', 1]).isPaid).toBe(false)
      expect(client.getQueryState(['invoices', 'tenant-1', '', '', '', 1])?.isInvalidated).toBe(true)
    })
  })

  describe('walking the snapshot', () => {
    const numbers = ['KSEF-A', 'KSEF-B', 'KSEF-C']
    const state = { listSearch: '?tenantId=tenant-1&q=abc&page=2', siblings: numbers }
    const entry = (number: string) => ({ pathname: `/invoices/${number}`, search: '?tenantId=tenant-1', state })
    const invoiceA = invoiceOf({ id: 'inv-a', ksefInvoiceNumber: 'KSEF-A', invoiceNumber: 'FV/A' })
    const invoiceB = invoiceOf({ id: 'inv-b', ksefInvoiceNumber: 'KSEF-B', invoiceNumber: 'FV/B' })
    const invoiceC = invoiceOf({ id: 'inv-c', ksefInvoiceNumber: 'KSEF-C', invoiceNumber: 'FV/C' })

    beforeEach(() => {
      mockMany([
        { invoice: invoiceA, transfer: transferOf({ qrCodeBase64: QR_IMAGE, title: 'FV/A' }) },
        { invoice: invoiceB, transfer: transferOf({ qrCodeBase64: OTHER_QR_IMAGE, title: 'FV/B' }) },
        { invoice: invoiceC, transfer: transferOf({ qrCodeBase64: null, qrMessage: 'x', title: 'FV/C' }) },
      ])
    })

    it.each([
      ['KSEF-A', null, 'KSEF-B'],
      ['KSEF-B', 'KSEF-A', 'KSEF-C'],
      ['KSEF-C', 'KSEF-B', null],
    ])('at %s shows previous %s and next %s, hiding the ends', async (number, previous, next) => {
      await render(entry(number))

      const href = (target: string | null) => (target ? `/invoices/${target}?tenantId=tenant-1` : undefined)
      expect(testId('invoice-prev')?.getAttribute('href')).toBe(href(previous))
      expect(testId('invoice-next')?.getAttribute('href')).toBe(href(next))
      if (previous) expect(testId('invoice-prev')?.getAttribute('aria-label')).toBe('Poprzednia faktura')
      if (next) expect(testId('invoice-next')?.getAttribute('aria-label')).toBe('Następna faktura')
    })

    it('shows neither button on a deep link', async () => {
      await render('/invoices/KSEF-B?tenantId=tenant-1')

      expect(testId('invoice-prev')).toBeNull()
      expect(testId('invoice-next')).toBeNull()
    })

    it('shows neither button when the invoice is not in the snapshot, or the state has no snapshot', async () => {
      await render({ pathname: '/invoices/KSEF-Z', search: '?tenantId=tenant-1', state })
      expect(testId('invoice-prev')).toBeNull()
      expect(testId('invoice-next')).toBeNull()
      await act(async () => root.unmount())

      root = createRoot(container)
      await render({ pathname: '/invoices/KSEF-A', search: '?tenantId=tenant-1', state: { listSearch: '?q=a' } })
      expect(testId('invoice-prev')).toBeNull()
      expect(testId('invoice-next')).toBeNull()
    })

    it('walks with replace, keeping the state, the company and Back to the list', async () => {
      const { router } = await render(entry('KSEF-A'))

      await click(testId('invoice-next'))

      expect(router.state.location.pathname).toBe('/invoices/KSEF-B')
      expect(router.state.location.search).toBe('?tenantId=tenant-1')
      expect(router.state.location.state).toEqual(state)
      expect(router.state.historyAction).toBe('REPLACE')
      expect(testId('invoice-detail-title')?.textContent).toBe('FV/B')
      expect(testId('invoice-prev')?.getAttribute('href')).toBe('/invoices/KSEF-A?tenantId=tenant-1')
      expect(testId('invoice-details-back-link')?.getAttribute('href')).toBe(`/invoices${state.listSearch}`)

      await act(async () => router.navigate(-1))
      expect(router.state.location.pathname).toBe('/invoices')
    })

    it('encodes a number with a slash in the walk link', async () => {
      const slash = 'KSEF/1'
      mockMany([{ invoice: invoiceOf({ ksefInvoiceNumber: slash }) }])
      await render({
        pathname: `/invoices/${encodeURIComponent(slash)}`,
        search: '?tenantId=tenant-1',
        state: { siblings: [slash, 'KSEF/2'] },
      })

      expect(testId('invoice-next')?.getAttribute('href')).toBe('/invoices/KSEF%2F2?tenantId=tenant-1')
    })

    it('keeps the buttons and steps past a missing invoice', async () => {
      const { router } = await render({
        pathname: '/invoices/KSEF-Y',
        search: '?tenantId=tenant-1',
        state: { siblings: ['KSEF-Y', 'KSEF-A'] },
      })

      expect(container.textContent).toContain('Nie znaleziono żądanej faktury.')
      await click(testId('invoice-next'))

      expect(router.state.location.pathname).toBe('/invoices/KSEF-A')
      expect(testId('invoice-detail-title')?.textContent).toBe('FV/A')
    })

    it('keeps focus on the same link while walking', async () => {
      await render(entry('KSEF-A'))
      const link = testId('invoice-next')
      link?.focus()

      await click(link)

      expect(testId('invoice-next')).toBe(link)
      expect(document.activeElement).toBe(link)
    })

    it('moves focus to the other link when the used one hides at the end', async () => {
      await render(entry('KSEF-B'))
      testId('invoice-next')?.focus()

      await click(testId('invoice-next'))

      expect(testId('invoice-next')).toBeNull()
      expect(document.activeElement).toBe(testId('invoice-prev'))
    })

    it('leaves focus alone on a first render', async () => {
      await render(entry('KSEF-B'))

      expect(document.activeElement).toBe(document.body)
    })

    it('never shows the previous QR while the next invoice or its transfer loads', async () => {
      const invoiceLoad = deferred<InvoiceResponse>()
      const transferLoad = deferred<TransferDetailsResponse>()
      vi.mocked(getInvoiceByKSeFNumber).mockImplementation((_tenant, number) =>
        number === 'KSEF-A' ? Promise.resolve(invoiceA) : invoiceLoad.promise,
      )
      vi.mocked(getTransferDetails).mockImplementation((_tenant, id) =>
        id === 'inv-a' ? Promise.resolve(transferOf({ title: 'FV/A' })) : transferLoad.promise,
      )
      await render(entry('KSEF-A'))
      expect(testId('qr-code-image')?.getAttribute('src')).toBe(QR_IMAGE)

      await click(testId('invoice-next'))
      expect(testId('qr-code-image')).toBeNull()
      expect(testId('transfer-title')).toBeNull()
      expect(testId('invoice-detail-title')?.textContent).toBe('KSEF-B')

      await act(async () => invoiceLoad.resolve(invoiceB))
      await settle()
      expect(testId('invoice-detail-title')?.textContent).toBe('FV/B')
      expect(testId('qr-code-image')).toBeNull()
      expect(testId('copy-transfer-btn')).toBeNull()
      expect(testId('pay-title')?.textContent).toBe('Dane do przelewu')

      await act(async () => transferLoad.resolve(transferOf({ qrCodeBase64: OTHER_QR_IMAGE, title: 'FV/B' })))
      await settle()
      expect(testId('qr-code-image')?.getAttribute('src')).toBe(OTHER_QR_IMAGE)
      expect(testId('pay-title')?.textContent).toBe('Zapłać tę fakturę')
    })

    function clientWithCachedB(invoice: InvoiceResponse): QueryClient {
      const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
      client.setQueryData(['invoice-details', 'tenant-1', 'KSEF-B'], invoice)
      client.setQueryData(['transfer-details', 'tenant-1', 'inv-b'], transferOf({ title: 'FV/B' }))
      return client
    }

    it('closes the collapsed transfer data when walking to another paid invoice already cached', async () => {
      const paidB = { ...invoiceB, isPaid: true }
      mockMany([{ invoice: { ...invoiceA, isPaid: true } }, { invoice: paidB }])
      await render(entry('KSEF-A'), clientWithCachedB(paidB))
      const open = testId('pay-collapsed') as HTMLDetailsElement
      open.open = true

      await click(testId('invoice-next'))

      const next = testId('pay-collapsed') as HTMLDetailsElement
      expect(testId('invoice-detail-title')?.textContent).toBe('FV/B')
      expect(next.open).toBe(false)
    })

    it('does not show the copied state on the next invoice already cached', async () => {
      Object.defineProperty(navigator, 'clipboard', { value: { writeText: vi.fn().mockResolvedValue(undefined) }, configurable: true })
      await render(entry('KSEF-A'), clientWithCachedB(invoiceB))
      await click(testId('copy-transfer-btn'))
      expect(testId('copy-transfer-btn')?.textContent).toBe('Skopiowano!')

      await click(testId('invoice-next'))

      expect(testId('copy-transfer-btn')?.textContent).toBe('Kopiuj dane przelewu')
    })

    it('applies a late mark paid to the invoice marked, not the one shown', async () => {
      const save = deferred<InvoiceResponse>()
      vi.mocked(setInvoicePaid).mockReturnValue(save.promise)
      const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
      const listKey = ['invoices', 'tenant-1', '', '', '', 1]
      client.setQueryData<PagedResult<InvoiceResponse>>(listKey, {
        items: [invoiceA, invoiceB],
        page: 1,
        pageSize: 25,
        totalCount: 2,
        totalPages: 1,
      })

      await render(entry('KSEF-A'), client)
      await click(testId('toggle-paid-btn'))
      expect(setInvoicePaid).toHaveBeenCalledExactlyOnceWith('tenant-1', 'inv-a', true)

      await click(testId('invoice-next'))
      expect(testId('invoice-detail-title')?.textContent).toBe('FV/B')
      expect(testId('toggle-paid-btn')?.textContent).toBe('Oznacz jako opłaconą')
      expect((testId('toggle-paid-btn') as HTMLButtonElement).disabled).toBe(false)

      const fetchesOfB = () => vi.mocked(getInvoiceByKSeFNumber).mock.calls.filter((call) => call[1] === 'KSEF-B').length
      const before = fetchesOfB()
      const paidA = { ...invoiceA, isPaid: true, paidAt: '2026-03-04T10:00:00Z' }
      await act(async () => save.resolve(paidA))
      await settle()

      const items = client.getQueryData<PagedResult<InvoiceResponse>>(listKey)!.items
      expect(items[0]).toMatchObject({ id: 'inv-a', isPaid: true, paidAt: paidA.paidAt })
      expect(items[1]).toMatchObject({ id: 'inv-b', isPaid: false, paidAt: null })
      expect(client.getQueryData<InvoiceResponse>(['invoice-details', 'tenant-1', 'KSEF-A'])?.isPaid).toBe(true)
      expect(client.getQueryData<InvoiceResponse>(['invoice-details', 'tenant-1', 'KSEF-B'])?.isPaid).toBe(false)
      expect(fetchesOfB()).toBe(before)
      expect(testId('invoice-detail-paid-status')?.textContent).toBe('Nieopłacona')
    })

    it('shows a marked invoice as paid when walking back, before any refetch lands', async () => {
      const save = deferred<InvoiceResponse>()
      vi.mocked(setInvoicePaid).mockReturnValue(save.promise)
      await render(entry('KSEF-A'))
      await click(testId('toggle-paid-btn'))
      await click(testId('invoice-next'))

      vi.mocked(getInvoiceByKSeFNumber).mockImplementation((_tenant, number) =>
        number === 'KSEF-A' ? new Promise<InvoiceResponse>(() => {}) : Promise.resolve(invoiceB),
      )
      await act(async () => save.resolve({ ...invoiceA, isPaid: true, paidAt: '2026-03-04T10:00:00Z' }))
      await settle()
      await click(testId('invoice-prev'))

      expect(testId('invoice-detail-title')?.textContent).toBe('FV/A')
      expect(testId('invoice-detail-paid-status')?.textContent).toBe('Opłacona')
      expect(testId('pay-title')?.textContent).toBe('Opłacona')
    })

    it('flips the status from the saved response, with a slow GET pending', async () => {
      vi.mocked(setInvoicePaid).mockResolvedValue({ ...invoiceA, isPaid: true, paidAt: '2026-03-04T10:00:00Z' })
      await render(entry('KSEF-A'))
      vi.mocked(getInvoiceByKSeFNumber).mockImplementation(() => new Promise<InvoiceResponse>(() => {}))

      await click(testId('toggle-paid-btn'))

      expect(testId('invoice-detail-paid-status')?.textContent).toBe('Opłacona')
      expect(testId('toggle-paid-btn')?.textContent).toBe('Cofnij oznaczenie')
    })

    it('replaces the QR element when walking to an invoice already cached', async () => {
      const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
      client.setQueryData(['invoice-details', 'tenant-1', 'KSEF-B'], invoiceB)
      client.setQueryData(['transfer-details', 'tenant-1', 'inv-b'], transferOf({ qrCodeBase64: OTHER_QR_IMAGE, title: 'FV/B' }))
      await render(entry('KSEF-A'), client)
      const first = testId('qr-code-image')

      await click(testId('invoice-next'))

      expect(testId('qr-code-image')?.getAttribute('src')).toBe(OTHER_QR_IMAGE)
      expect(testId('qr-code-image')).not.toBe(first)
    })

    it('applies both when A and B are marked while both responses are pending', async () => {
      const saveA = deferred<InvoiceResponse>()
      const saveB = deferred<InvoiceResponse>()
      vi.mocked(setInvoicePaid).mockImplementation((_tenant, id) => (id === 'inv-a' ? saveA.promise : saveB.promise))
      const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
      const listKey = ['invoices', 'tenant-1', '', '', '', 1]
      client.setQueryData<PagedResult<InvoiceResponse>>(listKey, { items: [invoiceA, invoiceB], page: 1, pageSize: 25, totalCount: 2, totalPages: 1 })
      await render(entry('KSEF-A'), client)
      await click(testId('toggle-paid-btn'))
      await click(testId('invoice-next'))
      await click(testId('toggle-paid-btn'))

      await act(async () => saveA.resolve({ ...invoiceA, isPaid: true, paidAt: '2026-03-04T10:00:00Z' }))
      await settle()
      await act(async () => saveB.resolve({ ...invoiceB, isPaid: true, paidAt: '2026-03-04T10:00:01Z' }))
      await settle()

      const items = client.getQueryData<PagedResult<InvoiceResponse>>(listKey)!.items
      expect(items.map((row) => row.isPaid)).toEqual([true, true])
      expect(toast.error).not.toHaveBeenCalled()
    })

    it('shows paidAt and acquisitionDate as the Warsaw date', async () => {
      mockOne(invoiceOf({ isPaid: true, paidAt: '2026-11-30T23:30:00Z', acquisitionDate: '2026-06-30T22:30:00Z', issueDate: '2026-11-30T00:00:00Z' }))
      await render('/invoices/KSEF-001?tenantId=tenant-1')

      expect(testId('invoice-detail-paid-date')?.textContent?.trim()).toBe('01.12.2026')
      expect(testId('invoice-detail-acquisition-date')?.textContent).toBe('01.07.2026')
      expect(testId('invoice-detail-issue-date')?.textContent).toBe('30.11.2026')
    })

    it('tells the user when the clipboard write fails', async () => {
      Object.defineProperty(navigator, 'clipboard', { value: { writeText: vi.fn().mockRejectedValue(new Error('denied')) }, configurable: true })
      await render(entry('KSEF-A'))

      await click(testId('copy-transfer-btn'))

      expect(toast.error).toHaveBeenCalledExactlyOnceWith('Nie udało się skopiować danych przelewu.')
      expect(testId('copy-transfer-btn')?.textContent).toBe('Kopiuj dane przelewu')
    })

    it('keeps the saved status with no banner when the GET would fail after a mark', async () => {
      vi.mocked(setInvoicePaid).mockResolvedValue({ ...invoiceA, isPaid: true, paidAt: '2026-03-04T10:00:00Z' })
      await render(entry('KSEF-A'))
      vi.mocked(getInvoiceByKSeFNumber).mockRejectedValue(new ApiError('Server Error', 500))

      await click(testId('toggle-paid-btn'))

      expect(testId('invoice-detail-paid-status')?.textContent).toBe('Opłacona')
      expect(container.querySelector('[role="alert"]')).toBeNull()
    })

    it('shows the invoice load failure in Polish, not the server text', async () => {
      vi.mocked(getInvoiceByKSeFNumber).mockRejectedValue(new ApiError('Server Error', 500))

      await render(entry('KSEF-A'))

      expect(container.querySelector('[role="alert"]')?.textContent).toContain('Nie udało się pobrać faktury.')
      expect(container.textContent).not.toContain('Server Error')
    })

    it('clears the copied label when a copy fails after a good one', async () => {
      const writeText = vi.fn().mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error('denied'))
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
      await render(entry('KSEF-A'))

      await click(testId('copy-transfer-btn'))
      expect(testId('copy-transfer-btn')?.textContent).toBe('Skopiowano!')

      await click(testId('copy-transfer-btn'))

      expect(toast.error).toHaveBeenCalledExactlyOnceWith('Nie udało się skopiować danych przelewu.')
      expect(testId('copy-transfer-btn')?.textContent).toBe('Kopiuj dane przelewu')
    })

    it('toasts a late failure after walking on', async () => {
      const save = deferred<InvoiceResponse>()
      vi.mocked(setInvoicePaid).mockReturnValue(save.promise)
      await render(entry('KSEF-A'))
      await click(testId('toggle-paid-btn'))
      await click(testId('invoice-next'))

      await act(async () => save.reject(new Error('boom')))
      await settle()

      expect(toast.error).toHaveBeenCalledExactlyOnceWith('Nie udało się zmienić statusu płatności.')
      expect(testId('invoice-detail-paid-status')?.textContent).toBe('Nieopłacona')
    })
  })
})
