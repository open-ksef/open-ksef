// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { listCredentials } from '@/api/endpoints/credentials'
import { listIssuedFeed } from '@/api/endpoints/issuedInvoices'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { listTenants } from '@/api/endpoints/tenants'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { IssuedInvoiceListPage } from './IssuedInvoiceList'

vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))
vi.mock('@/api/endpoints/credentials', () => ({ listCredentials: vi.fn() }))
vi.mock('@/api/endpoints/issuedInvoices', () => ({ listIssuedFeed: vi.fn() }))
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

  async function render(result: object, path = '/issued?tenantId=a'): Promise<void> {
    vi.mocked(listIssuedFeed).mockResolvedValue(result as never)
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter(
      [
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
    vi.mocked(listTenants).mockResolvedValue([{ id: 'a', nip: '1111111111', displayName: 'Firma A', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' }])
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
