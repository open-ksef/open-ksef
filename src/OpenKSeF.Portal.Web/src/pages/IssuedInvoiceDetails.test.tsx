// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  createIssuedInvoice,
  getIssuedInvoice,
  getIssuedInvoiceXml,
  issueIssuedInvoice,
} from '@/api/endpoints/issuedInvoices'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { listTenants } from '@/api/endpoints/tenants'
import { normalizeError } from '@/api/errors'
import type { IssuedInvoiceResponse, SellerProfileResponse } from '@/api/types'
import { ksefPollInterval } from '@/invoicing/status'
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
}))

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
    expect(container.querySelector('[role="alertdialog"]')?.textContent).toContain('dokumentem prawnym')
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

  it('Rejected shows the KSeF message and the edit link', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(
      invoice({ status: 'Rejected', ksef: { ...ksef, statusCode: 450, statusMessage: 'Błąd weryfikacji semantyki' } }),
    )
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes('Kod KSeF 450: Błąd weryfikacji semantyki') ?? false)

    expect(container.querySelector('a[href*="/issued/i-1/edit"]')?.textContent).toContain('Popraw i wyślij ponownie')
    expect(buttonByText(container, 'Pobierz XML')).toBeUndefined()
  })

  it('Sending with lastError warns not to resend', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Sending', ksef: { ...ksef, lastError: 'Timeout' } }))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes('KSeF nie odpowiada') ?? false)
    expect(container.textContent).toContain('Nie wysyłaj faktury drugi raz')
    expect(container.textContent).toContain('Timeout')
  })
})
