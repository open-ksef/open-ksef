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
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { formatMoney } from '@/invoicing/format'
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
  duplicateKsefNumber: null,
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

  it('PRODUKCJA asks for confirmation, then saves, issues and opens the detail page', async () => {
    const draft = invoice({
      number: null,
      seller: { nip: '7740001454', name: 'Sprzedawca z zapisanej faktury', addressLine1: 'ul. A 1', addressLine2: '00-001 B', bankAccount: null, cashAccounting: false },
      ksef: { ...ksef, environment: 'production' },
    })
    vi.mocked(createIssuedInvoice).mockResolvedValue(draft)
    vi.mocked(getIssuedInvoice).mockResolvedValue(draft)
    vi.mocked(issueIssuedInvoice).mockImplementation(async () => {
      vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Sending' }))
      return invoice({ status: 'Sending' })
    })
    await render('/issued/new?tenantId=t-1')
    await waitFor(() => Boolean(buttonByText(container, 'Wystaw i wyślij do KSeF')))

    await act(async () => buttonByText(container, 'Wystaw i wyślij do KSeF')!.click())
    await waitFor(() => Boolean(container.querySelector('[role="alertdialog"]')))
    const dialog = container.querySelector('[role="alertdialog"]')?.textContent
    expect(dialog).toContain('Środowisko PRODUKCJA')
    expect(dialog).toContain('PRODUKCJA')
    const seller = container.querySelector('[data-testid="confirm-seller"]')
    expect(seller?.textContent).toContain('Sprzedawca z zapisanej faktury')
    expect(seller?.textContent).toContain('NIP 7740001454')
    expect(seller?.textContent).toContain('ul. A 1, 00-001 B')
    expect(dialog?.indexOf('Sprzedawca')).toBeLessThan(dialog?.indexOf('Nabywca') ?? -1)
    expect(dialog).toContain('Nabywca')
    expect(container.querySelector('[data-testid="confirm-total"]')?.textContent?.replace(/\s/g, ' ')).toBe(
      `${formatMoney(draft.totalGross, draft.currency).replace(/\s/g, ' ')}`,
    )
    expect(dialog).toContain('dokumentem prawnym')
    expect(dialog).toContain('Nadamy przy wystawieniu')
    expect(dialog).not.toContain('FV/1/11/2026')
    expect(issueIssuedInvoice).not.toHaveBeenCalled()

    await act(async () => buttonByText(container.querySelector<HTMLElement>('[role="alertdialog"]')!, 'Wystaw i wyślij')!.click())
    await waitFor(() => container.textContent?.includes('czekamy na przyjęcie') ?? false)
    expect(vi.mocked(issueIssuedInvoice).mock.calls[0]).toEqual(['t-1', 'i-1'])
  })

  const envKsef = (environment: unknown) => ({ ...ksef, environment }) as IssuedInvoiceResponse['ksef']
  const clickIssue = async () => {
    await waitFor(() => Boolean(buttonByText(container, 'Wystaw i wyślij do KSeF')))
    await act(async () => buttonByText(container, 'Wystaw i wyślij do KSeF')!.click())
  }
  const dialogEl = () => container.querySelector<HTMLElement>('[role="alertdialog"]')

  it.each(['production', null, undefined, 'staging', 'TEST'])('saved environment %s asks for confirmation although the profile says test', async (environment) => {
    vi.mocked(getSellerProfile).mockResolvedValue({ ...profile, ksefEnvironment: 'test' })
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ number: null, ksef: envKsef('test') }))
    vi.mocked(updateIssuedInvoice).mockResolvedValue(invoice({ number: null, ksef: envKsef(environment) }))
    await render('/issued/i-1/edit?tenantId=t-1')

    await clickIssue()
    await waitFor(() => Boolean(dialogEl()))
    expect(dialogEl()?.textContent).toContain(environment === 'production' ? 'Środowisko PRODUKCJA' : 'Nie znamy środowiska KSeF')
    expect(issueIssuedInvoice).not.toHaveBeenCalled()

    await act(async () => buttonByText(dialogEl()!, 'Wróć do edycji')!.click())
    expect(dialogEl()).toBeNull()
    expect(issueIssuedInvoice).not.toHaveBeenCalled()
  })

  it.each(['test', 'demo'])('saved environment %s issues without a dialog although the profile says production', async (environment) => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ number: null }))
    vi.mocked(updateIssuedInvoice).mockResolvedValue(invoice({ number: null, ksef: envKsef(environment) }))
    vi.mocked(issueIssuedInvoice).mockResolvedValue(invoice({ status: 'Sending' }))
    await render('/issued/i-1/edit?tenantId=t-1')

    await clickIssue()
    await waitFor(() => vi.mocked(issueIssuedInvoice).mock.calls.length === 1)
    expect(dialogEl()).toBeNull()
  })

  it('Rejected resend asks for confirmation and names its own number', async () => {
    vi.mocked(getSellerProfile).mockResolvedValue({ ...profile, ksefEnvironment: 'test' })
    const rejected = invoice({ status: 'Rejected', number: 'FV/7/10/2026', ksef: envKsef(null) })
    vi.mocked(getIssuedInvoice).mockResolvedValue(rejected)
    vi.mocked(updateIssuedInvoice).mockResolvedValue(rejected)
    vi.mocked(issueIssuedInvoice).mockResolvedValue({ ...rejected, status: 'Sending' })
    await render('/issued/i-1/edit?tenantId=t-1')

    await clickIssue()
    await waitFor(() => Boolean(dialogEl()))
    expect(dialogEl()?.textContent).toContain('FV/7/10/2026')
    expect(dialogEl()?.textContent).not.toContain('FV/1/11/2026')
    expect(issueIssuedInvoice).not.toHaveBeenCalled()

    await act(async () => buttonByText(dialogEl()!, 'Wystaw i wyślij')!.click())
    await waitFor(() => vi.mocked(issueIssuedInvoice).mock.calls.length === 1)
  })

  it('Rejected resend on test issues without a dialog', async () => {
    const rejected = invoice({ status: 'Rejected', number: 'FV/7/10/2026' })
    vi.mocked(getIssuedInvoice).mockResolvedValue(rejected)
    vi.mocked(updateIssuedInvoice).mockResolvedValue(rejected)
    vi.mocked(issueIssuedInvoice).mockResolvedValue({ ...rejected, status: 'Sending' })
    await render('/issued/i-1/edit?tenantId=t-1')

    await clickIssue()
    await waitFor(() => vi.mocked(issueIssuedInvoice).mock.calls.length === 1)
    expect(dialogEl()).toBeNull()
  })

  it('detail page renders an unknown environment', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Sending', ksef: envKsef(null) }))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes('NIEZNANE') ?? false)
  })

  const networkFailure = () => normalizeError(new TypeError('Failed to fetch'))
  const badGateway = () => normalizeError(new Response('<html>502 Bad Gateway</html>', { status: 502, headers: { 'Content-Type': 'text/html' } }))
  const rawTexts = ['Service Unavailable', 'Failed to fetch', 'Bad Gateway', '<html>']
  const expectNoRawText = () => rawTexts.forEach((raw) => expect(container.textContent).not.toContain(raw))

  it.each([
    ['network failure', networkFailure],
    ['502', badGateway],
  ])('%s on issue shows Sending when the invoice went through, with no second POST', async (_name, failure) => {
    const draft = invoice({ number: null })
    vi.mocked(getIssuedInvoice).mockResolvedValue(draft)
    vi.mocked(updateIssuedInvoice).mockResolvedValue(draft)
    vi.mocked(issueIssuedInvoice).mockImplementation(async () => {
      vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Sending' }))
      throw await failure()
    })
    await render('/issued/i-1/edit?tenantId=t-1')

    await clickIssue()
    await waitFor(() => container.textContent?.includes('czekamy na przyjęcie') ?? false)

    expect(buttonByText(container, 'Zapisz szkic')).toBeUndefined()
    expect(issueIssuedInvoice).toHaveBeenCalledTimes(1)
    expectNoRawText()
  })

  it('network failure with the invoice still Draft offers a manual retry, without auto-retry', async () => {
    const draft = invoice({ number: null })
    vi.mocked(getIssuedInvoice).mockResolvedValue(draft)
    vi.mocked(updateIssuedInvoice).mockResolvedValue(draft)
    vi.mocked(issueIssuedInvoice).mockRejectedValueOnce(await networkFailure())
    vi.mocked(issueIssuedInvoice).mockResolvedValueOnce(invoice({ status: 'Sending' }))
    await render('/issued/i-1/edit?tenantId=t-1')

    await clickIssue()
    await waitFor(() => container.textContent?.includes('Faktura nadal ma status: Szkic') ?? false)
    await act(flush)

    expect(issueIssuedInvoice).toHaveBeenCalledTimes(1)
    expectNoRawText()
    expect(buttonByText(container, 'Wystaw i wyślij do KSeF')?.disabled).toBe(false)

    await clickIssue()
    await waitFor(() => vi.mocked(issueIssuedInvoice).mock.calls.length === 2)
  })

  it('network failure after the production dialog closes it and shows the status', async () => {
    const draft = invoice({ number: null, ksef: envKsef('production') })
    vi.mocked(getIssuedInvoice).mockResolvedValue(draft)
    vi.mocked(updateIssuedInvoice).mockResolvedValue(draft)
    vi.mocked(issueIssuedInvoice).mockRejectedValue(await networkFailure())
    await render('/issued/i-1/edit?tenantId=t-1')

    await clickIssue()
    await waitFor(() => Boolean(dialogEl()))
    await act(async () => buttonByText(dialogEl()!, 'Wystaw i wyślij')!.click())
    await waitFor(() => container.textContent?.includes('Faktura nadal ma status: Szkic') ?? false)

    expect(dialogEl()).toBeNull()
    expectNoRawText()
  })

  it('network failure with Rejected flipped to Accepted opens the real status', async () => {
    const rejected = invoice({ status: 'Rejected', number: 'FV/7/10/2026' })
    vi.mocked(getIssuedInvoice).mockResolvedValue(rejected)
    vi.mocked(updateIssuedInvoice).mockResolvedValue(rejected)
    vi.mocked(issueIssuedInvoice).mockImplementation(async () => {
      vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Accepted', ksef: { ...ksef, ksefNumber: 'K-77' } }))
      throw await networkFailure()
    })
    await render('/issued/i-1/edit?tenantId=t-1')

    await clickIssue()
    await waitFor(() => container.textContent?.includes('K-77') ?? false)
    expectNoRawText()
  })

  it('network failure with a resend already rejected again opens the new rejection', async () => {
    const rejected = invoice({ status: 'Rejected', number: 'FV/7/10/2026', issuedAt: '2026-11-03T10:00:00Z' })
    vi.mocked(getIssuedInvoice).mockResolvedValue(rejected)
    vi.mocked(updateIssuedInvoice).mockResolvedValue(rejected)
    vi.mocked(issueIssuedInvoice).mockImplementation(async () => {
      vi.mocked(getIssuedInvoice).mockResolvedValue({
        ...rejected,
        issuedAt: '2026-11-03T11:00:00Z',
        ksef: { ...ksef, statusCode: 450, statusMessage: 'Nowy powód odrzucenia' },
      })
      throw await networkFailure()
    })
    await render('/issued/i-1/edit?tenantId=t-1')

    await clickIssue()
    await waitFor(() => container.textContent?.includes('Nowy powód odrzucenia') ?? false)

    expect(issueIssuedInvoice).toHaveBeenCalledTimes(1)
    expect(buttonByText(container, 'Zapisz szkic')).toBeUndefined()
    expect(container.textContent).not.toContain('Faktura nadal ma status')
  })

  it('network failure with the same Rejected attempt stays on the form', async () => {
    const rejected = invoice({ status: 'Rejected', number: 'FV/7/10/2026', issuedAt: '2026-11-03T10:00:00Z' })
    vi.mocked(getIssuedInvoice).mockResolvedValue(rejected)
    vi.mocked(updateIssuedInvoice).mockResolvedValue(rejected)
    vi.mocked(issueIssuedInvoice).mockRejectedValue(await networkFailure())
    await render('/issued/i-1/edit?tenantId=t-1')

    await clickIssue()
    await waitFor(() => container.textContent?.includes('Faktura nadal ma status: Odrzucona') ?? false)

    expect(buttonByText(container, 'Zapisz szkic')).toBeDefined()
  })

  it('HTTP 4xx with a non-JSON body on issue hides the raw text', async () => {
    const draft = invoice({ number: null })
    vi.mocked(getIssuedInvoice).mockResolvedValue(draft)
    vi.mocked(updateIssuedInvoice).mockResolvedValue(draft)
    vi.mocked(issueIssuedInvoice).mockRejectedValue(
      await normalizeError(new Response('<html>413 Too Large</html>', { status: 413, headers: { 'Content-Type': 'text/html' } })),
    )
    await render('/issued/i-1/edit?tenantId=t-1')

    await clickIssue()
    await waitFor(() => container.textContent?.includes('Faktura nadal ma status: Szkic') ?? false)

    expectNoRawText()
  })

  it('network failure with the reload failing too says the status is unknown', async () => {
    const draft = invoice({ number: null })
    vi.mocked(getIssuedInvoice).mockResolvedValue(draft)
    vi.mocked(updateIssuedInvoice).mockResolvedValue(draft)
    const failure = await networkFailure()
    vi.mocked(issueIssuedInvoice).mockImplementation(async () => {
      vi.mocked(getIssuedInvoice).mockRejectedValue(failure)
      throw failure
    })
    await render('/issued/i-1/edit?tenantId=t-1')

    await clickIssue()
    await waitFor(() => container.textContent?.includes('Nie wiemy, czy faktura została wysłana') ?? false)

    expect(issueIssuedInvoice).toHaveBeenCalledTimes(1)
    expectNoRawText()
  })

  it('PUT 409 on issue because the invoice left Draft opens the real status', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ number: null }))
    const conflict = await normalizeError(new Response(JSON.stringify({ message: 'Faktura nie jest już szkicem' }), { status: 409, headers: { 'Content-Type': 'application/json' } }))
    vi.mocked(updateIssuedInvoice).mockImplementation(async () => {
      vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Sending' }))
      throw conflict
    })
    await render('/issued/i-1/edit?tenantId=t-1')

    await clickIssue()
    await waitFor(() => container.textContent?.includes('czekamy na przyjęcie') ?? false)
    expect(issueIssuedInvoice).not.toHaveBeenCalled()
  })

  it('HTTP 403 on issue with the status unchanged keeps the server message', async () => {
    const draft = invoice({ number: null })
    vi.mocked(getIssuedInvoice).mockResolvedValue(draft)
    vi.mocked(updateIssuedInvoice).mockResolvedValue(draft)
    vi.mocked(issueIssuedInvoice).mockRejectedValue(
      await normalizeError(new Response(JSON.stringify({ message: 'Brak uprawnień do wystawiania' }), { status: 403, headers: { 'Content-Type': 'application/json' } })),
    )
    await render('/issued/i-1/edit?tenantId=t-1')

    await clickIssue()
    await waitFor(() => container.textContent?.includes('Brak uprawnień do wystawiania') ?? false)
    expect(container.textContent).not.toContain('Faktura nadal ma status')
  })

  it('network failure creating a new draft shows Polish text, not raw', async () => {
    vi.mocked(getSellerProfile).mockResolvedValue({ ...profile, ksefEnvironment: 'test' })
    vi.mocked(createIssuedInvoice).mockRejectedValue(await networkFailure())
    await render('/issued/new?tenantId=t-1')

    await clickIssue()
    await waitFor(() => container.textContent?.includes('Nie udało się zapisać szkicu') ?? false)
    expect(issueIssuedInvoice).not.toHaveBeenCalled()
    expectNoRawText()
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

    const anchorClick = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    await act(async () => buttonByText(container, 'Pobierz XML')!.click())
    await waitFor(() => createObjectURL.mock.calls.length === 1)
    expect(getIssuedInvoiceXml).toHaveBeenCalledWith('t-1', 'i-1')
    anchorClick.mockRestore()
  })

  it('Rejected shows the KSeF message, details and the edit link', async () => {
    const statusDetails = 'Data wystawienia faktury nie może być późniejsza niż data przyjęcia do systemu.'
    vi.mocked(getIssuedInvoice).mockResolvedValue(
      invoice({ status: 'Rejected', ksef: { ...ksef, statusCode: 450, statusMessage: 'Błąd weryfikacji semantyki', statusDetails } }),
    )
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes('Błąd weryfikacji semantyki') ?? false)

    expect(container.querySelector('[data-testid="ksef-code"]')?.textContent).toBe('Kod KSeF 450')
    const details = container.querySelector<HTMLDetailsElement>('[data-testid="ksef-details"]')
    expect(details?.textContent).toContain(statusDetails)
    expect(details?.open).toBe(false)
    expect(container.querySelector('[role="alert"]')?.textContent).not.toContain(statusDetails)
    expect(buttonByText(container, 'Kopiuj jako nową')).toBeUndefined()
    expect(container.querySelector('a[href*="/issued/i-1/edit"]')?.textContent).toContain('Popraw i wyślij ponownie')
    expect(buttonByText(container, 'Pobierz XML')).toBeUndefined()
    expect(buttonByText(container, 'Usuń')).toBeUndefined()
  })

  it.each([
    ['no write permission', 403, 'Token KSeF nie ma uprawnienia do wystawiania faktur. Wygeneruj token z uprawnieniem do zapisu i podmień go w ustawieniach firmy.'],
    ['invalid token', 450, 'KSeF odrzucił token (nieprawidłowy lub unieważniony). Wygeneruj nowy token i podmień go w ustawieniach firmy.'],
    ['missing token', null, 'Wysyłka faktur wymaga tokenu KSeF w ustawieniach firmy'],
  ])('Rejected by %s: the next action is the token, editing is not offered', async (_name, statusCode, statusMessage) => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Rejected', ksef: { ...ksef, statusCode, statusMessage } }))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => Boolean(container.querySelector('[data-testid="replace-token-link"]')))

    const panel = container.querySelector<HTMLElement>('section[aria-labelledby="ksef-panel-title"]')!
    expect(panel.querySelector('[data-testid="replace-token-link"]')?.textContent).toContain('Podmień token KSeF')
    expect(panel.querySelector('[data-testid="replace-token-link"]')?.getAttribute('href')).toBe('/company/token')
    expect(panel.querySelector('[data-testid="token-resend-link"]')?.getAttribute('href')).toBe('/issued/i-1/edit?tenantId=t-1')
    expect(panel.textContent).toContain('Numer FV/1/11/2026 zostaje — po podmianie tokenu wyślemy fakturę z tym samym numerem.')
    expect(panel.textContent).not.toContain('Popraw i wyślij ponownie')
    expect(buttonByText(container, 'Kopiuj jako nową')).toBeUndefined()
    expect(panel.querySelector('[role="alert"]')?.textContent).toBe(statusMessage)
    expect(panel.querySelector('[data-testid="ksef-code"]')?.textContent ?? null).toBe(statusCode === null ? null : `Kod KSeF ${statusCode}`)
  })

  it('a word like token in a data rejection does not switch the next action', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Rejected', ksef: { ...ksef, statusCode: 450, statusMessage: 'Błąd pola token w dokumencie' } }))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes('Popraw i wyślij ponownie') ?? false)
    expect(container.querySelector('[data-testid="replace-token-link"]')).toBeNull()
  })

  it('Sending without an error does not warn against a second send', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Sending' }))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes('czekamy na przyjęcie') ?? false)
    expect(container.textContent).not.toContain('Nie wysyłaj faktury drugi raz')
    expect(container.textContent).toContain('Odświeżamy automatycznie')
  })

  it('Sending with an error warns against a second send', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Sending', ksef: { ...ksef, lastError: 'KSeF nie odpowiada, ponowimy wysyłkę' } }))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes('Nie wysyłaj faktury drugi raz') ?? false)
    expect(container.textContent).toContain('KSeF nie odpowiada, ponowimy wysyłkę')
  })

  it('Accepted reads as immutable with PDF, XML, UPO and KOD I in reach', async () => {
    const verificationUrl = 'https://qr-test.ksef.mf.gov.pl/invoice/5260250274/03-11-2026/abc'
    vi.mocked(getIssuedInvoice).mockResolvedValue(
      invoice({ status: 'Accepted', ksef: { ...ksef, ksefNumber: 'K-1', verificationUrl, hasUpo: true, acceptedAt: '2026-11-03T10:05:00Z' } }),
    )
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => Boolean(buttonByText(container, 'Pobierz PDF')))

    expect(container.textContent).toContain('Faktury przyjętej w KSeF nie można usunąć ani edytować.')
    expect(buttonByText(container, 'Pobierz XML')).toBeDefined()
    expect(buttonByText(container, 'Pobierz UPO')?.disabled).toBe(false)
    expect(container.querySelector('[data-testid="kod-card"] svg')).not.toBeNull()
    expect(container.querySelector('[data-testid="kod-card"] a')?.getAttribute('href')).toBe(verificationUrl)
  })

  it('Accepted without UPO says no reissue is needed and disables the UPO download', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Accepted', ksef: { ...ksef, ksefNumber: 'K-1', lastError: 'UPO jeszcze niedostępne, ponowimy pobranie' } }))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes('Brak UPO nie wymaga ponownego wystawienia.') ?? false)
    expect(buttonByText(container, 'Pobierz UPO')?.disabled).toBe(true)
    expect(container.querySelector('[data-testid="kod-card"]')).toBeNull()
  })

  it('Draft details offer edit and delete after a dialog', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ number: null }))
    vi.mocked(deleteIssuedInvoice).mockResolvedValue(undefined)
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => Boolean(buttonByText(container, 'Usuń szkic')))

    expect(container.querySelector('a[href*="/issued/i-1/edit"]')?.textContent).toContain('Edytuj szkic')
    await act(async () => buttonByText(container, 'Usuń szkic')!.click())
    expect(deleteIssuedInvoice).not.toHaveBeenCalled()
    const dialog = container.querySelector<HTMLElement>('[role="alertdialog"]')!
    await act(async () => buttonByText(dialog, 'Usuń szkic')!.click())
    await waitFor(() => vi.mocked(deleteIssuedInvoice).mock.calls.length === 1)
    expect(deleteIssuedInvoice).toHaveBeenCalledWith('t-1', 'i-1')
  })

  it('Rejected for a token with code 450 shows the stored description', async () => {
    const statusMessage = 'KSeF odrzucił token (nieprawidłowy lub unieważniony). Wygeneruj nowy token i podmień go w ustawieniach firmy.'
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Rejected', ksef: { ...ksef, statusCode: 450, statusMessage } }))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => Boolean(container.querySelector('[role="alert"]')))

    expect(container.querySelector('[role="alert"]')?.textContent).toContain(statusMessage)
    expect(container.textContent).not.toContain('Błąd weryfikacji')
  })

  const numberTakenMessage = 'Ten numer faktury jest już użyty w KSeF. Sprawdź tę fakturę w KSeF, zanim wystawisz ją pod nowym numerem.'
  const rejected440 = (duplicateKsefNumber: string | null) =>
    invoice({ status: 'Rejected', ksef: { ...ksef, statusCode: 440, statusMessage: numberTakenMessage, duplicateKsefNumber } })
  const guidance = 'Sprawdź tę fakturę w KSeF. Jeśli to ta sama sprzedaż, nie wystawiaj jej ponownie.'

  it('Rejected with 440 shows the KSeF number and guidance next to the only copy action', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(rejected440('5260250274-20261103-ABC'))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes('jest już zajęty w KSeF') ?? false)

    const panel = container.querySelector<HTMLElement>('section[aria-labelledby="ksef-panel-title"]')!
    const alert = panel.querySelector('[role="alert"]')?.textContent
    expect(alert).toContain(numberTakenMessage)
    expect(alert).not.toContain('inny dokument')
    expect(panel.querySelector('dd.mono')?.textContent).toBe('5260250274-20261103-ABC')
    expect(panel.textContent).toContain(guidance)
    expect(buttonByText(panel, 'Kopiuj jako nową')).toBeDefined()
    expect([...container.querySelectorAll('button')].filter((b) => b.textContent?.includes('Kopiuj jako nową'))).toHaveLength(1)
    expect(container.textContent).not.toContain('Popraw i wyślij ponownie')
    expect(container.querySelector('a[href*="/edit"]')).toBeNull()
  })

  it('Rejected with 440 and no KSeF number still blocks re-issue and keeps the guidance', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(rejected440(null))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes(guidance) ?? false)

    expect(container.textContent).not.toContain('Numer KSeF')
    expect(buttonByText(container, 'Kopiuj jako nową')).toBeDefined()
    expect(container.querySelector('a[href*="/edit"]')).toBeNull()
  })

  it('edit route of a Rejected 440 opens the detail page instead of the form', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(rejected440('K-1'))
    await render('/issued/i-1/edit?tenantId=t-1')
    await waitFor(() => container.textContent?.includes(guidance) ?? false)

    expect(buttonByText(container, 'Zapisz szkic')).toBeUndefined()
    expect(buttonByText(container, 'Wystaw i wyślij do KSeF')).toBeUndefined()
  })

  it('Rejected edit hint shows the kept number, not the next one', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Rejected', number: 'FV/7/10/2026', issueDate: '2026-10-20' }))
    await render('/issued/i-1/edit?tenantId=t-1')
    await waitFor(() => Boolean(buttonByText(container, 'Zapisz szkic')))

    const hint = container.querySelector('aside')?.textContent
    expect(hint).toContain('Numer FV/7/10/2026 zostaje')
    expect(hint).not.toContain('Numer nadamy')
    expect(hint).not.toContain('FV/1/11/2026')
  })

  it.each(['Zapisz szkic', 'Wystaw i wyślij do KSeF'])('INV-VAL-022 from PUT via %s shows inline on the issue date', async (action) => {
    const message =
      'Numer FV/7/11/2026 należy do okresu 11/2026. Zostaw datę wystawienia w tym okresie albo użyj „Kopiuj jako nową”. Nowa faktura dostanie nowy numer, a ten pozostanie niewykorzystany.'
    vi.mocked(getSellerProfile).mockResolvedValue({ ...profile, ksefEnvironment: 'test' })
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Rejected', number: 'FV/7/11/2026' }))
    const body = { errors: [{ code: 'INV-VAL-022', field: 'issueDate', message }], warnings: [] }
    vi.mocked(updateIssuedInvoice).mockRejectedValue(
      await normalizeError(new Response(JSON.stringify(body), { status: 422, headers: { 'Content-Type': 'application/json' } })),
    )
    await render('/issued/i-1/edit?tenantId=t-1')
    await waitFor(() => Boolean(buttonByText(container, action)))

    await act(async () => buttonByText(container, action)!.click())
    await waitFor(() => container.querySelector('#issue-date-error') !== null)

    expect(container.querySelector('#issue-date-error')?.textContent).toBe(message)
    expect(container.querySelector('#issue-date')?.getAttribute('aria-invalid')).toBe('true')
    expect(issueIssuedInvoice).not.toHaveBeenCalled()
  })

  it('INV-VAL-023 on issue is visible although the number is not a form field', async () => {
    const message = 'Numer FV/7/11/2026 jest już w KSeF, więc nie można go wysłać ponownie.'
    vi.mocked(getSellerProfile).mockResolvedValue({ ...profile, ksefEnvironment: 'test' })
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Rejected', number: 'FV/7/11/2026' }))
    vi.mocked(updateIssuedInvoice).mockResolvedValue(invoice({ status: 'Rejected', number: 'FV/7/11/2026' }))
    const body = { errors: [{ code: 'INV-VAL-023', field: 'number', message }], warnings: [] }
    vi.mocked(issueIssuedInvoice).mockRejectedValue(
      await normalizeError(new Response(JSON.stringify(body), { status: 422, headers: { 'Content-Type': 'application/json' } })),
    )
    await render('/issued/i-1/edit?tenantId=t-1')
    await waitFor(() => Boolean(buttonByText(container, 'Wystaw i wyślij do KSeF')))

    await act(async () => buttonByText(container, 'Wystaw i wyślij do KSeF')!.click())
    await waitFor(() => container.querySelector('[role="alert"]') !== null)

    expect(container.querySelector('[role="alert"] [data-code="INV-VAL-023"]')?.textContent).toBe(message)
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
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Accepted' }))
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
    const lastError = 'KSeF odrzucił token (nieprawidłowy lub unieważniony). Wygeneruj nowy token i podmień go w ustawieniach firmy.'
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Sending', ksef: { ...ksef, lastError } }))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes(lastError) ?? false)
    expect(container.textContent).toContain('Nie wysyłaj faktury drugi raz')
    expect(container.textContent).not.toContain('KSeF nie odpowiada')
    expect(buttonByText(container, 'Kopiuj jako nową')).toBeUndefined()
  })

  it.each([
    'UPO jeszcze niedostępne, ponowimy pobranie',
    'KSeF nie odpowiada, ponowimy wysyłkę',
  ])('Accepted with UPO pending shows "%s" and still offers copy', async (lastError) => {
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Accepted', ksef: { ...ksef, ksefNumber: 'K-1', lastError } }))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => container.textContent?.includes(lastError) ?? false)
    expect(container.textContent).toContain('Oczekuje na UPO')
    expect(container.textContent).toContain('Faktura jest przyjęta w KSeF i ma numer. Brak UPO nie wymaga ponownego wystawienia.')
    expect(container.textContent).not.toContain('Nie wystawiaj')
    expect(buttonByText(container, 'Kopiuj jako nową')).toBeDefined()
  })

  it('Accepted with UPO hides a stale lastError', async () => {
    const lastError = 'Stary błąd'
    vi.mocked(getIssuedInvoice).mockResolvedValue(invoice({ status: 'Accepted', ksef: { ...ksef, ksefNumber: 'K-1', hasUpo: true, lastError } }))
    await render('/issued/i-1?tenantId=t-1')
    await waitFor(() => Boolean(container.querySelector('#ksef-panel-title')))
    expect(container.textContent).not.toContain(lastError)
  })
})
