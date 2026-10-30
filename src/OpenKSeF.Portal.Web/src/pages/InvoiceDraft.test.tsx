// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { createIssuedInvoice, getIssuedInvoice, listIssuedInvoices } from '@/api/endpoints/issuedInvoices'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { listTenants } from '@/api/endpoints/tenants'
import { normalizeError } from '@/api/errors'
import type { IssuedInvoiceResponse, SellerProfileResponse } from '@/api/types'
import { InvoiceDraftPage } from './InvoiceDraft'
import { IssuedInvoiceListPage } from './IssuedInvoiceList'

vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))
vi.mock('@/api/endpoints/sellerProfile', () => ({ getSellerProfile: vi.fn() }))
vi.mock('@/api/endpoints/nipLookup', () => ({ lookupNip: vi.fn() }))
vi.mock('@/api/endpoints/issuedInvoices', () => ({
  createIssuedInvoice: vi.fn(),
  updateIssuedInvoice: vi.fn(),
  getIssuedInvoice: vi.fn(),
  listIssuedInvoices: vi.fn(),
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
  ksefEnvironment: 'test',
}

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

const buttonByText = (container: HTMLElement, text: string) =>
  [...container.querySelectorAll('button')].find((button) => button.textContent?.includes(text))!

describe('issuing pages', () => {
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
              <Route path="/issued" element={<IssuedInvoiceListPage />} />
              <Route path="/issued/new" element={<InvoiceDraftPage />} />
              <Route path="/issued/:id/edit" element={<InvoiceDraftPage />} />
            </Routes>
          </MemoryRouter>
        </QueryClientProvider>,
      ),
    )
  }

  it('new draft shows the read-only seller, live totals and the number preview', async () => {
    await render('/issued/new?tenantId=t-1')
    await waitFor(() => Boolean(container.querySelector('#line-0-unitNetPrice')))

    expect(container.textContent).toContain('OpenKSeF Test A sp. z o.o.')
    expect(container.textContent).toContain('61 1090 1014 0000 0712 1981 2874')
    expect(container.querySelector('#seller-name')).toBeNull()
    expect(container.textContent).toContain('FV/1/11/2026')

    await act(async () => typeInto(container.querySelector<HTMLInputElement>('#line-0-unitNetPrice')!, '100'))
    expect(container.querySelector('[data-testid="total-gross"]')?.textContent?.replace(/\s/g, ' ')).toBe('123,00 PLN')
  })

  it('Osoba prywatna hides NIP and keeps name and address required', async () => {
    await render('/issued/new?tenantId=t-1')
    await waitFor(() => Boolean(container.querySelector('#buyer-nip')))

    await act(async () => buttonByText(container, 'Osoba prywatna').click())

    expect(container.querySelector('#buyer-nip')).toBeNull()
    for (const id of ['#buyer-name', '#buyer-addressLine1', '#buyer-addressLine2']) {
      expect(container.querySelector(id)?.getAttribute('aria-required')).toBe('true')
    }
  })

  it('zw line reveals the exemption basis with the art. 113 preset', async () => {
    await render('/issued/new?tenantId=t-1')
    await waitFor(() => Boolean(container.querySelector('#line-0-vatRate')))

    const select = container.querySelector<HTMLSelectElement>('#line-0-vatRate')!
    await act(async () => {
      select.value = 'zw'
      select.dispatchEvent(new Event('change', { bubbles: true }))
    })

    const basis = container.querySelector<HTMLSelectElement>('#exemption-choice')
    expect(basis?.selectedOptions[0].textContent).toContain('art. 113 ust. 1 ustawy o VAT')
  })

  it('saves dates unchanged and shows server errors inline', async () => {
    const body = { errors: [{ code: 'INV-VAL-020', field: 'buyer.nip', message: 'Dla nabywcy firmy wymagany jest poprawny NIP' }], warnings: [] }
    vi.mocked(createIssuedInvoice).mockRejectedValue(
      await normalizeError(new Response(JSON.stringify(body), { status: 400, headers: { 'Content-Type': 'application/json' } })),
    )
    await render('/issued/new?tenantId=t-1')
    await waitFor(() => Boolean(container.querySelector('#issue-date')))

    await act(async () => typeInto(container.querySelector<HTMLInputElement>('#issue-date')!, '2026-11-01'))
    await act(async () => buttonByText(container, 'Zapisz szkic').click())
    await waitFor(() => container.querySelector('#buyer-nip-error') !== null)

    const request = vi.mocked(createIssuedInvoice).mock.calls[0][1]
    expect(request.issueDate).toBe('2026-11-01')
    expect(request.saleDate).toBe('2026-11-01')
    expect(container.querySelector('#buyer-nip')?.getAttribute('aria-invalid')).toBe('true')
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('wymagany jest poprawny NIP')
  })

  it('edit loads the saved draft', async () => {
    vi.mocked(getIssuedInvoice).mockResolvedValue({
      id: 'i-1',
      status: 'Draft',
      number: null,
      buyer: { kind: 'Company', nip: '5260250274', name: 'Nabywca', addressLine1: 'a', addressLine2: 'b' },
      issueDate: '2026-11-01',
      saleDate: '2026-10-29',
      dueDate: '2026-11-15',
      paymentMethod: 'Cash',
      splitPayment: false,
      exemptionBasis: null,
      lines: [{ lineNo: 1, name: 'Usługa', unit: 'szt.', quantity: 1, unitNetPrice: 100, vatRate: '23', netAmount: 100, vatAmount: 23 }],
      warnings: [],
      updatedAt: '2026-11-01T10:00:00Z',
    } as unknown as IssuedInvoiceResponse)
    await render('/issued/i-1/edit?tenantId=t-1')
    await waitFor(() => container.querySelector<HTMLInputElement>('#buyer-name')?.value === 'Nabywca')

    expect(container.querySelector<HTMLInputElement>('#issue-date')?.value).toBe('2026-11-01')
    expect(container.querySelector<HTMLInputElement>('#sale-date')?.value).toBe('2026-10-29')
    expect(buttonByText(container, 'Gotówka').getAttribute('aria-pressed')).toBe('true')
    expect(container.textContent).toContain('Edycja szkicu')
  })

  it('list shows issued invoices and drafts', async () => {
    vi.mocked(listIssuedInvoices).mockResolvedValue({
      items: [
        { id: 'i-1', status: 'Draft', number: null, buyerName: 'Nabywca', buyerNip: '5260250274', issueDate: '2026-11-01', dueDate: '2026-11-15', totalNet: 100, totalGross: 123, currency: 'PLN', ksefNumber: null },
        { id: 'i-2', status: 'Accepted', number: 'FV/1/11/2026', buyerName: 'Inny', buyerNip: null, issueDate: '2026-11-02', dueDate: '2026-11-16', totalNet: 12327.64, totalGross: 15162, currency: 'PLN', ksefNumber: '5260250274-20261102-ABC' },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 2,
      totalPages: 1,
    })
    await render('/issued?tenantId=t-1')
    await waitFor(() => container.querySelectorAll('tbody tr').length === 2)

    const text = container.textContent?.replace(/\s/g, ' ') ?? ''
    expect(text).toContain('— (szkic)')
    expect(text).toContain('FV/1/11/2026')
    expect(text).toContain('15 162,00 PLN')
    expect(text).toContain('02.11.2026')
    expect(text).toContain('16.11.2026')
    expect(text).toContain('12 327,64 PLN')
    expect(container.querySelector('a[href^="/issued/i-2?"]')).not.toBeNull()
    expect(text).toContain('Przyjęta w KSeF')

    await act(async () => buttonByText(container, 'Szkice').click())
    await waitFor(() => vi.mocked(listIssuedInvoices).mock.calls.some((call) => call[1]?.status === 'Draft'))
  })

  it('list shows the empty state', async () => {
    vi.mocked(listIssuedInvoices).mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 })
    await render('/issued?tenantId=t-1')
    await waitFor(() => container.textContent?.includes('Nie wystawiono jeszcze faktur') ?? false)
  })
})
