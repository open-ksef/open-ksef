// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { lookupNip } from '@/api/endpoints/nipLookup'
import { getSellerProfile, updateSellerProfile } from '@/api/endpoints/sellerProfile'
import { listTenants } from '@/api/endpoints/tenants'
import { normalizeError } from '@/api/errors'
import type { SellerProfileResponse } from '@/api/types'
import { CompanyCard } from '@/company/CompanyCard'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { InvoiceDraftPage } from './InvoiceDraft'
import { SellerProfilePage } from './SellerProfile'

vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))
vi.mock('@/api/endpoints/sellerProfile', () => ({ getSellerProfile: vi.fn(), updateSellerProfile: vi.fn() }))
vi.mock('@/api/endpoints/nipLookup', () => ({ lookupNip: vi.fn() }))
vi.mock('@/api/endpoints/issuedInvoices', () => ({ getIssuedInvoice: vi.fn() }))

const empty: SellerProfileResponse = {
  legalName: null,
  addressLine1: null,
  addressLine2: null,
  bankAccount: null,
  cashAccounting: false,
  numberingPattern: 'FV/{nr}/{MM}/{RRRR}',
  nextNumber: 1,
  nextNumberPeriod: '2026-11',
  nip: '5260250274',
  isComplete: false,
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

const input = (container: HTMLElement, id: string) => container.querySelector<HTMLInputElement>(`#${id}`)!

const apiError = async (status: number, body?: unknown) =>
  normalizeError(
    new Response(body ? JSON.stringify(body) : null, { status, headers: { 'Content-Type': 'application/json' } }),
  )

describe('SellerProfilePage', () => {
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
    vi.mocked(getSellerProfile).mockResolvedValue(empty)
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
              <CompanyCard />
              <Routes>
                <Route path="/company/invoice-data" element={<SellerProfilePage />} />
                <Route path="/issued/new" element={<InvoiceDraftPage />} />
              </Routes>
            </CompanyProvider>
          </MemoryRouter>
        </QueryClientProvider>,
      ),
    )
    await waitFor(() => Boolean(container.querySelector('#legal-name')))
  }

  it('edits either company after choosing it in the company card', async () => {
    vi.mocked(listTenants).mockResolvedValue([
      { id: 't-1', nip: '5260250274', displayName: 'A', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' },
      { id: 't-2', nip: '7740001454', displayName: 'B', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' },
    ])
    vi.mocked(getSellerProfile).mockImplementation(async (id) => ({
      ...empty,
      legalName: id === 't-1' ? 'Sprzedawca A' : 'Sprzedawca B',
      addressLine1: 'ul. Prosta 1',
      addressLine2: '00-001 Warszawa',
      bankAccount: '61109010140000071219812874',
    }))
    vi.mocked(updateSellerProfile).mockResolvedValue({ ...empty, isComplete: true })
    await render('/company/invoice-data')
    await waitFor(() => input(container, 'legal-name').value === 'Sprzedawca A')

    await act(async () => container.querySelector<HTMLElement>('[data-testid="company-card"]')!.click())
    await waitFor(() => Boolean(container.querySelector('[data-testid="company-option-t-2"]')))
    await act(async () => container.querySelector<HTMLElement>('[data-testid="company-option-t-2"]')!.click())
    await waitFor(() => input(container, 'legal-name')?.value === 'Sprzedawca B')

    await act(async () => buttonByText(container, 'Zapisz dane').click())
    await waitFor(() => vi.mocked(updateSellerProfile).mock.calls.length === 1)
    expect(vi.mocked(updateSellerProfile).mock.calls[0][0]).toBe('t-2')
    expect(container.querySelector('[data-testid="company-card"]')?.textContent).toContain('NIP 7740001454')
  })

  it.each([
    [false, 'Wpisana nazwa'],
    [true, 'Z białej listy'],
  ])('NIP lookup over typed data asks first, confirmed %s', async (confirmed, expected) => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(confirmed)
    vi.mocked(getSellerProfile).mockResolvedValue({ ...empty, legalName: 'Wpisana nazwa' })
    vi.mocked(lookupNip).mockResolvedValue({ nip: '5260250274', name: 'Z białej listy', addressLine1: null, addressLine2: null, vatStatus: 'Czynny', bankAccounts: [] })
    await render('/company/invoice-data?tenantId=t-1')
    await waitFor(() => input(container, 'legal-name').value === 'Wpisana nazwa')

    await act(async () => buttonByText(container, 'Pobierz z NIP').click())
    await waitFor(() => confirm.mock.calls.length === 1)
    await waitFor(() => input(container, 'legal-name').value === expected)
    confirm.mockRestore()
  })

  it('new invoice with an incomplete profile lands here with the prompt', async () => {
    await render('/issued/new?tenantId=t-1')

    expect(container.textContent).toContain('Uzupełnij dane firmy, aby wystawiać faktury')
    expect(container.textContent).toContain('Następna faktura otrzyma numer FV/1/11/2026')
  })

  it('Pobierz z NIP fills the fields and offers white-list accounts grouped', async () => {
    vi.mocked(lookupNip).mockResolvedValue({
      nip: '5260250274',
      name: 'Firma Testowa sp. z o.o.',
      addressLine1: 'ul. Prosta 1',
      addressLine2: '00-001 Warszawa',
      vatStatus: 'Czynny',
      bankAccounts: ['61109010140000071219812874', '65114020040000330201234567'],
    })
    vi.mocked(updateSellerProfile).mockResolvedValue({ ...empty, isComplete: true })
    await render('/company/invoice-data?tenantId=t-1')

    expect(input(container, 'seller-nip').readOnly).toBe(true)
    await act(async () => buttonByText(container, 'Pobierz z NIP').click())
    await waitFor(() => input(container, 'legal-name').value === 'Firma Testowa sp. z o.o.')

    expect(input(container, 'address-line-1').value).toBe('ul. Prosta 1')
    expect(container.textContent).toContain('61 1090 1014 0000 0712 1981 2874')
    const radios = container.querySelectorAll<HTMLInputElement>('input[name="bank-account"]')
    expect(radios).toHaveLength(3)
    expect(radios[0].checked).toBe(true)

    await act(async () => radios[1].click())
    await act(async () => buttonByText(container, 'Zapisz dane').click())
    await waitFor(() => vi.mocked(updateSellerProfile).mock.calls.length === 1)
    expect(vi.mocked(updateSellerProfile).mock.calls[0][1]).toMatchObject({
      legalName: 'Firma Testowa sp. z o.o.',
      bankAccount: '65114020040000330201234567',
    })
  })

  it.each([
    [404, 'Nie ma na białej liście'],
    [503, 'Biała lista nie odpowiada'],
  ])('lookup %i shows a message and keeps manual entry', async (status, message) => {
    vi.mocked(lookupNip).mockRejectedValue(await apiError(status))
    await render('/company/invoice-data?tenantId=t-1')

    await act(async () => buttonByText(container, 'Pobierz z NIP').click())
    await waitFor(() => container.textContent?.includes(message) ?? false)

    await act(async () => typeInto(input(container, 'legal-name'), 'Ręcznie'))
    expect(input(container, 'legal-name').value).toBe('Ręcznie')
    expect(input(container, 'bank-account')).not.toBeNull()
  })

  it('blocks save on a bad NRB and shows API validation inline', async () => {
    vi.mocked(updateSellerProfile).mockRejectedValue(
      await apiError(400, { errors: [{ code: 'SP-VAL-001', field: 'bankAccount', message: 'Niepoprawny numer rachunku (API)' }], warnings: [] }),
    )
    await render('/company/invoice-data?tenantId=t-1')
    await act(async () => {
      typeInto(input(container, 'legal-name'), 'Firma')
      typeInto(input(container, 'address-line-1'), 'ul. Prosta 1')
      typeInto(input(container, 'bank-account'), '61 1090 1014 0000 0712 1981 2875')
    })

    await act(async () => buttonByText(container, 'Zapisz dane').click())
    expect(container.querySelector('#bank-account-error')?.textContent).toBe('Niepoprawny numer rachunku')
    expect(updateSellerProfile).not.toHaveBeenCalled()

    await act(async () => typeInto(input(container, 'bank-account'), '61 1090 1014 0000 0712 1981 2874'))
    await act(async () => buttonByText(container, 'Zapisz dane').click())
    await waitFor(() => container.querySelector('#bank-account-error')?.textContent === 'Niepoprawny numer rachunku (API)')
    expect(input(container, 'bank-account').getAttribute('aria-invalid')).toBe('true')
  })

  it('shows SP-VAL-007 inline on the numbering pattern', async () => {
    const message = 'Wzorzec numeracji musi zawierać {RRRR}, inaczej numery powtórzą się w kolejnym roku'
    vi.mocked(updateSellerProfile).mockRejectedValue(
      await apiError(400, { errors: [{ code: 'SP-VAL-007', field: 'numberingPattern', message }], warnings: [] }),
    )
    await render('/company/invoice-data?tenantId=t-1')
    await act(async () => {
      typeInto(input(container, 'legal-name'), 'Firma')
      typeInto(input(container, 'address-line-1'), 'ul. Prosta 1')
      typeInto(input(container, 'bank-account'), '61 1090 1014 0000 0712 1981 2874')
      typeInto(input(container, 'numbering-pattern'), 'FV/{nr}')
    })

    await act(async () => buttonByText(container, 'Zapisz dane').click())
    await waitFor(() => container.querySelector('#numbering-pattern-error')?.textContent === message)
    expect(input(container, 'numbering-pattern').getAttribute('aria-invalid')).toBe('true')
  })

  it('updates the number preview when the pattern changes', async () => {
    await render('/company/invoice-data?tenantId=t-1')
    await act(async () => {
      typeInto(input(container, 'numbering-pattern'), 'FS/{RRRR}/')
      typeInto(input(container, 'next-number'), '7')
    })
    await act(async () => buttonByText(container, '{nr}').click())

    expect(input(container, 'numbering-pattern').value).toBe('FS/{RRRR}/{nr}')
    expect(container.textContent).toMatch(/Następna faktura otrzyma numer FS\/\d{4}\/7/)
  })
})
