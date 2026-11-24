// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { listCredentials } from '@/api/endpoints/credentials'
import { copyIssuedInvoice, listIssuedFeed } from '@/api/endpoints/issuedInvoices'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { listTenants } from '@/api/endpoints/tenants'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { IssuedInvoiceListPage } from './IssuedInvoiceList'
import { ownerAccess } from '@/test/tenant'

vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))
vi.mock('@/api/endpoints/credentials', () => ({ listCredentials: vi.fn() }))
vi.mock('@/api/endpoints/issuedInvoices', () => ({ listIssuedFeed: vi.fn(), copyIssuedInvoice: vi.fn() }))
vi.mock('@/api/endpoints/sellerProfile', () => ({ getSellerProfile: vi.fn() }))

const NOTE = 'Ostatnie pobieranie faktur sprzedaży z KSeF nie objęło całego okresu. Lista może być niepełna.'
const NO_INVOICES = 'Nie masz jeszcze wystawionych faktur'
const NO_MATCHES = 'Brak faktur spełniających kryteria'
const flush = () => new Promise<void>((resolve) => setTimeout(resolve, 0))
const invoice = { id: 'i1', source: 'External', ksefNumber: 'K1', number: 'FV/1', buyerName: 'Nabywca', issueDate: '2026-11-01', dueDate: null, totalNet: 100, totalGross: 123, currency: 'PLN', status: 'Accepted' }
const feed = (items: object[], flag?: object) => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1, ...flag })

describe('Wystawione faktury, niepełne pobieranie', () => {
  let root: Root
  let container: HTMLDivElement

  let router: ReturnType<typeof createMemoryRouter>

  async function render(result: object, path = '/issued?tenantId=a'): Promise<void> {
    vi.mocked(listIssuedFeed).mockResolvedValue(result as never)
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    router = createMemoryRouter(
      [
        { path: '/issued/:id/edit', element: <p>Formularz szkicu</p> },
        { path: '/issued/:id', element: <p>Szczegóły faktury</p> },
        {
          path: '*',
          element: (
            <QueryClientProvider client={client}>
              <CompanyProvider>
                <IssuedInvoiceListPage />
              </CompanyProvider>
            </QueryClientProvider>
          ),
        },
      ],
      { initialEntries: [path] },
    )
    await act(async () => root.render(<RouterProvider router={router} />))
    for (let tick = 0; tick < 8; tick++) await act(flush)
  }

  const note = () => container.querySelector('[data-testid="issued-fetch-incomplete"]')
  const table = () => container.querySelector('[data-testid="issued-invoice-table"]')

  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('localStorage', memoryStorage())
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    vi.mocked(listTenants).mockResolvedValue([{ id: 'a', nip: '1111111111', displayName: 'Firma A', notificationEmail: null, ...ownerAccess, createdAt: '2026-01-01T00:00:00Z' }])
    vi.mocked(listCredentials).mockResolvedValue([{ tenantId: 'a', tenantDisplayName: 'Firma A', hasCredential: true, credentialType: 'Token', lastUpdatedAt: '2026-09-14T10:00:00Z' }])
    vi.mocked(getSellerProfile).mockResolvedValue({ ksefEnvironment: 'test' } as never)
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  it('shows an info note above the list, the list stays', async () => {
    await render(feed([invoice], { issuedFetchIncomplete: true }))

    expect(note()?.textContent).toBe(NOTE)
    expect(note()?.getAttribute('role')).toBe('status')
    expect(note()?.className).toContain('inv-alert--info')
    expect(table()?.textContent).toContain('Nabywca')
  })

  it('an empty list with the flag shows the note instead of claiming no invoices', async () => {
    await render(feed([], { issuedFetchIncomplete: true }))

    expect(note()?.textContent).toBe(NOTE)
    expect(container.textContent).not.toContain(NO_INVOICES)
    expect(container.textContent).not.toContain(NO_MATCHES)
    expect(container.querySelector('.ui-pagination')).toBeNull()
  })

  it('an empty list with the flag and no token says the token is missing and links the token tab', async () => {
    vi.mocked(listCredentials).mockResolvedValue([{ tenantId: 'a', tenantDisplayName: 'Firma A', hasCredential: false, credentialType: null, lastUpdatedAt: null }])
    await render(feed([], { issuedFetchIncomplete: true }))

    expect(container.textContent).toContain('Brakuje tokenu KSeF')
    expect(container.textContent).toContain('Bez tokenu nie pobierzemy faktur z KSeF')
    expect(container.querySelector('.ui-empty-state a[href="/company/token"]')).not.toBeNull()
    expect(container.textContent).not.toContain(NO_INVOICES)
    expect(note()?.textContent).toBe(NOTE)
  })

  it('an empty list without the flag and without a token does not claim no invoices either', async () => {
    vi.mocked(listCredentials).mockResolvedValue([{ tenantId: 'a', tenantDisplayName: 'Firma A', hasCredential: false, credentialType: null, lastUpdatedAt: null }])
    await render(feed([], { issuedFetchIncomplete: false }))

    expect(container.textContent).toContain('Brakuje tokenu KSeF')
    expect(container.textContent).not.toContain(NO_INVOICES)
  })

  it('a filter with no match and the flag shows the no-match message and the note', async () => {
    await render(feed([], { issuedFetchIncomplete: true }), '/issued?tenantId=a&q=NIEMA-TAKIEJ')

    expect(note()?.textContent).toBe(NOTE)
    expect(container.textContent).toContain(NO_MATCHES)
    expect(container.querySelector('#issued-q')).not.toBeNull()
  })

  it('a status filter with no match and the flag shows the no-match message', async () => {
    await render(feed([], { issuedFetchIncomplete: true }), '/issued?tenantId=a&status=Rejected')

    expect(container.textContent).toContain(NO_MATCHES)
  })

  it('a complete fetch shows no note and keeps the empty states', async () => {
    await render(feed([], { issuedFetchIncomplete: false }))

    expect(note()).toBeNull()
    expect(container.textContent).toContain(NO_INVOICES)
  })

  it('a filtered empty list on a complete fetch still says no matches', async () => {
    await render(feed([], { issuedFetchIncomplete: false }), '/issued?tenantId=a&status=Rejected')

    expect(note()).toBeNull()
    expect(container.textContent).toContain(NO_MATCHES)
  })

  it('a feed without the flag, as from an older API, shows no note', async () => {
    await render(feed([invoice]))

    expect(note()).toBeNull()
    expect(table()).not.toBeNull()
  })
  const rowLink = (index = 0) => container.querySelectorAll('[data-testid="issued-invoice-table"] a.rowlink')[index]
  const button = (label: string) => [...container.querySelectorAll('button')].find((b) => b.textContent === label) as HTMLButtonElement

  it('offers Kopiuj jako nową only on Accepted VAT rows issued in OpenKSeF; a click opens the new draft form, not the details', async () => {
    const own = { ...invoice, source: 'OpenKSeF', ksefNumber: 'K2' }
    await render(
      feed([
        { ...own, id: 'vat', buyerName: 'VAT przyjęta' },
        { ...own, id: 'vat-old', buyerName: 'VAT bez rodzaju', invoiceType: undefined },
        { ...invoice, id: 'ext', buyerName: 'Spoza' },
        { ...own, id: 'draft', status: 'Draft', number: null },
        { ...own, id: 'sending', status: 'Sending' },
        { ...own, id: 'rejected', status: 'Rejected' },
        ...['KOR', 'ZAL', 'ROZ', 'KOR_ZAL', 'KOR_ROZ'].map((invoiceType) => ({ ...own, id: invoiceType, invoiceType })),
      ]),
    )
    const copyButtons = [...container.querySelectorAll<HTMLButtonElement>('button[aria-label="Kopiuj jako nową"]')]
    expect(copyButtons.map((button) => button.closest('tr')?.textContent?.split('FV/1')[0])).toEqual(['VAT przyjęta', 'VAT bez rodzaju'])

    vi.mocked(copyIssuedInvoice).mockResolvedValue({ id: 'new-1' } as never)
    await act(async () => copyButtons[0].click())
    for (let tick = 0; tick < 4; tick++) await act(flush)
    await act(() => new Promise<void>((resolve) => setTimeout(resolve, 300)))

    expect(copyIssuedInvoice).toHaveBeenCalledWith('a', 'vat')
    expect(router.state.location.pathname).toBe('/issued/new-1/edit')
    expect(container.textContent).toContain('Formularz szkicu')
  })

  it('a failed copy stays on the list with the error', async () => {
    vi.mocked(copyIssuedInvoice).mockRejectedValue(new Error('Nie można skopiować'))
    await render(feed([{ ...invoice, source: 'OpenKSeF', invoiceType: 'VAT' }]))
    await act(async () => container.querySelector<HTMLButtonElement>('button[aria-label="Kopiuj jako nową"]')!.click())
    for (let tick = 0; tick < 4; tick++) await act(flush)
    expect(container.textContent).toContain('Nie można skopiować')
    expect(router.state.location.pathname).toBe('/issued')
  })

  it('links the template list', async () => {
    await render(feed([invoice]))
    expect(container.querySelector('a[href="/issued/templates?tenantId=a"]')?.textContent).toContain('Szablony')
  })

  it('labels only a draft without a number as szkic', async () => {
    const own = { ...invoice, source: 'OpenKSeF', ksefNumber: null, number: null }
    await render(feed([{ ...own, id: 'd', status: 'Draft' }, { ...own, id: 's', status: 'Sending' }]))

    expect(rowLink(0).getAttribute('aria-label')).toBe('Nabywca, — (szkic)')
    expect(rowLink(1).getAttribute('aria-label')).toBe('Nabywca, —')
  })

  it('sends the KSeF number search to the feed', async () => {
    await render(feed([{ ...invoice, source: 'OpenKSeF' }]), '/issued?tenantId=a&q=1111111111-20261101-ABC')

    expect(listIssuedFeed).toHaveBeenCalledWith('a', expect.objectContaining({ q: '1111111111-20261101-ABC' }))
  })

  it('names the external origin in the row link for keyboard users', async () => {
    await render(feed([invoice, { ...invoice, id: 'o', source: 'OpenKSeF' }]))

    expect(rowLink(0).getAttribute('aria-label')).toBe('Nabywca, FV/1, Spoza OpenKSeF: Faktura wystawiona w innym programie, tylko do odczytu')
    expect(rowLink(1).getAttribute('aria-label')).toBe('Nabywca, FV/1')
  })

  it('marks a correction as Korekta with a signed amount; VAT rows stay unsigned', async () => {
    const own = { ...invoice, source: 'OpenKSeF' }
    await render(feed([{ ...own, id: 'k', number: 'KOR/1/11/2026', invoiceType: 'KOR', totalNet: -100, totalGross: -123 }, { ...own, id: 'v', invoiceType: 'VAT' }]))

    expect(rowLink(0).getAttribute('aria-label')).toBe('Nabywca, Korekta · KOR/1/11/2026')
    const rows = [...container.querySelectorAll('[data-testid="issued-invoice-table"] tbody tr')].map((row) => row.textContent?.replace(/\s/g, ' '))
    expect(rows[0]).toContain('Korekta · KOR/1/11/2026')
    expect(rows[0]).toContain('-123,00 PLN')
    expect(rows[1]).toContain('123,00 PLN')
    expect(rows[1]).not.toContain('+')
    expect(rows[1]).not.toContain('Korekta')
  })

  it('marks advance, final and KOR_ZAL rows by kind; a KOR_ZAL refund is signed', async () => {
    const own = { ...invoice, source: 'OpenKSeF' }
    await render(
      feed([
        { ...own, id: 'z', number: 'ZAL/1/11/2026', invoiceType: 'ZAL', totalGross: 369 },
        { ...own, id: 'r', number: 'FV/2/11/2026', invoiceType: 'ROZ', totalGross: 615 },
        { ...own, id: 'kz', number: 'KOR/2/11/2026', invoiceType: 'KOR_ZAL', totalNet: -100, totalGross: -123 },
      ]),
    )
    const rows = [...container.querySelectorAll('[data-testid="issued-invoice-table"] tbody tr')].map((row) => row.textContent?.replace(/\s/g, ' '))
    expect(rows[0]).toContain('Zaliczka · ZAL/1/11/2026')
    expect(rows[1]).toContain('Rozliczenie · FV/2/11/2026')
    expect(rows[2]).toContain('Korekta · KOR/2/11/2026')
    expect(rows[2]).toContain('-123,00 PLN')
  })

  it('stops at row 10 000 with the clamped page range and a narrowing hint', async () => {
    const items = Array.from({ length: 20 }, (_, i) => ({ ...invoice, id: `r${i}` }))
    await render({ items, page: 500, pageSize: 20, totalCount: 12_000, totalPages: 600 }, '/issued?tenantId=a&page=600')

    expect(container.querySelector('.ui-pagination-info')?.textContent).toBe('9981–10000 z 12000 faktur')
    expect(button('Następna').disabled).toBe(true)
    expect(button('Poprzednia').disabled).toBe(false)
    expect(container.querySelector('[data-testid="issued-cap-note"]')?.textContent).toBe('Pokazujemy pierwsze 10 000 faktur. Zawęź wyniki miesiącem lub wyszukiwaniem.')
  })

  it('shows no narrowing hint up to 10 000 rows', async () => {
    await render(feed([invoice]))

    expect(container.querySelector('[data-testid="issued-cap-note"]')).toBeNull()
  })
})
