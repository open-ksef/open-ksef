// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import pdfMake from 'pdfmake/build/pdfmake'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  createIssuedInvoice,
  getExchangeRateProposal,
  getIssuedInvoice,
  getIssuedInvoiceXml,
  issueIssuedInvoice,
  listIssuedFeed,
  updateIssuedInvoice,
} from '@/api/endpoints/issuedInvoices'
import { listCredentials } from '@/api/endpoints/credentials'
import { getSellerProfile, updateSellerProfile } from '@/api/endpoints/sellerProfile'
import { listTenants } from '@/api/endpoints/tenants'
import { normalizeError } from '@/api/errors'
import type { IssuedInvoiceResponse, SellerProfileResponse } from '@/api/types'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { generateInvoice } from '@/vendor/ksef-pdf-generator/ksef-fe-invoice-converter.js'
import { InvoiceDraftPage } from './InvoiceDraft'
import { IssuedInvoiceDetailsPage } from './IssuedInvoiceDetails'
import { IssuedInvoiceListPage } from './IssuedInvoiceList'
import { SellerProfilePage } from './SellerProfile'

vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))
vi.mock('@/api/endpoints/credentials', () => ({ listCredentials: vi.fn() }))
vi.mock('@/api/endpoints/sellerProfile', () => ({ getSellerProfile: vi.fn(), updateSellerProfile: vi.fn() }))
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
  getExchangeRateProposal: vi.fn(),
  listIssuedFeed: vi.fn(),
}))
vi.mock('@/vendor/ksef-pdf-generator/ksef-fe-invoice-converter.js', () => ({ generateInvoice: vi.fn() }))
vi.mock('pdfmake/build/pdfmake', () => ({ default: { addVirtualFileSystem: vi.fn(), createPdf: vi.fn() } }))
vi.mock('pdfmake/build/vfs_fonts', () => ({ default: { 'Roboto-Regular.ttf': 'AA' } }))

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
  ksefEnvironment: 'test',
  bankSwift: 'TESTPLPW',
  bankName: 'Bank Główny',
  foreignBankAccount: null,
  foreignBankSwift: null,
  foreignBankName: null,
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
  duplicateKsefNumber: null,
}

const invoice = (patch: Partial<IssuedInvoiceResponse> = {}): IssuedInvoiceResponse => ({
  id: 'i-1',
  status: 'Draft',
  number: null,
  seller: { nip: '5260250274', name: 'Sprzedawca', addressLine1: 'a', addressLine2: 'b', bankAccount: null, cashAccounting: false, iban: 'GB82WEST12345698765432', swift: 'TESTGB2L', bankName: 'Bank Próbny' },
  buyer: { kind: 'Foreign', nip: null, name: 'Example Ltd', addressLine1: '1 Sample Street', addressLine2: 'London', countryCode: 'GB', idType: 'Other', foreignId: 'GB-123' },
  issueDate: '2026-11-03',
  saleDate: '2026-11-03',
  dueDate: '2026-11-17',
  paymentMethod: 'Transfer',
  splitPayment: false,
  exemptionBasis: null,
  lines: [{ lineNo: 1, name: 'Usługa', unit: 'szt.', quantity: 1, unitNetPrice: 20000, vatRate: 'np I', netAmount: 20000, vatAmount: 0 }],
  vatSummary: [{ vatRate: 'np I', net: 20000, vat: 0 }],
  totalNet: 20000,
  totalVat: 0,
  totalGross: 20000,
  currency: 'GBP',
  ksef,
  warnings: [],
  createdAt: '2026-11-03T10:00:00Z',
  updatedAt: '2026-11-03T10:00:00Z',
  issuedAt: null,
  version: 5,
  exchangeRate: 5.1,
  exchangeRateDate: '2026-11-02',
  ...patch,
})

const printXml = `<?xml version="1.0" encoding="UTF-8"?>
<Faktura xmlns="http://crd.gov.pl/wzor/2025/06/25/13775/">
  <Podmiot1><DaneIdentyfikacyjne><NIP>5260250274</NIP><Nazwa>Sprzedawca</Nazwa></DaneIdentyfikacyjne><Adres><KodKraju>PL</KodKraju><AdresL1>a</AdresL1></Adres></Podmiot1>
  <Podmiot2><DaneIdentyfikacyjne><BrakID>1</BrakID><Nazwa>Example Ltd</Nazwa></DaneIdentyfikacyjne><Adres><KodKraju>GB</KodKraju><AdresL1>b</AdresL1></Adres></Podmiot2>
  <Fa><KodWaluty>GBP</KodWaluty><P_1>2026-11-03</P_1><P_2>FV/7/11/2026</P_2><P_15>100.00</P_15><Adnotacje><P_18>1</P_18></Adnotacje>
    <FaWiersz><NrWierszaFa>1</NrWierszaFa><P_7>Usługa</P_7><P_8A>szt.</P_8A><P_8B>1</P_8B><P_9A>100.00</P_9A><P_11>100.00</P_11><P_12>np I</P_12></FaWiersz></Fa>
</Faktura>`

const flush = () => new Promise<void>((resolve) => setTimeout(resolve, 0))

async function waitFor(assertion: () => boolean): Promise<void> {
  for (let i = 0; i < 100; i++) {
    if (assertion()) return
    await act(flush)
  }
  throw new Error('Condition not met')
}

function typeInto(element: HTMLInputElement, value: string): void {
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(element, value)
  element.dispatchEvent(new Event('input', { bubbles: true }))
}

function choose(element: HTMLSelectElement, value: string): void {
  Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')?.set?.call(element, value)
  element.dispatchEvent(new Event('change', { bubbles: true }))
}

const plain = (text: string | null | undefined): string => (text ?? '').replace(/\s/g, ' ')

const apiError = async (status: number, body: unknown) =>
  normalizeError(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

const envelope = (code: string, field: string, message: string) => ({ errors: [{ code, field, message }], warnings: [] })

describe('foreign invoices in the portal', () => {
  let root: Root
  let container: HTMLDivElement

  const byText = (text: string) => [...container.querySelectorAll('button')].find((button) => button.textContent?.includes(text))
  const buttonLabels = () => [...container.querySelectorAll('button')].map((button) => button.textContent?.trim() ?? '')
  const select = (id: string) => container.querySelector<HTMLSelectElement>(`#${id}`)!
  const input = (id: string) => container.querySelector<HTMLInputElement>(`#${id}`)!

  beforeEach(() => {
    vi.stubGlobal('localStorage', memoryStorage())
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
    vi.clearAllMocks()
    vi.mocked(listTenants).mockResolvedValue([
      { id: 't-1', nip: '5260250274', displayName: 'A', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' },
    ])
    vi.mocked(getSellerProfile).mockResolvedValue(profile)
    vi.mocked(getExchangeRateProposal).mockImplementation(async (_tenant, query) => ({ currency: query.currency, rate: 5.1234, tableDate: '2026-11-02' }))
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
            <CompanyProvider>
              <Routes>
                <Route path="/issued" element={<IssuedInvoiceListPage />} />
                <Route path="/issued/new" element={<InvoiceDraftPage />} />
                <Route path="/issued/:id/edit" element={<InvoiceDraftPage />} />
                <Route path="/issued/:id" element={<IssuedInvoiceDetailsPage />} />
                <Route path="/company/invoice-data" element={<SellerProfilePage />} />
              </Routes>
            </CompanyProvider>
          </MemoryRouter>
        </QueryClientProvider>,
      ),
    )
  }

  async function openForeignForm(country = 'GB'): Promise<void> {
    await render('/issued/new?tenantId=t-1')
    await waitFor(() => Boolean(byText('Firma zagraniczna')))
    await act(async () => byText('Firma zagraniczna')!.click())
    await act(async () => choose(select('buyer-countryCode'), country))
  }

  async function chooseCurrency(code: string): Promise<void> {
    await act(async () => choose(select('invoice-currency'), code))
    await waitFor(() => input('exchange-rate')?.value !== '' || container.textContent?.includes('Brak kursu NBP') === true)
  }

  const rateOptions = () => [...select('line-0-vatRate').options].map((option) => option.value)

  describe('T-498 form', () => {
    it('hides NIP lookup, requires country and offers only np rates', async () => {
      await openForeignForm()

      expect(container.querySelector('#buyer-nip')).toBeNull()
      expect(select('buyer-countryCode').getAttribute('aria-required')).toBe('true')
      expect(rateOptions()).toEqual(['np I', 'np II'])
      expect(container.textContent).not.toContain('Mechanizm podzielonej płatności')
    })

    it('does not offer np rates or foreign fields to Firma and Osoba prywatna', async () => {
      await render('/issued/new?tenantId=t-1')
      await waitFor(() => Boolean(container.querySelector('#buyer-nip')))

      expect(rateOptions()).toEqual(['23', '8', '5', '0 KR', 'zw'])
      expect(container.querySelector('#buyer-countryCode')).toBeNull()
      expect(container.querySelector('#invoice-currency')).toBeNull()

      await act(async () => byText('Osoba prywatna')!.click())
      expect(rateOptions()).toEqual(['23', '8', '5', '0 KR', 'zw'])
      expect(container.querySelector('#invoice-currency')).toBeNull()
    })

    it('switches the id field and the line rate with the country', async () => {
      await openForeignForm('GB')
      expect(container.querySelector('label[for="buyer-foreignId"]')?.textContent).toContain('Numer podatkowy')
      expect(container.textContent).toContain('Bez numeru podatkowego')
      expect(select('line-0-vatRate').value).toBe('np I')

      await act(async () => choose(select('buyer-countryCode'), 'DE'))

      expect(container.querySelector('label[for="buyer-foreignId"]')?.textContent).toContain('Numer VAT UE')
      expect(container.textContent).not.toContain('Bez numeru podatkowego')
      expect(select('line-0-vatRate').value).toBe('np II')

      await act(async () => byText('Dodaj pozycję')!.click())
      expect(select('line-1-vatRate').value).toBe('np II')
    })

    it('PLN has no rate field and no lookup; another currency shows the rate with its table date', async () => {
      await openForeignForm()
      expect(select('invoice-currency').value).toBe('PLN')
      expect(container.querySelector('#exchange-rate')).toBeNull()
      expect(getExchangeRateProposal).not.toHaveBeenCalled()

      await chooseCurrency('GBP')

      expect(vi.mocked(getExchangeRateProposal).mock.calls[0][1].currency).toBe('GBP')
      expect(input('exchange-rate').value).toBe('5,1234')
      expect(container.querySelector('#exchange-rate-source')?.textContent).toContain('02.11.2026')

      await act(async () => typeInto(input('exchange-rate'), '5,5'))
      expect(input('exchange-rate').value).toBe('5,5')
      expect(container.querySelector('#exchange-rate-source')?.textContent).toContain('wpisany ręcznie')
    })

    it('refreshes an NBP rate on a date or currency change and keeps a typed rate on a date change', async () => {
      await openForeignForm()
      await chooseCurrency('GBP')
      vi.mocked(getExchangeRateProposal).mockResolvedValue({ currency: 'GBP', rate: 5.2, tableDate: '2026-10-30' })

      await act(async () => typeInto(input('issue-date'), '2026-11-02'))
      await waitFor(() => input('exchange-rate').value === '5,2')
      expect(vi.mocked(getExchangeRateProposal).mock.lastCall?.[1]).toEqual({ currency: 'GBP', issueDate: '2026-11-02', saleDate: '2026-11-02' })
      expect(container.querySelector('#exchange-rate-source')?.textContent).toContain('30.10.2026')

      await act(async () => typeInto(input('exchange-rate'), '6'))
      vi.mocked(getExchangeRateProposal).mockResolvedValue({ currency: 'GBP', rate: 5.3, tableDate: '2026-10-29' })
      await act(async () => typeInto(input('sale-date'), '2026-10-30'))
      await waitFor(() => Boolean(byText('Użyj kursu NBP')))
      expect(input('exchange-rate').value).toBe('6')

      await act(async () => byText('Użyj kursu NBP')!.click())
      expect(input('exchange-rate').value).toBe('5,3')

      vi.mocked(getExchangeRateProposal).mockResolvedValue({ currency: 'USD', rate: 3.9, tableDate: '2026-10-29' })
      await act(async () => choose(select('invoice-currency'), 'USD'))
      await waitFor(() => input('exchange-rate').value === '3,9')
    })

    it('NBP failure leaves the rate empty and editable, and the draft saves', async () => {
      vi.mocked(getExchangeRateProposal).mockRejectedValue(new Error('down'))
      vi.mocked(createIssuedInvoice).mockResolvedValue(invoice({ exchangeRate: null, exchangeRateDate: null }))
      await openForeignForm()
      await act(async () => choose(select('invoice-currency'), 'GBP'))
      await waitFor(() => container.textContent?.includes('Brak kursu NBP') ?? false)

      expect(input('exchange-rate').value).toBe('')
      expect(input('exchange-rate').disabled).toBe(false)

      await act(async () => byText('Zapisz szkic')!.click())
      await waitFor(() => vi.mocked(createIssuedInvoice).mock.calls.length === 1)
      const request = vi.mocked(createIssuedInvoice).mock.calls[0][1]
      expect(request.currency).toBe('GBP')
      expect(request.exchangeRate).toBeNull()
    })

    it('sends the foreign fields, an emptied id as an empty string, and 0 for an emptied stored rate', async () => {
      vi.mocked(getIssuedInvoice).mockResolvedValue(invoice())
      vi.mocked(updateIssuedInvoice).mockResolvedValue(invoice())
      vi.mocked(getExchangeRateProposal).mockResolvedValue({ currency: 'GBP', rate: 5.1, tableDate: '2026-11-02' })
      await render('/issued/i-1/edit?tenantId=t-1')
      await waitFor(() => input('buyer-foreignId')?.value === 'GB-123')
      await waitFor(() => vi.mocked(getExchangeRateProposal).mock.calls.length === 1)

      await act(async () => byText('Zapisz szkic')!.click())
      await waitFor(() => vi.mocked(updateIssuedInvoice).mock.calls.length === 1)
      expect(vi.mocked(updateIssuedInvoice).mock.calls[0][2]).toMatchObject({
        buyer: { kind: 'Foreign', nip: null, countryCode: 'GB', foreignId: 'GB-123' },
        currency: 'GBP',
        exchangeRate: 5.1,
        exchangeRateDate: '2026-11-02',
        splitPayment: false,
      })

      const noId = container.querySelector<HTMLInputElement>('.inv-check input[type="checkbox"]')!
      await act(async () => noId.click())
      await act(async () => typeInto(input('exchange-rate'), ''))
      await act(async () => byText('Zapisz szkic')!.click())
      await waitFor(() => vi.mocked(updateIssuedInvoice).mock.calls.length === 2)
      const second = vi.mocked(updateIssuedInvoice).mock.calls[1][2]
      expect(second.buyer?.foreignId).toBe('')
      expect(second.exchangeRate).toBe(0)
      expect(second.exchangeRateDate).toBeNull()
    })

    it('going back to Firma resets currency, rate and np lines', async () => {
      vi.mocked(createIssuedInvoice).mockResolvedValue(invoice())
      await openForeignForm()
      await chooseCurrency('GBP')

      await act(async () => [...container.querySelectorAll('button')].find((button) => button.textContent === 'Firma')!.click())

      expect(container.querySelector('#buyer-nip')).not.toBeNull()
      expect(select('line-0-vatRate').value).toBe('23')
      expect(container.querySelector('#invoice-currency')).toBeNull()
      expect(plain(container.querySelector('[data-testid="total-gross"]')?.textContent)).toBe('0,00 PLN')

      await act(async () => byText('Zapisz szkic')!.click())
      await waitFor(() => vi.mocked(createIssuedInvoice).mock.calls.length === 1)
      const request = vi.mocked(createIssuedInvoice).mock.calls[0][1]
      expect(request.currency).toBe('PLN')
      expect(request.exchangeRate).toBeNull()
      expect(request.buyer).not.toHaveProperty('countryCode')
      expect(request.buyer).not.toHaveProperty('foreignId')
    })

    it('blocks issuing a third-country buyer without an id or the no-id tick', async () => {
      await openForeignForm()
      await act(async () => byText('Wystaw i wyślij do KSeF')!.click())

      expect(container.querySelector('#buyer-foreignId-error')?.textContent).toContain('Bez numeru podatkowego')
      expect(createIssuedInvoice).not.toHaveBeenCalled()
    })

    it.each(['test', 'production'] as const)('blocks issuing on %s while the rate is empty, and the rate is on screen before anything is sent', async (environment) => {
      const draft = invoice({ ksef: { ...ksef, environment } })
      vi.mocked(getSellerProfile).mockResolvedValue({ ...profile, ksefEnvironment: environment })
      vi.mocked(getIssuedInvoice).mockResolvedValue(draft)
      vi.mocked(updateIssuedInvoice).mockResolvedValue({ ...draft, exchangeRate: 5.1234 })
      vi.mocked(issueIssuedInvoice).mockResolvedValue(invoice({ status: 'Sending' }))
      await render('/issued/i-1/edit?tenantId=t-1')
      await waitFor(() => input('exchange-rate')?.value === '5,1234')

      await act(async () => typeInto(input('exchange-rate'), ''))
      await act(flush)
      expect(input('exchange-rate').value).toBe('')
      await act(async () => byText('Wystaw i wyślij do KSeF')!.click())
      await act(flush)

      expect(container.querySelector('#exchange-rate-error')?.textContent).toContain('Podaj kurs waluty')
      expect(updateIssuedInvoice).not.toHaveBeenCalled()
      expect(issueIssuedInvoice).not.toHaveBeenCalled()

      await act(async () => byText('Użyj kursu NBP')!.click())
      expect(input('exchange-rate').value).toBe('5,1234')
      await act(async () => byText('Wystaw i wyślij do KSeF')!.click())
      await waitFor(() => vi.mocked(updateIssuedInvoice).mock.calls.length === 1)
      expect(vi.mocked(updateIssuedInvoice).mock.calls[0][2].exchangeRate).toBe(5.1234)
    })

    it('a reopened third-country draft without an id needs a new tick before issuing', async () => {
      vi.mocked(getIssuedInvoice).mockResolvedValue(
        invoice({ buyer: { kind: 'Foreign', nip: null, name: 'Example Ltd', addressLine1: '1 Sample Street', addressLine2: 'London', countryCode: 'GB', idType: 'None', foreignId: null } }),
      )
      await render('/issued/i-1/edit?tenantId=t-1')
      await waitFor(() => Boolean(container.querySelector('#buyer-foreignId')))
      expect(container.querySelector<HTMLInputElement>('.inv-check input[type="checkbox"]')?.checked).toBe(false)

      await act(async () => byText('Wystaw i wyślij do KSeF')!.click())

      expect(container.querySelector('#buyer-foreignId-error')?.textContent).toContain('Bez numeru podatkowego')
      expect(updateIssuedInvoice).not.toHaveBeenCalled()
      expect(issueIssuedInvoice).not.toHaveBeenCalled()
    })

    it('tells the user to enter buyer data in Latin script', async () => {
      await openForeignForm()
      expect(container.textContent).toContain('alfabetem łacińskim')
    })

    it('a non-PLN Person draft keeps the currency control, so INV-VAL-091 can be fixed', async () => {
      vi.mocked(getIssuedInvoice).mockResolvedValue(
        invoice({ buyer: { kind: 'Person', nip: null, name: 'Jan Kowalski', addressLine1: 'ul. A 1', addressLine2: null } }),
      )
      vi.mocked(updateIssuedInvoice).mockResolvedValue(invoice())
      await render('/issued/i-1/edit?tenantId=t-1')
      await waitFor(() => Boolean(container.querySelector('#invoice-currency')))

      await act(async () => choose(select('invoice-currency'), 'PLN'))
      expect(container.querySelector('#invoice-currency')).toBeNull()

      await act(async () => byText('Zapisz szkic')!.click())
      await waitFor(() => vi.mocked(updateIssuedInvoice).mock.calls.length === 1)
      expect(vi.mocked(updateIssuedInvoice).mock.calls[0][2].currency).toBe('PLN')
    })

    it.each([
      ['INV-VAL-030', 'buyer.countryCode', '#buyer-countryCode-error'],
      ['INV-VAL-031', 'buyer.foreignId', '#buyer-foreignId-error'],
      ['INV-VAL-031', 'buyer.nip', '#buyer-foreignId-error'],
      ['INV-VAL-032', 'buyer.vatUe', '#buyer-foreignId-error'],
      ['INV-VAL-092', 'exchangeRate', '#exchange-rate-error'],
      ['INV-VAL-094', 'exchangeRate', '#exchange-rate-error'],
      ['INV-VAL-091', 'currency', '#invoice-currency-error'],
      ['INV-VAL-062', 'lines[0].vatRate', '#line-0-vatRate-error'],
    ])('shows server error %s on %s at the field', async (code, field, selector) => {
      vi.mocked(createIssuedInvoice).mockRejectedValue(await apiError(400, envelope(code, field, `Błąd ${code}`)))
      await openForeignForm()
      await chooseCurrency('GBP')

      await act(async () => byText('Zapisz szkic')!.click())
      await waitFor(() => container.querySelector(selector) !== null)

      expect(container.querySelector(selector)?.textContent).toBe(`Błąd ${code}`)
    })

    it('shows GBP on form, line, summary and dock, and no MPP warning above 15 000', async () => {
      await openForeignForm()
      await chooseCurrency('GBP')
      await act(async () => typeInto(input('line-0-unitNetPrice'), '20000'))

      expect(plain(container.querySelector('[data-testid="total-gross"]')?.textContent)).toBe('20 000,00 GBP')
      expect(plain(container.querySelector('[data-testid="line-0"]')?.textContent)).toContain('20 000,00 GBP')
      expect(plain(container.querySelector('.inv-dock')?.textContent)).toContain('20 000,00 GBP')
      expect(container.querySelector('.inv-vat')?.textContent).toContain('Kwoty w GBP')
      expect(container.textContent).not.toContain('podzielonej płatności')
      expect(plain(container.querySelector('.inv-totals')?.textContent)).not.toContain('PLN')
    })

    it('shows the IBAN built from the main account, SWIFT and bank', async () => {
      await openForeignForm()
      expect(plain(container.querySelector('.inv-account')?.textContent)).toContain('PL61 1090 1014 0000 0712 1981 2874')
      expect(plain(container.querySelector('.inv-account')?.textContent)).toContain('SWIFT TESTPLPW')
      expect(plain(container.querySelector('.inv-account')?.textContent)).toContain('Bank Główny')
    })

    it('uses the foreign account from the profile when set', async () => {
      vi.mocked(getSellerProfile).mockResolvedValue({ ...profile, foreignBankAccount: 'GB82WEST12345698765432', foreignBankSwift: 'TESTGB2L', foreignBankName: 'Bank Próbny' })
      await openForeignForm()
      expect(plain(container.querySelector('.inv-account')?.textContent)).toContain('GB82 WEST 1234 5698 7654 32')
      expect(plain(container.querySelector('.inv-account')?.textContent)).toContain('SWIFT TESTGB2L')
    })

    it('the confirmation shows the currency, country, id and rate', async () => {
      vi.mocked(getSellerProfile).mockResolvedValue({ ...profile, ksefEnvironment: 'production' })
      const saved = invoice({ ksef: { ...ksef, environment: 'production' } })
      vi.mocked(getIssuedInvoice).mockResolvedValue(saved)
      vi.mocked(updateIssuedInvoice).mockResolvedValue(saved)
      vi.mocked(issueIssuedInvoice).mockResolvedValue(saved)
      await render('/issued/i-1/edit?tenantId=t-1')
      await waitFor(() => input('buyer-foreignId')?.value === 'GB-123')

      await act(async () => byText('Wystaw i wyślij do KSeF')!.click())
      await waitFor(() => Boolean(container.querySelector('[role="alertdialog"]')))

      const dialog = plain(container.querySelector('[role="alertdialog"]')?.textContent)
      expect(dialog).toContain('20 000,00 GBP')
      expect(dialog).toContain('Wielka Brytania')
      expect(dialog).toContain('GB-123')
      expect(dialog).toContain('1 GBP = 5,10 PLN')
    })

    it('details show GBP, country, id, rate and the foreign account', async () => {
      vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Sending', number: 'FV/7/11/2026' }))
      await render('/issued/i-1?tenantId=t-1')
      await waitFor(() => container.textContent?.includes('Example Ltd') ?? false)

      const text = plain(container.textContent)
      expect(text).toContain('20 000,00 GBP')
      expect(text).toContain('Wielka Brytania')
      expect(text).toContain('Numer podatkowy')
      expect(text).toContain('GB-123')
      expect(text).toContain('1 GBP = 5,10 PLN (NBP z 02.11.2026)')
      expect(text).toContain('GB82 WEST 1234 5698 7654 32')
      expect(text).toContain('TESTGB2L')
      expect(text).toContain('np I')
      expect(text.replace('5,10 PLN', '')).not.toContain('PLN')
    })

    it('the list shows the row currency', async () => {
      vi.mocked(listCredentials).mockResolvedValue([])
      vi.mocked(listIssuedFeed).mockResolvedValue({
        items: [
          {
            id: 'i-1',
            source: 'OpenKSeF',
            status: 'Accepted',
            number: 'FV/7/11/2026',
            buyerName: 'Example Ltd',
            buyerNip: null,
            buyerIdType: 'Other',
            buyerForeignId: 'GB-123',
            buyerCountryCode: 'GB',
            issueDate: '2026-11-03',
            dueDate: '2026-11-17',
            totalNet: 100,
            totalGross: 100,
            currency: 'GBP',
            ksefNumber: 'K-1',
            needsAttention: false,
          },
        ],
        page: 1,
        pageSize: 20,
        totalCount: 1,
        totalPages: 1,
      })
      await render('/issued?tenantId=t-1')
      await waitFor(() => container.textContent?.includes('Example Ltd') ?? false)

      expect(plain(container.textContent)).toContain('100,00 GBP')
      expect(plain(container.textContent)).not.toContain('PLN')
    })

    it('seller profile saves the foreign account and shows its errors at the field', async () => {
      vi.mocked(updateSellerProfile).mockRejectedValueOnce(await apiError(400, envelope('SP-VAL-008', 'foreignBankAccount', 'Niepoprawny numer IBAN rachunku zagranicznego')))
      vi.mocked(updateSellerProfile).mockResolvedValue(profile)
      await render('/company/invoice-data?tenantId=t-1')
      await waitFor(() => Boolean(container.querySelector('#foreign-bank-account')))
      expect(input('bank-swift').value).toBe('TESTPLPW')

      await act(async () => typeInto(input('foreign-bank-account'), 'GB00'))
      await act(async () => typeInto(input('foreign-bank-swift'), 'TESTGB2L'))
      await act(async () => typeInto(input('foreign-bank-name'), 'Bank Próbny'))
      await act(async () => typeInto(input('bank-name'), ''))
      await act(async () => byText('Zapisz dane')!.click())
      await waitFor(() => container.querySelector('#foreign-bank-account-error') !== null)

      expect(container.querySelector('#foreign-bank-account-error')?.textContent).toContain('IBAN')
      expect(vi.mocked(updateSellerProfile).mock.calls[0][1]).toMatchObject({
        bankSwift: 'TESTPLPW',
        bankName: '',
        foreignBankAccount: 'GB00',
        foreignBankSwift: 'TESTGB2L',
        foreignBankName: 'Bank Próbny',
      })
    })
  })

  describe('T-502 print buttons', () => {
    const verificationUrl = 'https://qr-test.ksef.mf.gov.pl/invoice/x'
    const accepted = (patch: Partial<IssuedInvoiceResponse> = {}) =>
      invoice({ status: 'Accepted', number: 'FV/7/11/2026', ksef: { ...ksef, ksefNumber: 'K-1', verificationUrl }, ...patch })
    const pdfLabels = () => buttonLabels().filter((label) => label.includes('Pobierz PDF'))

    function captureDownloads(): { files: string[]; types: string[]; restore: () => void } {
      const files: string[] = []
      const types: string[] = []
      Object.assign(URL, {
        createObjectURL: (blob: Blob) => {
          types.push(blob.type)
          return 'blob:x'
        },
        revokeObjectURL: vi.fn(),
      })
      const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
        files.push(this.download)
      })
      return { files, types, restore: () => click.mockRestore() }
    }

    it('AC1: an accepted foreign invoice has EN/PL as primary, PL/EN and the MF layout', async () => {
      vi.mocked(getIssuedInvoice).mockResolvedValue(accepted())
      await render('/issued/i-1?tenantId=t-1')
      await waitFor(() => Boolean(byText('Pobierz PDF (EN/PL)')))

      expect(pdfLabels()).toEqual(['Pobierz PDF (EN/PL)', 'Pobierz PDF (PL/EN)', 'Pobierz PDF (wzór MF)'])
      expect(byText('Pobierz PDF (EN/PL)')?.className).toContain('ui-button--primary')
      expect(byText('Pobierz PDF (PL/EN)')?.className).toContain('ui-button--outline')
      expect(byText('Pobierz PDF (wzór MF)')?.className).toContain('ui-button--outline')
    })

    it.each([
      ['Pobierz PDF (EN/PL)', 'FV_7_11_2026_EN-PL.pdf', 'Invoice no. FV/7/11/2026'],
      ['Pobierz PDF (PL/EN)', 'FV_7_11_2026_PL-EN.pdf', 'Faktura nr FV/7/11/2026'],
    ])('AC2, AC3: %s saves %s from the stored XML', async (label, fileName, title) => {
      vi.mocked(getIssuedInvoice).mockResolvedValue(accepted())
      vi.mocked(getIssuedInvoiceXml).mockResolvedValue(printXml)
      vi.mocked(pdfMake.createPdf).mockReturnValue({ getBlob: async () => new Blob(['%PDF'], { type: 'application/pdf' }) })
      const downloads = captureDownloads()
      await render('/issued/i-1?tenantId=t-1')
      await waitFor(() => Boolean(byText(label)))

      await act(async () => byText(label)!.click())
      await waitFor(() => downloads.files.length === 1)
      downloads.restore()

      expect(downloads.files).toEqual([fileName])
      expect(downloads.types).toEqual(['application/pdf'])
      expect(getIssuedInvoiceXml).toHaveBeenCalledWith('t-1', 'i-1')
      expect(generateInvoice).not.toHaveBeenCalled()
      const document = JSON.stringify(vi.mocked(pdfMake.createPdf).mock.calls[0][0])
      expect(document.indexOf(title)).toBeGreaterThan(-1)
      expect(document).toContain(`"qr":"${verificationUrl}"`)
      expect(updateIssuedInvoice).not.toHaveBeenCalled()
    })

    it('the MF button calls the MF generator with the same input as before', async () => {
      vi.mocked(getIssuedInvoice).mockResolvedValue(accepted())
      vi.mocked(getIssuedInvoiceXml).mockResolvedValue(printXml)
      vi.mocked(generateInvoice).mockResolvedValue(new Blob(['%PDF']))
      const downloads = captureDownloads()
      await render('/issued/i-1?tenantId=t-1')
      await waitFor(() => Boolean(byText('Pobierz PDF (wzór MF)')))

      await act(async () => byText('Pobierz PDF (wzór MF)')!.click())
      await waitFor(() => downloads.files.length === 1)
      downloads.restore()

      expect(downloads.files).toEqual(['FV_7_11_2026.pdf'])
      expect(vi.mocked(generateInvoice).mock.calls[0][1]).toEqual({ nrKSeF: 'K-1', qrCode: verificationUrl })
      expect(pdfMake.createPdf).not.toHaveBeenCalled()
    })

    it('a broken XML shows the error instead of a file', async () => {
      vi.mocked(getIssuedInvoice).mockResolvedValue(accepted())
      vi.mocked(getIssuedInvoiceXml).mockResolvedValue('<x/>')
      await render('/issued/i-1?tenantId=t-1')
      await waitFor(() => Boolean(byText('Pobierz PDF (EN/PL)')))

      await act(async () => byText('Pobierz PDF (EN/PL)')!.click())
      await waitFor(() => container.textContent?.includes('Nie udało się odczytać XML faktury') ?? false)
    })

    it('AC10: a domestic accepted invoice keeps one Pobierz PDF', async () => {
      vi.mocked(getIssuedInvoice).mockResolvedValue(
        accepted({
          buyer: { kind: 'Company', nip: '5260250274', name: 'Nabywca', addressLine1: 'a', addressLine2: 'b' },
          currency: 'PLN',
          exchangeRate: null,
          exchangeRateDate: null,
        }),
      )
      await render('/issued/i-1?tenantId=t-1')
      await waitFor(() => Boolean(byText('Pobierz PDF')))

      expect(pdfLabels()).toEqual(['Pobierz PDF'])
    })

    it.each([
      ['Sending', { ksefNumber: null, verificationUrl: null }],
      ['Rejected', { ksefNumber: null, verificationUrl: null, statusCode: 450, statusMessage: 'Odrzucona' }],
    ] as const)('AC11: a foreign invoice in %s has no PDF button', async (status, patch) => {
      vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status, number: 'FV/7/11/2026', ksef: { ...ksef, ...patch } }))
      await render('/issued/i-1?tenantId=t-1')
      await waitFor(() => container.textContent?.includes('Example Ltd') ?? false)

      expect(pdfLabels()).toEqual([])
    })

    it('an accepted foreign invoice without KOD I has no PDF button', async () => {
      vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Accepted', number: 'FV/7/11/2026' }))
      await render('/issued/i-1?tenantId=t-1')
      await waitFor(() => container.textContent?.includes('Example Ltd') ?? false)

      expect(pdfLabels()).toEqual([])
    })
  })
})
