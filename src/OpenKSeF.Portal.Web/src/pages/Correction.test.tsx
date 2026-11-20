// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, type ReactElement } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  createCorrection,
  getIssuedInvoice,
  issueIssuedInvoice,
  reissueInvoice,
  updateCorrection,
} from '@/api/endpoints/issuedInvoices'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { listTenants } from '@/api/endpoints/tenants'
import { ApiError } from '@/api/errors'
import type { IssuedInvoiceResponse, SellerProfileResponse } from '@/api/types'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { ownerAccess } from '@/test/tenant'
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
  copyIssuedInvoice: vi.fn(),
  deleteIssuedInvoice: vi.fn(),
  createCorrection: vi.fn(),
  updateCorrection: vi.fn(),
  reissueInvoice: vi.fn(),
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
  correctionNumberingPattern: 'KOR/{nr}/{MM}/{RRRR}',
  correctionNextNumber: 1,
  correctionNextNumberPeriod: '2026-11',
  correctionNextNumberPreview: 'KOR/1/11/2026',
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
  acceptedOn: null,
  upoUnavailable: false,
}

const line = { lineNo: 1, name: 'Usługa', unit: 'szt.', quantity: 1, unitNetPrice: 100, vatRate: '23' as const, netAmount: 100, vatAmount: 23 }

const original = (patch: Partial<IssuedInvoiceResponse> = {}): IssuedInvoiceResponse => ({
  id: 'o-1',
  status: 'Accepted',
  number: 'FV/3/11/2026',
  seller: { nip: '4444444444', name: 'Sprzedawca', addressLine1: 'a', addressLine2: 'b', bankAccount: null, cashAccounting: false },
  buyer: { kind: 'Company', nip: '5555555555', name: 'Nabywca', addressLine1: 'ul. Krzywa 1', addressLine2: '00-002 Warszawa' },
  issueDate: '2026-11-03',
  saleDate: '2026-11-03',
  dueDate: '2026-11-17',
  paymentMethod: 'Transfer',
  splitPayment: false,
  exemptionBasis: null,
  lines: [line],
  vatSummary: [{ vatRate: '23', net: 100, vat: 23 }],
  totalNet: 100,
  totalVat: 23,
  totalGross: 123,
  currency: 'PLN',
  ksef: { ...ksef, ksefNumber: '4444444444-20261103-ABC-01', acceptedAt: '2026-11-03T10:05:00Z' },
  warnings: [],
  createdAt: '2026-11-03T10:00:00Z',
  updatedAt: '2026-11-03T10:00:00Z',
  issuedAt: '2026-11-03T10:01:00Z',
  version: 5,
  sellerVersion: 3,
  invoiceType: 'VAT',
  ...patch,
})

const correction = (patch: Partial<IssuedInvoiceResponse> = {}): IssuedInvoiceResponse =>
  original({
    id: 'k-1',
    status: 'Draft',
    number: null,
    issueDate: '2026-11-20',
    lines: [{ ...line, baseLineNo: 1 }],
    vatSummary: [],
    totalNet: 0,
    totalVat: 0,
    totalGross: 0,
    ksef,
    issuedAt: null,
    version: 1,
    invoiceType: 'KOR',
    correctedInvoice: { id: 'o-1', number: 'FV/3/11/2026', ksefNumber: '4444444444-20261103-ABC-01', issueDate: '2026-11-03', invoiceType: 'VAT' },
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

function typeInto(element: HTMLInputElement | HTMLSelectElement, value: string): void {
  const prototype = element instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype
  Object.getOwnPropertyDescriptor(prototype, 'value')?.set?.call(element, value)
  element.dispatchEvent(new Event(element instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }))
}

function conflict(message: string, code: string, status = 409): ApiError {
  return new ApiError(message, status, new Response(JSON.stringify({ message, code }), { status, headers: { 'Content-Type': 'application/json' } }))
}

function Path(): ReactElement {
  return <output data-testid="path">{useLocation().pathname}</output>
}

describe('KOR corrections in the portal', () => {
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

  async function render(path: string): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    await act(async () =>
      root.render(
        <QueryClientProvider client={client}>
          <MemoryRouter initialEntries={[path]}>
            <CompanyProvider>
              <Path />
              <Routes>
                <Route path="/issued/:id/edit" element={<InvoiceDraftPage />} />
                <Route path="/issued/:id/correct" element={<CorrectionDraftPage />} />
                <Route path="/issued/:id" element={<IssuedInvoiceDetailsPage />} />
              </Routes>
            </CompanyProvider>
          </MemoryRouter>
        </QueryClientProvider>,
      ),
    )
  }

  describe('original details', () => {
    it('offers Koryguj, Koryguj do zera and Błędny NIP nabywcy; Koryguj opens the correction form', async () => {
      serve(original(), correction())
      vi.mocked(createCorrection).mockResolvedValue(correction())
      await render('/issued/o-1?tenantId=t-1')
      await waitFor(() => Boolean(button('Koryguj do zera')))
      expect(button('Błędny NIP nabywcy')).toBeDefined()
      expect(text('corrections-card')).toContain('Brak korekt')

      await act(async () => button('Koryguj')!.click())
      await waitFor(() => path() === '/issued/k-1/correct')
      expect(createCorrection).toHaveBeenCalledWith('t-1', 'o-1', 'Edit')
    })

    it('an open correction disables the actions and names it', async () => {
      serve(
        original({
          openCorrectionId: 'k-1',
          corrections: [{ id: 'k-1', number: 'KOR/1/11/2026', invoiceType: 'KOR', status: 'Rejected', issueDate: '2026-11-20', totalGross: -123 }],
        }),
      )
      await render('/issued/o-1?tenantId=t-1')
      await waitFor(() => Boolean(button('Koryguj do zera')))
      expect(button('Koryguj')!.disabled).toBe(true)
      expect(button('Koryguj do zera')!.disabled).toBe(true)
      expect(button('Błędny NIP nabywcy')!.disabled).toBe(true)
      expect(text('open-correction')).toContain('KOR/1/11/2026')
      expect(text('corrections-card')).toContain('-123,00 PLN')
    })

    it('shows the server message of 409 INV-KOR-OPEN as is', async () => {
      serve(original())
      vi.mocked(createCorrection).mockRejectedValue(conflict('Faktura ma otwartą korektę KOR/1/11/2026.', 'INV-KOR-OPEN'))
      await render('/issued/o-1?tenantId=t-1')
      await waitFor(() => Boolean(button('Koryguj do zera')))
      await act(async () => button('Koryguj do zera')!.click())
      await waitFor(() => (container.textContent ?? '').includes('Faktura ma otwartą korektę KOR/1/11/2026.'))
      expect(createCorrection).toHaveBeenCalledWith('t-1', 'o-1', 'ToZero')
    })

    it('Błędny NIP nabywcy explains the path, then creates a to-zero correction', async () => {
      serve(original(), correction({ lines: [], correctionReason: 'Błędny NIP nabywcy' }))
      vi.mocked(createCorrection).mockResolvedValue(correction({ lines: [] }))
      await render('/issued/o-1?tenantId=t-1')
      await waitFor(() => Boolean(button('Błędny NIP nabywcy')))
      await act(async () => button('Błędny NIP nabywcy')!.click())
      expect(container.querySelector('[role="alertdialog"]')?.textContent).toContain('NIP-u nie zmienisz korektą')
      await act(async () => button('Utwórz korektę do zera')!.click())
      await waitFor(() => path() === '/issued/k-1/correct')
      expect(createCorrection).toHaveBeenCalledWith('t-1', 'o-1', 'WrongBuyerId')
    })

    it('lists corrections, shows the current state and Wystaw poprawną fakturę after a zero correction', async () => {
      const zeroed = original({
        currentState: { lines: [], vatSummary: [], totalNet: 0, totalVat: 0, totalGross: 0, buyer: original().buyer, isZero: true },
        corrections: [{ id: 'k-1', number: 'KOR/1/11/2026', invoiceType: 'KOR', status: 'Accepted', issueDate: '2026-11-20', totalGross: -123 }],
      })
      serve(zeroed, original({ id: 'n-1', status: 'Draft', number: null, buyer: { ...original().buyer, nip: null } }))
      vi.mocked(reissueInvoice).mockResolvedValue(original({ id: 'n-1', status: 'Draft', number: null }))
      await render('/issued/o-1?tenantId=t-1')
      await waitFor(() => Boolean(button('Wystaw poprawną fakturę')))
      expect(button('Koryguj do zera')).toBeUndefined()
      expect(text('current-state')).toContain('Faktura skorygowana do zera')
      expect(text('current-gross')).toBe('0,00 PLN')
      expect(container.querySelector('a[href^="/issued/k-1"]')?.textContent).toBe('KOR/1/11/2026')
      expect(text('reissue')).toContain('pole NIP będzie puste')

      await act(async () => button('Wystaw poprawną fakturę')!.click())
      await waitFor(() => path() === '/issued/n-1/edit')
      expect(reissueInvoice).toHaveBeenCalledWith('t-1', 'o-1')
    })

    it('a correction links to the original and shows before and after rows, without copy or new corrections', async () => {
      serve(
        correction({
          status: 'Accepted',
          number: 'KOR/1/11/2026',
          ksef: { ...ksef, ksefNumber: '4444444444-20261120-DEF-01' },
          correctionReason: 'Zwrot towaru',
          correctionType: 2,
          correctionRows: [
            { rowNo: 1, isBefore: true, baseLineNo: 1, name: 'Usługa', unit: 'szt.', quantity: 1, unitNetPrice: 100, vatRate: '23', netAmount: 100 },
            { rowNo: 2, isBefore: false, baseLineNo: 1, name: 'Usługa', unit: 'szt.', quantity: 0, unitNetPrice: 100, vatRate: '23', netAmount: 0 },
          ],
          vatSummary: [{ vatRate: '23', net: -100, vat: -23 }],
          totalNet: -100,
          totalVat: -23,
          totalGross: -123,
        }),
      )
      await render('/issued/k-1?tenantId=t-1')
      await waitFor(() => Boolean(container.querySelector('[data-testid="corrected-invoice"]')))
      expect(container.querySelector('[data-testid="corrected-invoice"] a')?.getAttribute('href')).toBe('/issued/o-1?tenantId=t-1')
      expect(text('corrected-invoice')).toContain('Zwrot towaru')
      expect(text('corrected-invoice')).toContain('w dacie wystawienia faktury korygującej')
      const rows = [...container.querySelectorAll('[data-testid="correction-rows"] tbody tr')].map((row) => row.textContent)
      expect(rows).toHaveLength(2)
      expect(rows[0]).toContain('przed')
      expect(rows[1]).toContain('po')
      expect(text('total-gross')).toBe('-123,00 PLN')
      expect(button('Kopiuj jako nową')).toBeUndefined()
      expect(button('Koryguj')).toBeUndefined()
    })
  })

  describe('correction form', () => {
    it('redirects a KOR draft opened at /edit (released mobile) to the correction form', async () => {
      serve(original(), correction())
      await render('/issued/k-1/edit?tenantId=t-1')
      await waitFor(() => path() === '/issued/k-1/correct')
    })

    it('locks tax id and currency, shows live signed deltas and P_15, and saves the after state', async () => {
      serve(original(), correction())
      vi.mocked(updateCorrection).mockImplementation(async (_tenant, _id, request) => correction({ correctionReason: request.correctionReason, version: 2 }))
      await render('/issued/k-1/correct?tenantId=t-1')
      await waitFor(() => Boolean(container.querySelector('#line-0-quantity')))
      expect(text('buyer-locked')).toContain('5555555555')
      expect(text('buyer-locked')).toContain('PLN')
      expect(container.querySelector('#buyer-nip')).toBeNull()
      expect(text('corrected-ref')).toContain('FV/3/11/2026')
      expect(text('total-gross')).toBe('0,00 PLN')
      expect(byId('correction-type').value).toBe('')
      expect(container.textContent).toContain('KOR/1/11/2026')

      await act(async () => typeInto(byId('line-0-quantity'), '0'))
      expect(text('total-gross')).toBe('-123,00 PLN')
      expect(text('correction-deltas')).toContain('-100,00')
      expect(text('correction-deltas')).toContain('-23,00')
      expect([...container.querySelectorAll('[data-testid="correction-rows"] tbody tr')]).toHaveLength(2)

      await act(async () => button('Zwrot towaru')!.click())
      await act(async () => button('Zapisz szkic')!.click())
      await waitFor(() => vi.mocked(updateCorrection).mock.calls.length === 1)
      expect(vi.mocked(updateCorrection).mock.calls[0]).toEqual([
        't-1',
        'k-1',
        expect.objectContaining({
          correctionReason: 'Zwrot towaru',
          correctionType: null,
          buyer: expect.objectContaining({ kind: 'Company', nip: '5555555555' }),
          lines: [{ baseLineNo: 1, name: 'Usługa', unit: 'szt.', quantity: 0, unitNetPrice: 100, vatRate: '23' }],
        }),
        1,
      ])
    })

    it('price up to 120 shows +24,60 and a removed line can be restored', async () => {
      serve(original(), correction())
      await render('/issued/k-1/correct?tenantId=t-1')
      await waitFor(() => Boolean(container.querySelector('#line-0-unitNetPrice')))
      await act(async () => typeInto(byId('line-0-unitNetPrice'), '120'))
      expect(text('total-gross')).toBe('+24,60 PLN')
      expect(container.textContent).toContain('Nabywca dopłaca')
      expect(container.querySelector('#due-date')).not.toBeNull()

      await act(async () => typeInto(byId('line-0-unitNetPrice'), '100'))
      await act(async () => button('Dodaj pozycję')!.click())
      await act(async () => (container.querySelector('[aria-label="Usuń pozycję 1"]') as HTMLButtonElement).click())
      expect(text('total-gross')).toBe('-123,00 PLN')
      await act(async () => button('Przywróć')!.click())
      expect(text('total-gross')).toBe('0,00 PLN')
    })

    it('data-only buyer correction sends the new address with the same NIP', async () => {
      serve(original(), correction())
      vi.mocked(updateCorrection).mockResolvedValue(correction({ version: 2 }))
      await render('/issued/k-1/correct?tenantId=t-1')
      await waitFor(() => Boolean(container.querySelector('#buyer-address-1')))
      await act(async () => typeInto(byId('buyer-address-1'), 'ul. Prosta 2'))
      await act(async () => typeInto(byId('correction-reason'), 'Błąd w adresie'))
      await act(async () => typeInto(byId('correction-type'), '2'))
      expect(container.querySelector('[data-testid="correction-rows"]')).toBeNull()
      await act(async () => button('Zapisz szkic')!.click())
      await waitFor(() => vi.mocked(updateCorrection).mock.calls.length === 1)
      expect(vi.mocked(updateCorrection).mock.calls[0][2]).toMatchObject({
        correctionReason: 'Błąd w adresie',
        correctionType: 2,
        buyer: { nip: '5555555555', addressLine1: 'ul. Prosta 2' },
      })
    })

    it('shows 422 field messages from the server and issues with invoiceType KOR', async () => {
      serve(original(), correction())
      const envelope = { errors: [{ code: 'INV-VAL-084', field: 'correctionReason', message: 'Podaj przyczynę korekty' }], warnings: [] }
      vi.mocked(updateCorrection)
        .mockRejectedValueOnce(new ApiError('x', 422, new Response(JSON.stringify(envelope), { status: 422, headers: { 'Content-Type': 'application/json' } })))
        .mockResolvedValue(correction({ version: 2 }))
      vi.mocked(issueIssuedInvoice).mockResolvedValue(correction({ status: 'Sending', number: 'KOR/1/11/2026' }))
      await render('/issued/k-1/correct?tenantId=t-1')
      await waitFor(() => Boolean(button('Wystaw korektę')))
      await act(async () => button('Wystaw korektę')!.click())
      await waitFor(() => Boolean(container.querySelector('#correction-reason-error')))
      expect(container.querySelector('#correction-reason-error')?.textContent).toBe('Podaj przyczynę korekty')
      expect(issueIssuedInvoice).not.toHaveBeenCalled()

      await act(async () => button('Rabat')!.click())
      await act(async () => button('Wystaw korektę')!.click())
      await waitFor(() => vi.mocked(issueIssuedInvoice).mock.calls.length === 1)
      expect(vi.mocked(issueIssuedInvoice).mock.calls[0]).toEqual(['t-1', 'k-1', 2, 3, undefined, 'KOR'])
    })

    it('shows the server message of a 503 on issue as is', async () => {
      serve(original(), correction())
      vi.mocked(updateCorrection).mockResolvedValue(correction({ version: 2 }))
      vi.mocked(issueIssuedInvoice).mockRejectedValue(conflict('KSeF chwilowo niedostępny. Spróbuj za kilka minut.', 'INV-KSEF-UNAVAILABLE', 503))
      await render('/issued/k-1/correct?tenantId=t-1')
      await waitFor(() => Boolean(button('Wystaw korektę')))
      await act(async () => button('Wystaw korektę')!.click())
      await waitFor(() => (container.textContent ?? '').includes('KSeF chwilowo niedostępny. Spróbuj za kilka minut.'))
    })

    it('GBP np I correction keeps the original currency and rate read-only', async () => {
      const foreign = {
        buyer: { kind: 'Foreign' as const, nip: null, name: 'Example Ltd', addressLine1: '1 Sample Street', addressLine2: 'London', countryCode: 'GB', idType: 'Other' as const, foreignId: 'GB123' },
        currency: 'GBP',
        exchangeRate: 5.1234,
        lines: [{ ...line, vatRate: 'np I' as const, quantity: 10 }],
      }
      serve(original(foreign), correction({ ...foreign, lines: [{ ...foreign.lines[0], baseLineNo: 1 }] }))
      await render('/issued/k-1/correct?tenantId=t-1')
      await waitFor(() => Boolean(container.querySelector('#line-0-quantity')))
      expect(text('buyer-locked')).toContain('GB123')
      expect(text('buyer-locked')).toContain('GBP · kurs 5,1234 PLN')
      expect(container.querySelector('#buyer-country')).toBeNull()
      await act(async () => typeInto(byId('line-0-quantity'), '8'))
      expect(text('total-gross')).toBe('-200,00 GBP')
    })
  })
})
