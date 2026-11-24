// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  convertProforma,
  copyIssuedInvoice,
  createIssuedInvoice,
  deleteIssuedInvoice,
  getIssuedInvoice,
  issueIssuedInvoice,
} from '@/api/endpoints/issuedInvoices'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { listTenants } from '@/api/endpoints/tenants'
import type { IssuedInvoiceResponse, SellerProfileResponse } from '@/api/types'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { InvoiceDraftPage } from './InvoiceDraft'
import { IssuedInvoiceDetailsPage } from './IssuedInvoiceDetails'
import { ownerAccess } from '@/test/tenant'

vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))
vi.mock('@/api/endpoints/sellerProfile', () => ({ getSellerProfile: vi.fn() }))
vi.mock('@/api/endpoints/nipLookup', () => ({ lookupNip: vi.fn() }))
vi.mock('@/api/endpoints/issuedInvoices', () => ({
  convertProforma: vi.fn(),
  createIssuedInvoice: vi.fn(),
  updateIssuedInvoice: vi.fn(),
  getIssuedInvoice: vi.fn(),
  issueIssuedInvoice: vi.fn(),
  getIssuedInvoiceXml: vi.fn(),
  getIssuedInvoiceUpo: vi.fn(),
  getExchangeRateProposal: vi.fn(),
  copyIssuedInvoice: vi.fn(),
  deleteIssuedInvoice: vi.fn(),
  saveAsTemplate: vi.fn(),
  listRecentBuyers: vi.fn(),
  listLineSuggestions: vi.fn(),
}))

const profile: SellerProfileResponse = {
  legalName: 'Sprzedawca',
  addressLine1: 'ul. Prosta 1',
  addressLine2: '00-001 Warszawa',
  bankAccount: '93000000002026111700000815',
  cashAccounting: false,
  numberingPattern: 'FV/{nr}/{MM}/{RRRR}',
  nextNumber: 1,
  nextNumberPeriod: '2026-11',
  nip: '4444444444',
  isComplete: true,
  nextNumberPreview: 'FV/1/11/2026',
  ksefEnvironment: 'production',
  proformaNumberingPattern: 'PRO/{nr}/{MM}/{RRRR}',
  proformaNextNumber: 1,
  proformaNextNumberPeriod: '2026-11',
  proformaNextNumberPreview: 'PRO/1/11/2026',
}

const proforma = (patch: Partial<IssuedInvoiceResponse> = {}): IssuedInvoiceResponse => ({
  id: 'p-1',
  status: 'Issued',
  invoiceType: 'PRO',
  number: 'PRO/1/11/2026',
  seller: { nip: '4444444444', name: 'Sprzedawca', addressLine1: 'a', addressLine2: 'b', bankAccount: '93000000002026111700000815', cashAccounting: false },
  buyer: { kind: 'Company', nip: '7740001454', name: 'Nabywca', addressLine1: 'a', addressLine2: 'b' },
  issueDate: '2026-11-24',
  saleDate: '2026-11-24',
  dueDate: '2026-12-08',
  paymentMethod: 'Transfer',
  splitPayment: false,
  exemptionBasis: null,
  lines: [{ lineNo: 1, name: 'Usługa', unit: 'szt.', quantity: 1, unitNetPrice: 100, vatRate: '23', netAmount: 100, vatAmount: 23 }],
  vatSummary: [{ vatRate: '23', net: 100, vat: 23 }],
  totalNet: 100,
  totalVat: 23,
  totalGross: 123,
  currency: 'PLN',
  ksef: {
    environment: 'production',
    ksefNumber: null,
    acceptedAt: null,
    statusCode: null,
    statusMessage: null,
    statusDetails: null,
    lastError: null,
    verificationUrl: null,
    hasUpo: false,
    duplicateKsefNumber: null,
    acceptedOn: null,
    upoUnavailable: false,
  },
  warnings: [],
  createdAt: '2026-11-24T10:00:00Z',
  updatedAt: '2026-11-24T10:00:00Z',
  issuedAt: '2026-11-24T10:00:00Z',
  version: 2,
  sellerVersion: 1,
  ...patch,
})

const vatDraft = proforma({ id: 'v-1', invoiceType: 'VAT', status: 'Draft', number: null, issuedAt: null, proforma: { id: 'p-1', number: 'PRO/1/11/2026', invoiceType: 'PRO', status: 'Issued', issueDate: '2026-11-24', totalGross: 123 } })
const linked = { id: 'v-1', number: null, invoiceType: 'VAT' as const, status: 'Draft' as const, issueDate: '2026-11-24', totalGross: 123 }

const flush = () => new Promise<void>((resolve) => setTimeout(resolve, 0))

describe('proforma pages', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.stubGlobal('localStorage', memoryStorage())
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
    vi.clearAllMocks()
    vi.mocked(listTenants).mockResolvedValue([
      { id: 't-1', nip: '4444444444', displayName: 'A', notificationEmail: null, ...ownerAccess, createdAt: '2026-01-01T00:00:00Z' },
    ])
    vi.mocked(getSellerProfile).mockResolvedValue(profile)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  async function waitFor(assertion: () => boolean): Promise<void> {
    for (let i = 0; i < 100; i++) {
      if (assertion()) return
      await act(flush)
    }
    throw new Error(`Condition not met: ${container.textContent}`)
  }

  const button = (text: string) => [...container.querySelectorAll('button')].find((item) => item.textContent?.includes(text))
  const link = (text: string) => [...container.querySelectorAll('a')].find((item) => item.textContent?.includes(text))

  async function render(path: string): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    await act(async () =>
      root.render(
        <QueryClientProvider client={client}>
          <MemoryRouter initialEntries={[path]}>
            <CompanyProvider>
              <Routes>
                <Route path="/issued" element={<p>Lista</p>} />
                <Route path="/issued/new" element={<InvoiceDraftPage />} />
                <Route path="/issued/:id/edit" element={<InvoiceDraftPage />} />
                <Route path="/issued/:id" element={<IssuedInvoiceDetailsPage />} />
              </Routes>
            </CompanyProvider>
          </MemoryRouter>
        </QueryClientProvider>,
      ),
    )
  }

  it('Nowa proforma saves as PRO and issues without the KSeF dialog', async () => {
    const draft = proforma({ status: 'Draft', number: null, issuedAt: null })
    vi.mocked(createIssuedInvoice).mockResolvedValue(draft)
    vi.mocked(getIssuedInvoice).mockResolvedValue(draft)
    vi.mocked(issueIssuedInvoice).mockImplementation(async () => {
      vi.mocked(getIssuedInvoice).mockResolvedValue(proforma())
      return proforma()
    })
    await render('/issued/new?kind=PRO&tenantId=t-1')
    await waitFor(() => Boolean(button('Wystaw proformę')))

    expect(container.querySelector('h1')?.textContent).toBe('Nowa proforma')
    expect(container.textContent).not.toContain('KSeF')
    expect(container.textContent).not.toContain('PRODUKCJA')
    expect(container.textContent).toContain('PRO/1/11/2026')
    expect(button('Zapisz jako szablon')).toBeUndefined()

    await act(async () => button('Wystaw proformę')!.click())
    await waitFor(() => Boolean(container.querySelector('[data-testid="proforma-panel"]')))

    expect(container.querySelector('[role="alertdialog"]')).toBeNull()
    expect(vi.mocked(createIssuedInvoice).mock.calls[0][2]).toBe('PRO')
    expect(vi.mocked(issueIssuedInvoice).mock.calls[0]).toEqual(['t-1', 'p-1', 2, 1, undefined, 'PRO'])
  })

  it('a foreign-currency proforma has no exchange-rate field', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(proforma({ status: 'Draft', number: null, currency: 'EUR', buyer: { kind: 'Foreign', nip: null, name: 'Buyer', addressLine1: 'a', addressLine2: null, countryCode: 'DE', foreignId: 'DE123' } }))
    await render('/issued/p-1/edit?tenantId=t-1')
    await waitFor(() => Boolean(container.querySelector('#invoice-currency')))

    expect(container.querySelector('#exchange-rate')).toBeNull()
    expect(container.querySelector('h1')?.textContent).toBe('Szkic proformy')
  })

  it('issued proforma: PDF, Utwórz fakturę, copy as proforma and delete; no KSeF controls', async () => {
    vi.mocked(getIssuedInvoice).mockImplementation(async (_tenant, id) => (id === 'v-1' ? vatDraft : proforma()))
    vi.mocked(convertProforma).mockResolvedValue(vatDraft)
    await render('/issued/p-1?tenantId=t-1')
    await waitFor(() => Boolean(container.querySelector('[data-testid="proforma-panel"]')))

    const text = container.textContent ?? ''
    expect(text).toContain('Wystawiona')
    expect(text).not.toMatch(/wyślij|Wyślij|Numer KSeF|PRODUKCJA|Pobierz XML|Pobierz UPO|KOD I|Koryguj/)
    expect(button('Pobierz PDF')).toBeDefined()
    expect(button('Usuń proformę')).toBeDefined()
    expect(button('Kopiuj jako nową')).toBeDefined()

    await act(async () => button('Utwórz fakturę')!.click())
    await waitFor(() => Boolean(container.querySelector('[data-testid="from-proforma"]')))

    expect(convertProforma).toHaveBeenCalledWith('t-1', 'p-1')
    expect(container.querySelector('#buyer-name')).not.toBeNull()
    expect(container.querySelector('[data-testid="from-proforma"]')?.textContent).toContain('PRO/1/11/2026')
  })

  it('converted proforma opens the linked draft and offers no delete', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(proforma({ convertedTo: linked }))
    await render('/issued/p-1?tenantId=t-1')
    await waitFor(() => Boolean(container.querySelector('[data-testid="proforma-panel"]')))

    expect(container.textContent).toContain('Przekształcona')
    expect(link('Otwórz fakturę')?.getAttribute('href')).toBe('/issued/v-1/edit?tenantId=t-1')
    expect(button('Utwórz fakturę')).toBeUndefined()
    expect(button('Usuń proformę')).toBeUndefined()
    expect(convertProforma).not.toHaveBeenCalled()
  })

  it('Kopiuj jako nową on a proforma asks for a PRO copy', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(proforma())
    vi.mocked(copyIssuedInvoice).mockResolvedValue(proforma({ id: 'p-2', status: 'Draft', number: null }))
    await render('/issued/p-1?tenantId=t-1')
    await waitFor(() => Boolean(button('Kopiuj jako nową')))

    await act(async () => button('Kopiuj jako nową')!.click())
    await waitFor(() => vi.mocked(copyIssuedInvoice).mock.calls.length === 1)
    expect(copyIssuedInvoice).toHaveBeenCalledWith('t-1', 'p-1', 'PRO')
  })

  it('Usuń proformę deletes after confirmation and returns to the Proformy view', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(proforma())
    vi.mocked(deleteIssuedInvoice).mockResolvedValue()
    await render('/issued/p-1?tenantId=t-1')
    await waitFor(() => Boolean(button('Usuń proformę')))

    await act(async () => button('Usuń proformę')!.click())
    const dialog = container.querySelector<HTMLElement>('[role="alertdialog"]')!
    expect(dialog.textContent).toContain('Usunąć proformę?')
    await act(async () => [...dialog.querySelectorAll('button')].find((item) => item.textContent === 'Usuń proformę')!.click())
    await waitFor(() => container.textContent === 'Lista')
    expect(deleteIssuedInvoice).toHaveBeenCalledWith('t-1', 'p-1', 2)
  })
})
