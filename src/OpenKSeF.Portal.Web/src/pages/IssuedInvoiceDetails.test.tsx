// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  copyIssuedInvoice,
  createIssuedInvoice,
  deleteIssuedInvoice,
  getIssuedInvoice,
  getIssuedInvoiceXml,
  issueIssuedInvoice,
  updateIssuedInvoice,
} from '@/api/endpoints/issuedInvoices'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { listTenants } from '@/api/endpoints/tenants'
import { normalizeError } from '@/api/errors'
import type { IssuedInvoiceResponse, SellerProfileResponse } from '@/api/types'
import { ksefPollInterval } from '@/invoicing/status'
import { generateInvoice } from '@/vendor/ksef-pdf-generator/ksef-fe-invoice-converter.js'
import { InvoiceDraftPage } from './InvoiceDraft'
import { IssuedInvoiceDetailsPage } from './IssuedInvoiceDetails'

vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))
vi.mock('@/api/endpoints/sellerProfile', () => ({ getSellerProfile: vi.fn() }))
vi.mock('@/api/endpoints/nipLookup', () => ({ lookupNip: vi.fn() }))
vi.mock('@/api/endpoints/issuedInvoices', () => ({
  createIssuedInvoice: vi.fn(),
  updateIssuedInvoice: vi.fn(),
  getIssuedInvoice: vi.fn(),
  issueIssuedInvoice: vi.fn(),
  getIssuedInvoiceXml: vi.fn(),
  getIssuedInvoiceUpo: vi.fn(),
  copyIssuedInvoice: vi.fn(),
  deleteIssuedInvoice: vi.fn(),
}))
vi.mock('@/vendor/ksef-pdf-generator/ksef-fe-invoice-converter.js', () => ({ generateInvoice: vi.fn() }))

const profile: SellerProfileResponse = {
  legalName: 'OpenKSeF Test A sp. z o.o.',
  addressLine1: 'ul. Prosta 1',
  addressLine2: '00-001 Warszawa',
  bankAccount: '61109010140000071219812874',
  cashAccounting: false,
  numberingPattern: 'FV/{nr}/{MM}/{RRRR}',
  nextNumber: 1,
  nextNumberPeriod: '2026-11',
  nip: '5260250274',
  isComplete: true,
  nextNumberPreview: 'FV/1/11/2026',
  ksefEnvironment: 'production',
}

const ksef = {
  environment: 'test' as const,
  ksefNumber: null,
  acceptedAt: null,
  statusCode: null,
  statusMessage: null,
  statusDetails: null,
  lastError: null,
  verificationUrl: null,
  hasUpo: false,
}

const invoice = (patch: Partial<IssuedInvoiceResponse>): IssuedInvoiceResponse => ({
  id: 'i-1',
  status: 'Draft',
  number: 'FV/1/11/2026',
  seller: { nip: '5260250274', name: 'Sprzedawca', addressLine1: 'a', addressLine2: 'b', bankAccount: null, cashAccounting: false },
  buyer: { kind: 'Company', nip: '5260250274', name: 'Nabywca', addressLine1: 'a', addressLine2: 'b' },
  issueDate: '2026-11-03',
  saleDate: '2026-11-03',
  dueDate: '2026-11-17',
  paymentMethod: 'Transfer',
  splitPayment: false,
  exemptionBasis: null,
  lines: [{ lineNo: 1, name: 'Usługa', unit: 'szt.', quantity: 1, unitNetPrice: 100, vatRate: '23', netAmount: 100, vatAmount: 23 }],
  vatSummary: [{ vatRate: '23', net: 100, vat: 23 }],
  totalNet: 100,
  totalVat: 23,
  totalGross: 123,
  currency: 'PLN',
  ksef,
  warnings: [],
  createdAt: '2026-11-03T10:00:00Z',
  updatedAt: '2026-11-03T10:00:00Z',
  issuedAt: null,
  ...patch,
})

const flush = () => new Promise<void>((resolve) => setTimeout(resolve, 0))

async function waitFor(assertion: () => boolean): Promise<void> {
  for (let i = 0; i < 100; i++) {
    if (assertion()) return
    await act(flush)
  }
  throw new Error('Condition not met')
}

const buttonByText = (container: HTMLElement, text: string) =>
  [...container.querySelectorAll('button')].find((button) => button.textContent?.includes(text))

describe('KSeF status polling', () => {
  it('polls Sending every 3 s for 2 min, then every 30 s, and stops otherwise', () => {
    expect(ksefPollInterval('Sending', 0)).toBe(3000)
    expect(ksefPollInterval('Sending', 119_999)).toBe(3000)
    expect(ksefPollInterval('Sending', 120_000)).toBe(30000)
    expect(ksefPollInterval('Accepted', 0)).toBe(false)
    expect(ksefPollInterval('Rejected', 0)).toBe(false)
    expect(ksefPollInterval(undefined, 0)).toBe(false)
  })
})

describe('issue and detail pages', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
    vi.clearAllMocks()
    vi.mocked(listTenants).mockResolvedValue([
      { id: 't-1', nip: '5260250274', displayName: 'A', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' },
    ])
    vi.mocked(getSellerProfile).mockResolvedValue(profile)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  async function render(path: string): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    await act(async () =>
      root.render(
        <QueryClientProvider client={client}>
          <MemoryRouter initialEntries={[path]}>
            <Routes>
              <Route path="/issued/new" element={<InvoiceDraftPage />} />
              <Route path="/issued/:id/edit" element={<InvoiceDraftPage />} />
              <Route path="/issued/:id" element={<IssuedInvoiceDetailsPage />} />
            </Routes>
          </MemoryRouter>
        </QueryClientProvider>,
      ),
    )
  }

  it('PRODUKCJA asks for confirmation, then saves, issues and opens the detail page', async () => {
    vi.mocked(createIssuedInvoice).mockResolvedValue(invoice({ number: null }))
    vi.mocked(issueIssuedInvoice).mockResolvedValue(invoice({ status: 'Sending' }))
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Sending' }))
    await render('/issued/new?tenantId=t-1')
    await waitFor(() => Boolean(buttonByText(container, 'Wystaw i wyślij do KSeF')))

    await act(async () => buttonByText(container, 'Wystaw i wyślij do KSeF')!.click())
    const dialog = container.querySelector('[role="alertdialog"]')?.textContent
    expect(dialog).toContain('dokumentem prawnym')
    expect(dialog).toContain('Nadamy przy wystawieniu')
    expect(dialog).not.toContain('FV/1/11/2026')
    expect(issueIssuedInvoice).not.toHaveBeenCalled()

    await act(async () => buttonByText(container.querySelector<HTMLElement>('[role="alertdialog"]')!, 'Wystaw i wyślij')!.click())
    await waitFor(() => container.textContent?.includes('czekamy na przyjęcie') ?? false)
    expect(vi.mocked(issueIssuedInvoice).mock.calls[0]).toEqual(['t-1', 'i-1'])
  })

  it('issue 422 keeps the form and shows the errors', async () => {
    vi.mocked(getSellerProfile).mockResolvedValue({ ...profile, ksefEnvironment: 'test' })
    vi.mocked(createIssuedInvoice).mockResolvedValue(invoice({ number: null }))
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ number: null }))
    const body = { errors: [{ code: 'INV-VAL-015', field: 'buyer.name', message: 'Uzupełnij nazwę nabywcy' }], warnings: [] }
    vi.mocked(issueIssuedInvoice).mockRejectedValue(
      await normalizeError(new Response(JSON.stringify(body), { status: 422, headers: { 'Content-Type': 'application/json' } })),
    )
    await render('/issued/new?tenantId=t-1')
    await waitFor(() => Boolean(buttonByText(container, 'Wystaw i wyślij do KSeF')))

    await act(async () => buttonByText(container, 'Wystaw i wyślij do KSeF')!.click())
    await waitFor(() => container.querySelector('#buyer-name-error') !== null)
    expect(container.querySelector('[role="alertdialog"]')).toBeNull()
  })

  it('INV-VAL-020 on issue shows inline on the issue date field', async () => {
    vi.mocked(getSellerProfile).mockResolvedValue({ ...profile, ksefEnvironment: 'test' })
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ number: null }))
    vi.mocked(updateIssuedInvoice).mockResolvedValue(invoice({ number: null }))
    const body = { errors: [{ code: 'INV-VAL-020', field: 'issueDate', message: 'Data wystawienia nie może być późniejsza niż dzisiejsza' }], warnings: [] }
    vi.mocked(issueIssuedInvoice).mockRejectedValue(
      await normalizeError(new Response(JSON.stringify(body), { status: 422, headers: { 'Content-Type': 'application/json' } })),
    )
    await render('/issued/i-1/edit?tenantId=t-1')
    await waitFor(() => Boolean(buttonByText(container, 'Wystaw i wyślij do KSeF')))

    await act(async () => buttonByText(container, 'Wystaw i wyślij do KSeF')!.click())
    await waitFor(() => container.querySelector('#issue-date-error') !== null)

    expect(container.querySelector('#issue-date-error')?.textContent).toContain('późniejsza niż dzisiejsza')
    expect(container.querySelector('#issue-date')?.getAttribute('aria-invalid')).toBe('true')
  })

  it('Accepted shows the KSeF number and downloads, no edit', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(
      invoice({ status: 'Accepted', ksef: { ...ksef, ksefNumber: '5260250274-20261103-ABC', acceptedAt: '2026-11-03T10:05:00Z', hasUpo: true } }),
    )
    vi.mocked(getIssuedInvoiceXml).mockResolvedValue('<Faktura/>')
    const createObjectURL = vi.fn(() => 'blob:x')
    Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() })
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes('5260250274-20261103-ABC') ?? false)

    expect(buttonByText(container, 'Pobierz UPO')).toBeDefined()
    expect(container.textContent).not.toContain('Popraw i wyślij ponownie')
    expect(container.querySelector('a[href*="/edit"]')).toBeNull()

    await act(async () => buttonByText(container, 'Pobierz XML')!.click())
    await waitFor(() => createObjectURL.mock.calls.length === 1)
    expect(getIssuedInvoiceXml).toHaveBeenCalledWith('t-1', 'i-1')
  })

  it('Rejected shows the KSeF message, details and the edit link', async () => {
    const statusDetails = 'Data wystawienia faktury nie może być późniejsza niż data przyjęcia do systemu.'
    vi.mocked(getIssuedInvoice).mockResolvedValue(
      invoice({ status: 'Rejected', ksef: { ...ksef, statusCode: 450, statusMessage: 'Błąd weryfikacji semantyki', statusDetails } }),
    )
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes('Kod KSeF 450: Błąd weryfikacji semantyki') ?? false)

    expect(container.querySelector('[role="alert"]')?.textContent).toContain(statusDetails)
    expect(container.querySelector('a[href*="/issued/i-1/edit"]')?.textContent).toContain('Popraw i wyślij ponownie')
    expect(buttonByText(container, 'Pobierz XML')).toBeUndefined()
    expect(buttonByText(container, 'Usuń')).toBeUndefined()
  })

  it('Rejected with a duplicate number says it is taken and offers only the copy', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(
      invoice({ status: 'Rejected', ksef: { ...ksef, statusCode: 440, statusMessage: 'Duplikat faktury' } }),
    )
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes('jest już zajęty w KSeF') ?? false)

    expect(buttonByText(container, 'Kopiuj jako nową')).toBeDefined()
    expect(container.textContent).not.toContain('Popraw i wyślij ponownie')
    expect(container.querySelector('a[href*="/edit"]')).toBeNull()
  })

  it('Sending hides Kopiuj jako nową', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Sending' }))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes('czekamy na przyjęcie') ?? false)

    expect(buttonByText(container, 'Kopiuj jako nową')).toBeUndefined()
  })

  it('Usuń szkic deletes a draft only after confirmation', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ number: null }))
    vi.mocked(deleteIssuedInvoice).mockResolvedValue(undefined)
    await render('/issued/i-1/edit?tenantId=t-1')
    await waitFor(() => Boolean(buttonByText(container, 'Usuń szkic')))

    await act(async () => buttonByText(container, 'Usuń szkic')!.click())
    const dialog = () => container.querySelector<HTMLElement>('[role="alertdialog"]')!
    expect(dialog().textContent).toContain('trwale usunięty')
    expect(deleteIssuedInvoice).not.toHaveBeenCalled()

    await act(async () => buttonByText(dialog(), 'Wróć do edycji')!.click())
    expect(container.querySelector('[role="alertdialog"]')).toBeNull()
    expect(deleteIssuedInvoice).not.toHaveBeenCalled()

    await act(async () => buttonByText(container, 'Usuń szkic')!.click())
    await act(async () => buttonByText(dialog(), 'Usuń szkic')!.click())
    await waitFor(() => vi.mocked(deleteIssuedInvoice).mock.calls.length === 1)
    expect(deleteIssuedInvoice).toHaveBeenCalledWith('t-1', 'i-1')
    await waitFor(() => !buttonByText(container, 'Zapisz szkic'))
  })

  it('Usuń szkic failure keeps the form and shows the error', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ number: null }))
    vi.mocked(deleteIssuedInvoice).mockRejectedValue(new Error('Faktura nie jest już szkicem'))
    await render('/issued/i-1/edit?tenantId=t-1')
    await waitFor(() => Boolean(buttonByText(container, 'Usuń szkic')))

    await act(async () => buttonByText(container, 'Usuń szkic')!.click())
    await act(async () => buttonByText(container.querySelector<HTMLElement>('[role="alertdialog"]')!, 'Usuń szkic')!.click())
    await waitFor(() => container.textContent?.includes('Faktura nie jest już szkicem') ?? false)

    expect(container.querySelector('[role="alertdialog"]')).toBeNull()
    expect(buttonByText(container, 'Zapisz szkic')).toBeDefined()
  })

  it('Rejected edit offers no delete', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Rejected' }))
    await render('/issued/i-1/edit?tenantId=t-1')
    await waitFor(() => Boolean(buttonByText(container, 'Zapisz szkic')))

    expect(buttonByText(container, 'Usuń szkic')).toBeUndefined()
  })

  it('Kopiuj jako nową opens the new draft and leaves the source alone', async () => {
    const source = invoice({ status: 'Accepted', ksef: { ...ksef, ksefNumber: 'K-1' } })
    const copy = invoice({ id: 'i-2', number: null, issueDate: '2026-12-01', saleDate: '2026-12-01', dueDate: '2026-12-15' })
    vi.mocked(getIssuedInvoice).mockImplementation(async (_tenantId, id) => (id === 'i-2' ? copy : source))
    vi.mocked(copyIssuedInvoice).mockResolvedValue(copy)
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => Boolean(buttonByText(container, 'Kopiuj jako nową')))

    await act(async () => buttonByText(container, 'Kopiuj jako nową')!.click())
    await waitFor(() => container.querySelector<HTMLInputElement>('#buyer-name')?.value === 'Nabywca')

    expect(copyIssuedInvoice).toHaveBeenCalledWith('t-1', 'i-1')
    expect(getIssuedInvoice).toHaveBeenLastCalledWith('t-1', 'i-2')
    expect(buttonByText(container, 'Zapisz szkic')).toBeDefined()
    expect(container.querySelector<HTMLInputElement>('input[value="2026-12-01"]')).not.toBeNull()
    expect(updateIssuedInvoice).not.toHaveBeenCalled()
    expect(deleteIssuedInvoice).not.toHaveBeenCalled()
    expect(issueIssuedInvoice).not.toHaveBeenCalled()
  })

  it('Kopiuj jako nową failure stays on the source and shows the error', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Rejected' }))
    vi.mocked(copyIssuedInvoice).mockRejectedValue(new Error('Nie można skopiować'))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => Boolean(buttonByText(container, 'Kopiuj jako nową')))

    await act(async () => buttonByText(container, 'Kopiuj jako nową')!.click())
    await waitFor(() => container.textContent?.includes('Nie można skopiować') ?? false)
    expect(buttonByText(container, 'Kopiuj jako nową')).toBeDefined()
  })

  it('Pobierz PDF renders the stored XML with the KSeF number and KOD I', async () => {
    const verificationUrl = 'https://qr-test.ksef.mf.gov.pl/invoice/5260250274/03-11-2026/abc'
    vi.mocked(getIssuedInvoice).mockResolvedValue(
      invoice({ status: 'Accepted', ksef: { ...ksef, ksefNumber: 'K-1', verificationUrl } }),
    )
    vi.mocked(getIssuedInvoiceXml).mockResolvedValue('<Faktura/>')
    vi.mocked(generateInvoice).mockResolvedValue(new Blob(['%PDF']))
    const createObjectURL = vi.fn<(blob: Blob) => string>(() => 'blob:x')
    Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() })
    const fileNames: string[] = []
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      fileNames.push(this.download)
    })
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => Boolean(buttonByText(container, 'Pobierz PDF')))

    await act(async () => buttonByText(container, 'Pobierz PDF')!.click())
    await waitFor(() => createObjectURL.mock.calls.length === 1)
    click.mockRestore()

    const [file, additionalData, format] = vi.mocked(generateInvoice).mock.calls[0]
    const xml = await new Promise((resolve) => {
      const reader = new FileReader()
      reader.onload = () => resolve(reader.result)
      reader.readAsText(file)
    })
    expect(xml).toBe('<Faktura/>')
    expect(additionalData).toEqual({ nrKSeF: 'K-1', qrCode: verificationUrl })
    expect(format).toBe('blob')
    expect(createObjectURL.mock.calls[0][0].type).toBe('application/pdf')
    expect(fileNames).toEqual(['FV_1_11_2026.pdf'])
  })

  it('Pobierz PDF failure shows the error', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(
      invoice({ status: 'Accepted', ksef: { ...ksef, ksefNumber: 'K-1', verificationUrl: 'https://qr-test.ksef.mf.gov.pl/x' } }),
    )
    vi.mocked(getIssuedInvoiceXml).mockResolvedValue('<Faktura/>')
    vi.mocked(generateInvoice).mockRejectedValue(new Error('Unknown XML Version'))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => Boolean(buttonByText(container, 'Pobierz PDF')))

    await act(async () => buttonByText(container, 'Pobierz PDF')!.click())
    await waitFor(() => container.textContent?.includes('Unknown XML Version') ?? false)
  })

  it.each([
    ['Draft', ksef],
    ['Sending', { ...ksef, verificationUrl: 'https://qr-test.ksef.mf.gov.pl/x' }],
    ['Rejected', { ...ksef, ksefNumber: 'K-1', verificationUrl: 'https://qr-test.ksef.mf.gov.pl/x' }],
    ['Accepted', { ...ksef, ksefNumber: 'K-1' }],
  ] as const)('Pobierz PDF is not offered for %s without a KSeF number and KOD I', async (status, ksefData) => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status, ksef: ksefData }))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => Boolean(container.querySelector('#ksef-panel-title')))
    expect(buttonByText(container, 'Pobierz PDF')).toBeUndefined()
  })

  it('Sending with lastError warns not to resend', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Sending', ksef: { ...ksef, lastError: 'Timeout' } }))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes('KSeF nie odpowiada') ?? false)
    expect(container.textContent).toContain('Nie wysyłaj faktury drugi raz')
    expect(container.textContent).toContain('Timeout')
  })
})
