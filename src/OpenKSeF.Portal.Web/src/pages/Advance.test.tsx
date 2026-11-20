// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, type ReactElement } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  createAdvance,
  createFinal,
  getIssuedInvoice,
  issueIssuedInvoice,
  updateAdvance,
  updateCorrection,
  updateFinal,
} from '@/api/endpoints/issuedInvoices'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { listTenants } from '@/api/endpoints/tenants'
import { ApiError } from '@/api/errors'
import type { IssuedInvoiceResponse, OrderChain, SellerProfileResponse } from '@/api/types'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import acceptedCorrectionSample from '@/test/fixtures/issued-invoice-correction-accepted.json'
import originalSample from '@/test/fixtures/issued-invoice-original-with-corrections.json'
import { ownerAccess } from '@/test/tenant'
import { ChainDraftPage } from './ChainDraft'
import { CorrectionDraftPage } from './CorrectionDraft'
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
  getExchangeRateProposal: vi.fn(),
  copyIssuedInvoice: vi.fn(),
  deleteIssuedInvoice: vi.fn(),
  createCorrection: vi.fn(),
  updateCorrection: vi.fn(),
  reissueInvoice: vi.fn(),
  createAdvance: vi.fn(),
  updateAdvance: vi.fn(),
  createFinal: vi.fn(),
  updateFinal: vi.fn(),
}))

const profile: SellerProfileResponse = {
  legalName: 'OpenKSeF Test A sp. z o.o.',
  addressLine1: 'ul. Prosta 1',
  addressLine2: '00-001 Warszawa',
  bankAccount: '93000000002026111700000815',
  cashAccounting: false,
  numberingPattern: 'FV/{nr}/{MM}/{RRRR}',
  nextNumber: 4,
  nextNumberPeriod: '2026-11',
  nip: '4444444444',
  isComplete: true,
  nextNumberPreview: 'FV/4/11/2026',
  ksefEnvironment: 'test',
  advanceNumberingPattern: 'ZAL/{nr}/{MM}/{RRRR}',
  advanceNextNumber: 2,
  advanceNextNumberPeriod: '2026-11',
  advanceNextNumberPreview: 'ZAL/2/11/2026',
}

const order = [{ lineNo: 1, name: 'Projekt', unit: 'usł.', quantity: 1, unitNetPrice: 1000, vatRate: '23' as const, netAmount: 1000, vatAmount: 230, baseLineNo: null }]

const base = originalSample as unknown as IssuedInvoiceResponse

const doc = (patch: Partial<IssuedInvoiceResponse>): IssuedInvoiceResponse => ({
  ...base,
  createdAt: '2026-11-05T10:00:00Z',
  updatedAt: '2026-11-05T10:00:00Z',
  issuedAt: null,
  ksef: { ...base.ksef, acceptedAt: null, verificationUrl: null, ksefNumber: null, hasUpo: false },
  currentState: null,
  corrections: null,
  openCorrectionId: null,
  correctedInvoice: null,
  lines: order,
  issueDate: '2026-11-06',
  saleDate: '2026-11-05',
  ...patch,
})

const chain = (patch: Partial<OrderChain> = {}): OrderChain => ({
  orderValue: 1230,
  advances: [{ id: 'z-1', number: 'ZAL/1/11/2026', invoiceType: 'ZAL', status: 'Accepted', issueDate: '2026-11-06', totalGross: 369 }],
  final: null,
  corrections: [],
  remaining: 861,
  openDocumentId: null,
  ...patch,
})

const firstZal = (patch: Partial<IssuedInvoiceResponse> = {}): IssuedInvoiceResponse =>
  doc({
    id: 'z-1',
    status: 'Draft',
    number: null,
    invoiceType: 'ZAL',
    orderChainId: 'z-1',
    advanceAmount: null,
    vatSummary: [],
    totalNet: 0,
    totalVat: 0,
    totalGross: 0,
    chain: chain({ advances: [{ id: 'z-1', number: null, invoiceType: 'ZAL', status: 'Draft', issueDate: '2026-11-06', totalGross: 0 }], remaining: 1230, openDocumentId: 'z-1' }),
    ...patch,
  })

const acceptedZal = (patch: Partial<IssuedInvoiceResponse> = {}): IssuedInvoiceResponse =>
  doc({
    id: 'z-1',
    status: 'Accepted',
    number: 'ZAL/1/11/2026',
    invoiceType: 'ZAL',
    orderChainId: 'z-1',
    advanceAmount: 369,
    vatSummary: [{ vatRate: '23', net: 300, vat: 69 }],
    totalNet: 300,
    totalVat: 69,
    totalGross: 369,
    ksef: { ...base.ksef, acceptedAt: '2026-11-06T10:05:00Z', verificationUrl: 'https://qr-test.ksef.mf.gov.pl/x' },
    chain: chain(),
    ...patch,
  })

const secondZal = (): IssuedInvoiceResponse =>
  firstZal({ id: 'z-2', orderChainId: 'z-1', advanceAmount: null, chain: chain({ openDocumentId: 'z-2' }) })

const finalDraft = (): IssuedInvoiceResponse =>
  doc({
    id: 'r-1',
    status: 'Draft',
    number: null,
    invoiceType: 'ROZ',
    orderChainId: 'z-1',
    saleDate: '2026-11-20',
    issueDate: '2026-11-20',
    dueDate: '2026-12-04',
    vatSummary: [{ vatRate: '23', net: 500, vat: 115 }],
    totalNet: 500,
    totalVat: 115,
    totalGross: 615,
    advanceInvoices: [
      { id: 'z-1', number: 'ZAL/1/11/2026', ksefNumber: 'K-1' },
      { id: 'z-2', number: 'ZAL/2/11/2026', ksefNumber: 'K-2' },
    ],
    chain: chain({ remaining: 615, openDocumentId: 'r-1' }),
  })

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

function Path(): ReactElement {
  return <output data-testid="path">{useLocation().pathname}</output>
}

describe('advance and final invoices in the portal', () => {
  let root: Root
  let container: HTMLDivElement
  const button = (text: string) => [...container.querySelectorAll('button')].find((item) => item.textContent?.includes(text))
  const byId = (id: string) => container.querySelector<HTMLInputElement>(`#${id}`)!
  const path = () => container.querySelector('[data-testid="path"]')?.textContent
  const text = (testId: string) => container.querySelector(`[data-testid="${testId}"]`)?.textContent?.replace(/\s/g, ' ') ?? ''

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

  function serve(...invoices: IssuedInvoiceResponse[]): void {
    vi.mocked(getIssuedInvoice).mockImplementation(async (_tenant, id) => invoices.find((item) => item.id === id) ?? Promise.reject(new ApiError('Nie znaleziono.', 404)))
  }

  async function render(at: string): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    await act(async () =>
      root.render(
        <QueryClientProvider client={client}>
          <MemoryRouter initialEntries={[at]}>
            <CompanyProvider>
              <Path />
              <Routes>
                <Route path="/issued/:id/edit" element={<InvoiceDraftPage />} />
                <Route path="/issued/:id/chain" element={<ChainDraftPage />} />
                <Route path="/issued/:id/correct" element={<CorrectionDraftPage />} />
                <Route path="/issued/:id" element={<IssuedInvoiceDetailsPage />} />
              </Routes>
            </CompanyProvider>
          </MemoryRouter>
        </QueryClientProvider>,
      ),
    )
  }

  it('redirects a ZAL draft opened at /edit (released mobile) to the chain form', async () => {
    serve(firstZal())
    await render('/issued/z-1/edit?tenantId=t-1')
    await waitFor(() => path() === '/issued/z-1/chain')
  })

  it('first ZAL: 369,00 of a 1 230,00 order splits to 300,00 + 69,00, saves the advance and issues as ZAL', async () => {
    serve(firstZal())
    vi.mocked(updateAdvance).mockImplementation(async (_t, _id, request) => firstZal({ advanceAmount: request.advanceAmount, version: 2, totalGross: 369 }))
    vi.mocked(issueIssuedInvoice).mockResolvedValue(acceptedZal({ status: 'Sending' }))
    await render('/issued/z-1/chain?tenantId=t-1')
    await waitFor(() => Boolean(container.querySelector('#advance-amount')))
    expect(container.querySelector('#buyer-name')).not.toBeNull()
    expect(container.querySelector('#due-date')).toBeNull()
    expect(text('order-value')).toBe('1 230,00 PLN')
    expect(container.textContent).toContain('ZAL/2/11/2026')

    await act(async () => typeInto(byId('advance-amount'), '369'))
    expect(text('total-gross')).toBe('369,00 PLN')
    expect(text('chain-summary')).toContain('300,00')
    expect(text('chain-summary')).toContain('69,00')
    expect(text('left-after')).toContain('861,00 PLN')

    await act(async () => button('Wystaw i wyślij')!.click())
    await waitFor(() => vi.mocked(issueIssuedInvoice).mock.calls.length === 1)
    expect(vi.mocked(updateAdvance).mock.calls[0][2]).toMatchObject({
      advanceAmount: 369,
      paymentDate: '2026-11-05',
      issueDate: '2026-11-06',
      currency: 'PLN',
      buyer: expect.objectContaining({ nip: '3333333333' }),
      lines: [{ name: 'Projekt', unit: 'usł.', quantity: 1, unitNetPrice: 1000, vatRate: '23' }],
    })
    expect(vi.mocked(issueIssuedInvoice).mock.calls[0][5]).toBe('ZAL')
    await waitFor(() => path() === '/issued/z-1')
  })

  it('multi-rate order splits 115,50 to 50,00 + 11,50 and 50,00 + 4,00', async () => {
    const mixed = [order[0], { ...order[0], lineNo: 2, vatRate: '8' as const, vatAmount: 8 }].map((line) => ({ ...line, unitNetPrice: 100, netAmount: 100, vatAmount: line.vatRate === '8' ? 8 : 23 }))
    serve(firstZal({ lines: mixed }))
    await render('/issued/z-1/chain?tenantId=t-1')
    await waitFor(() => Boolean(container.querySelector('#advance-amount')))
    await act(async () => typeInto(byId('advance-amount'), '115,50'))
    const rows = [...container.querySelectorAll('[data-testid="chain-summary"] tbody tr')].map((row) => row.textContent)
    expect(rows).toEqual(['23%50,0011,5061,50', '8%50,004,0054,00'])
  })

  it('accepted ZAL shows the chain, Zapłacono, no due date, and Kolejna zaliczka opens a locked-buyer draft with remaining 615,00', async () => {
    serve(acceptedZal(), secondZal())
    vi.mocked(createAdvance).mockResolvedValue(secondZal())
    await render('/issued/z-1?tenantId=t-1')
    await waitFor(() => Boolean(button('Kolejna zaliczka')))
    expect(text('chain-order')).toBe('1 230,00 PLN')
    expect(text('chain-remaining')).toBe('861,00 PLN')
    expect(text('advance-paid')).toContain('369,00 PLN')
    expect(container.textContent).not.toContain('Termin płatności')
    expect(container.textContent).toContain('Faktura zaliczkowa')
    expect(button('Kopiuj jako nową')).toBeUndefined()

    await act(async () => button('Kolejna zaliczka')!.click())
    await waitFor(() => path() === '/issued/z-2/chain')
    expect(createAdvance).toHaveBeenCalledWith('t-1', 'z-1')
    await waitFor(() => Boolean(container.querySelector('#advance-amount')))
    expect(text('buyer-locked')).toContain('3333333333')
    expect(container.querySelector('#buyer-name')).toBeNull()
    expect(text('advance-open')).toContain('369,00 PLN')
    expect(text('advance-open')).toContain('861,00 PLN')
    await act(async () => typeInto(byId('advance-amount'), '246'))
    expect(text('chain-summary')).toContain('200,00')
    expect(text('chain-summary')).toContain('46,00')
    expect(text('left-after')).toContain('615,00 PLN')
    await act(async () => typeInto(byId('advance-amount'), '900'))
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('przekracza pozostałą wartość')
  })

  it('an open chain document disables the chain actions', async () => {
    serve(acceptedZal({ chain: chain({ openDocumentId: 'z-2' }) }))
    await render('/issued/z-1?tenantId=t-1')
    await waitFor(() => Boolean(button('Kolejna zaliczka')))
    expect(button('Kolejna zaliczka')!.disabled).toBe(true)
    expect(button('Wystaw fakturę rozliczającą')!.disabled).toBe(true)
    expect(text('chain-open')).toContain('otwarty dokument')
  })

  it('Wystaw fakturę rozliczającą: remaining 615,00 equals the server, lowering lines below the advances blocks with the server text', async () => {
    serve(acceptedZal(), finalDraft())
    vi.mocked(createFinal).mockResolvedValue(finalDraft())
    vi.mocked(updateFinal).mockImplementation(async () => finalDraft())
    await render('/issued/z-1?tenantId=t-1')
    await waitFor(() => Boolean(button('Wystaw fakturę rozliczającą')))
    await act(async () => button('Wystaw fakturę rozliczającą')!.click())
    await waitFor(() => path() === '/issued/r-1/chain')
    expect(createFinal).toHaveBeenCalledWith('t-1', 'z-1')
    await waitFor(() => Boolean(container.querySelector('#line-0-unitNetPrice')))
    expect(text('total-gross')).toBe('615,00 PLN')
    expect(text('advances-deducted')).toContain('615,00 PLN')
    expect(text('buyer-locked')).toContain('PLN')
    expect((container.querySelector('#invoice-currency') as HTMLSelectElement | null)?.disabled ?? true).toBe(true)

    await act(async () => typeInto(byId('line-0-unitNetPrice'), '500'))
    expect(text('total-gross')).toBe('0,00 PLN')
    expect(container.querySelector('[data-testid="exceeds"]')).toBeNull()
    expect(container.textContent).toContain('nie ma sekcji płatności')

    await act(async () => typeInto(byId('line-0-unitNetPrice'), '400'))
    expect(text('exceeds')).toBe('Zaliczki przekraczają wartość rozliczenia. Najpierw skoryguj fakturę zaliczkową.')

    await act(async () => typeInto(byId('line-0-unitNetPrice'), '1000'))
    await act(async () => button('Zapisz szkic')!.click())
    await waitFor(() => vi.mocked(updateFinal).mock.calls.length === 1)
    expect(vi.mocked(updateFinal).mock.calls[0][2]).toMatchObject({ saleDate: '2026-11-20', dueDate: '2026-12-04', lines: [expect.objectContaining({ unitNetPrice: 1000 })] })
    expect(vi.mocked(updateFinal).mock.calls[0][2]).not.toHaveProperty('buyer')
  })

  it('accepted ROZ lists the deducted advances', async () => {
    serve({ ...finalDraft(), status: 'Accepted', number: 'FV/4/11/2026' })
    await render('/issued/r-1?tenantId=t-1')
    await waitFor(() => Boolean(container.querySelector('[data-testid="advance-invoices"]')))
    expect(text('advance-invoices')).toContain('ZAL/1/11/2026')
    expect(text('advance-invoices')).toContain('K-2')
    expect(container.textContent).toContain('Faktura rozliczająca')
  })

  it('KOR_ZAL 369,00 to 246,00 shows -123,00 with -100,00 / -23,00 deltas and every order line as a pair', async () => {
    const korZal = doc({
      id: 'kz-1',
      status: 'Draft',
      number: null,
      invoiceType: 'KOR_ZAL',
      orderChainId: 'z-1',
      advanceAmount: 369,
      amountBefore: 369,
      correctedInvoice: { id: 'z-1', number: 'ZAL/1/11/2026', ksefNumber: 'K-1', issueDate: '2026-11-06', invoiceType: 'ZAL' },
      lines: order.map((line) => ({ ...line, baseLineNo: 1 })),
      vatSummary: [],
      totalNet: 0,
      totalVat: 0,
      totalGross: 0,
    })
    serve(acceptedZal(), korZal)
    vi.mocked(updateCorrection).mockResolvedValue({ ...korZal, version: 2 })
    await render('/issued/kz-1/correct?tenantId=t-1')
    await waitFor(() => Boolean(container.querySelector('#advance-amount')))
    expect(container.textContent).toContain('Korekta faktury zaliczkowej')
    expect(text('advance-before')).toContain('369,00 PLN')
    expect(text('total-gross')).toBe('0,00 PLN')

    await act(async () => typeInto(byId('advance-amount'), '246'))
    expect(text('total-gross')).toBe('-123,00 PLN')
    expect(text('correction-deltas')).toContain('-100,00')
    expect(text('correction-deltas')).toContain('-23,00')
    expect(container.querySelectorAll('[data-testid="correction-rows"] tbody tr')).toHaveLength(2)

    await act(async () => button('Rabat')!.click())
    await act(async () => button('Zapisz szkic')!.click())
    await waitFor(() => vi.mocked(updateCorrection).mock.calls.length === 1)
    expect(vi.mocked(updateCorrection).mock.calls[0][2]).toMatchObject({ advanceAmount: 246, correctionReason: 'Rabat' })
  })

  it('a refund correction (P_15 <= 0) hides Termin płatności, Płatność and Rachunek', async () => {
    const sample = acceptedCorrectionSample as unknown as IssuedInvoiceResponse
    serve({
      ...sample,
      id: 'k-1',
      createdAt: '2026-11-20T10:00:00Z',
      updatedAt: '2026-11-20T10:00:00Z',
      issuedAt: '2026-11-20T10:01:00Z',
      ksef: { ...sample.ksef, acceptedAt: '2026-11-20T10:05:00Z', verificationUrl: 'https://qr-test.ksef.mf.gov.pl/x' },
      correctedInvoice: sample.correctedInvoice ? { ...sample.correctedInvoice, id: 'o-1' } : null,
    })
    await render('/issued/k-1?tenantId=t-1')
    await waitFor(() => Boolean(container.querySelector('[data-testid="corrected-invoice"]')))
    expect(sample.totalGross).toBeLessThan(0)
    expect(container.textContent).not.toContain('Termin płatności')
    expect(container.textContent).not.toContain('Rachunek')
    expect([...container.querySelectorAll('dt')].map((dt) => dt.textContent)).not.toContain('Płatność')
  })
})
