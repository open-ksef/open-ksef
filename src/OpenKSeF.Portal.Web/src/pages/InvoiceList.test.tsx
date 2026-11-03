// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import toast from 'react-hot-toast'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { forceCredentialSync, listCredentials } from '@/api/endpoints/credentials'
import { getDashboardOverview } from '@/api/endpoints/dashboard'
import { listInvoices } from '@/api/endpoints/invoices'
import { listIssuedInvoices } from '@/api/endpoints/issuedInvoices'
import { listTenants } from '@/api/endpoints/tenants'
import { ApiError } from '@/api/errors'
import type { InvoiceResponse, TenantCredentialStatusResponse, TenantDashboardSummaryResponse } from '@/api/types'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { InvoiceListPage } from './InvoiceList'

vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))
vi.mock('@/api/endpoints/invoices', () => ({ listInvoices: vi.fn() }))
vi.mock('@/api/endpoints/dashboard', () => ({ getDashboardOverview: vi.fn() }))
vi.mock('@/api/endpoints/credentials', () => ({ forceCredentialSync: vi.fn(), listCredentials: vi.fn() }))
vi.mock('@/api/endpoints/issuedInvoices', () => ({ listIssuedInvoices: vi.fn() }))
vi.mock('react-hot-toast', () => ({ default: { error: vi.fn(), success: vi.fn() } }))

const SEARCH_WAIT_MS = 340

const flush = () => new Promise<void>((resolve) => setTimeout(resolve, 0))
const wait = (ms: number) => act(async () => void (await new Promise((resolve) => setTimeout(resolve, ms))))
const settle = async () => {
  for (let tick = 0; tick < 5; tick++) await wait(0)
}

const invoice = (overrides: Partial<InvoiceResponse> = {}): InvoiceResponse => ({
  id: 'inv-1',
  ksefInvoiceNumber: 'KSEF-001',
  ksefReferenceNumber: 'REF-001',
  invoiceNumber: 'FV/2026/001',
  vendorName: 'Vendor A',
  vendorNip: '9876543210',
  buyerName: null,
  buyerNip: null,
  amountNet: 1200,
  amountVat: 276,
  amountGross: 1476,
  currency: 'PLN',
  issueDate: '2026-02-01T00:00:00Z',
  acquisitionDate: null,
  invoiceType: null,
  firstSeenAt: '2026-02-01T12:00:00Z',
  isPaid: false,
  paidAt: null,
  ...overrides,
})

const summary = (overrides: Partial<TenantDashboardSummaryResponse>): TenantDashboardSummaryResponse => ({
  tenantId: 'tenant-1',
  nip: '1234567890',
  displayName: 'Tenant A',
  lastSyncedAt: null,
  lastSuccessfulSync: '2026-11-01T06:00:00',
  totalInvoices: 1,
  invoicesLast7Days: 1,
  invoicesLast30Days: 1,
  syncStatus: 'Success',
  ...overrides,
})

const credential = (overrides: Partial<TenantCredentialStatusResponse> = {}): TenantCredentialStatusResponse => ({
  tenantId: 'tenant-1',
  tenantDisplayName: 'Tenant A',
  hasCredential: true,
  credentialType: 'Token',
  lastUpdatedAt: new Date().toISOString(),
  ...overrides,
})

describe('InvoiceListPage', () => {
  let root: Root
  let container: HTMLDivElement

  const byTestId = (id: string) => container.querySelector<HTMLElement>(`[data-testid="${id}"]`)
  const input = (id: string) => byTestId(id) as HTMLInputElement
  const lastListCall = () => vi.mocked(listInvoices).mock.lastCall
  const fetchButtonNode = () => byTestId('invoice-fetch-button') as HTMLButtonElement

  function renderPage(path: string) {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const page = (
      <QueryClientProvider client={client}>
        <CompanyProvider>
          <InvoiceListPage />
        </CompanyProvider>
      </QueryClientProvider>
    )
    return createMemoryRouter(
      [
        { path: '/invoices', element: page },
        { path: '/invoices/:ksefInvoiceNumber', element: <p data-testid="details-page" /> },
      ],
      { initialEntries: [path] },
    )
  }

  async function render(path: string) {
    const router = renderPage(path)
    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flush()
      await flush()
    })
    await settle()
    return router
  }

  async function type(id: string, value: string) {
    const element = input(id)
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(element, value)
      element.dispatchEvent(new Event('input', { bubbles: true }))
    })
  }

  beforeEach(() => {
    vi.stubGlobal('localStorage', memoryStorage())
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
    vi.mocked(listTenants).mockResolvedValue([
      { id: 'tenant-1', nip: '1234567890', displayName: 'Tenant A', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' },
      { id: 'tenant-2', nip: '2222222222', displayName: 'Tenant B', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' },
    ])
    vi.mocked(listInvoices).mockResolvedValue({ items: [invoice()], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 })
    vi.mocked(getDashboardOverview).mockResolvedValue([summary({})])
    vi.mocked(listCredentials).mockResolvedValue([credential()])
    vi.mocked(listIssuedInvoices).mockResolvedValue({ items: [], page: 1, pageSize: 1, totalCount: 0, totalPages: 0 })
    vi.mocked(forceCredentialSync).mockResolvedValue({
      tenantId: 'tenant-1',
      fetchedInvoices: 3,
      newInvoices: 1,
      syncedAtUtc: '2026-11-03T10:00:00Z',
    })
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
    window.getSelection()?.removeAllRanges()
  })

  it('renders one row in the B layout, with fixed page size and no apply, refresh or page size controls', async () => {
    await render('/invoices?tenantId=tenant-1&pageSize=10')

    expect(container.querySelector('h1')?.textContent).toBe('Otrzymane faktury')
    expect(container.querySelectorAll('[data-testid="invoice-row"]').length).toBe(1)
    expect(byTestId('invoice-number')?.textContent).toBe('FV/2026/001')
    expect(byTestId('invoice-ksef-number')?.textContent).toBe('KSEF-001')
    expect(byTestId('invoice-row')?.textContent?.replace(/\s/g, ' ')).toContain('1 476,00 PLN')
    expect(byTestId('invoice-row')?.textContent).toContain('01.02.2026')
    expect(byTestId('invoice-paid-status')?.textContent).toBe('Nieopłacona')
    expect(byTestId('invoice-paid-status-mobile')?.textContent).toBe('Nieopłacona')
    expect(container.querySelector('[data-testid="invoice-row"] .received-gross [data-testid="invoice-paid-status-mobile"]')).toBeTruthy()
    expect(byTestId('invoice-page-size')).toBeNull()
    expect(byTestId('invoice-apply-filters')).toBeNull()
    expect(byTestId('invoice-refresh-button')).toBeNull()
    expect(container.textContent).toContain('1–1 z 1 faktury, od najnowszej')
    expect(lastListCall()).toEqual(['tenant-1', expect.objectContaining({ page: 1, pageSize: 25 })])
  })

  it('search sends the term, resets page to 1 and keeps the term in the URL over a reload', async () => {
    const router = await render('/invoices?tenantId=tenant-1&page=3')
    expect(lastListCall()?.[1]).toMatchObject({ page: 3 })

    await type('invoice-search', ' abc ')
    await wait(SEARCH_WAIT_MS)

    expect(router.state.location.search).toBe('?tenantId=tenant-1&q=abc')
    expect(router.state.historyAction).toBe('REPLACE')
    expect(lastListCall()?.[1]).toMatchObject({ filterByVendor: 'abc', page: 1 })

    act(() => root.unmount())
    root = createRoot(container)
    vi.mocked(listInvoices).mockClear()
    const reloaded = await render(router.state.location.pathname + router.state.location.search)

    expect(input('invoice-search').value).toBe('abc')
    expect(reloaded.state.location.search).toBe('?tenantId=tenant-1&q=abc')
    expect(lastListCall()?.[1]).toMatchObject({ filterByVendor: 'abc', page: 1 })
  })

  it('sends one search for fast typing and keeps the focused input', async () => {
    await render('/invoices?tenantId=tenant-1')
    const element = input('invoice-search')
    element.focus()

    for (const text of ['a', 'ab', 'abc']) {
      await type('invoice-search', text)
      await wait(20)
    }
    await wait(SEARCH_WAIT_MS)

    const sent = vi.mocked(listInvoices).mock.calls.map(([, params]) => params?.filterByVendor)
    expect(sent.filter(Boolean)).toEqual(['abc'])
    expect(input('invoice-search')).toBe(element)
    expect(document.activeElement).toBe(element)
  })

  it('keeps the space typed between words after the term is applied', async () => {
    const router = await render('/invoices?tenantId=tenant-1')

    await type('invoice-search', 'abc ')
    await wait(SEARCH_WAIT_MS)

    expect(router.state.location.search).toContain('q=abc')
    expect(input('invoice-search').value).toBe('abc ')
  })

  it('restores the search box when the URL changes from outside', async () => {
    const router = await render('/invoices?tenantId=tenant-1&q=abc')

    await act(async () => {
      await router.navigate('/invoices?tenantId=tenant-1&q=xyz')
    })
    await wait(SEARCH_WAIT_MS)

    expect(input('invoice-search').value).toBe('xyz')
    expect(router.state.location.search).toContain('q=xyz')
  })

  it.each(['123-456-78-90', '123 456 78 90', 'PL1234567890', 'pl 123-456-78-90'])('sends the pasted NIP %s without separators', async (term) => {
    await render('/invoices?tenantId=tenant-1')

    await type('invoice-search', term)
    await wait(SEARCH_WAIT_MS)

    expect(lastListCall()?.[1]).toMatchObject({ filterByVendor: '1234567890' })
  })

  it.each([
    ['invoice-date-from', 'dateFrom'],
    ['invoice-date-to', 'dateTo'],
  ])('%s applies at once, replaces history, resets page to 1 and keeps the rest', async (id, param) => {
    const router = await render('/invoices?tenantId=tenant-1&q=abc&page=3')

    await type(id, '2026-01-15')
    await settle()

    const search = new URLSearchParams(router.state.location.search)
    expect(search.get(param)).toBe('2026-01-15')
    expect(search.get('q')).toBe('abc')
    expect(search.get('tenantId')).toBe('tenant-1')
    expect(search.has('page')).toBe(false)
    expect(router.state.historyAction).toBe('REPLACE')
    expect(lastListCall()?.[1]).toMatchObject({ [param]: '2026-01-15', page: 1 })
    expect(input(id).value).toBe('2026-01-15')
  })

  it('does not apply a year still being typed', async () => {
    const router = await render('/invoices?tenantId=tenant-1&page=3')

    await type('invoice-date-from', '0002-01-15')
    await settle()

    expect(router.state.location.search).toBe('?tenantId=tenant-1&page=3')
    expect(input('invoice-date-from').value).toBe('0002-01-15')
  })

  it('date field follows the URL when it changes from outside', async () => {
    const router = await render('/invoices?tenantId=tenant-1&dateFrom=2026-01-15')

    await act(async () => {
      await router.navigate('/invoices?tenantId=tenant-1')
    })
    await settle()

    expect(input('invoice-date-from').value).toBe('')
  })

  it('does not add the company to the URL when filtering', async () => {
    const router = await render('/invoices')

    await type('invoice-date-from', '2026-01-15')
    await settle()

    expect(router.state.location.search).toBe('?dateFrom=2026-01-15')
    expect(lastListCall()?.[0]).toBe('tenant-1')
  })

  it('pages by URL, writing page only above 1, and counts rows of the current page', async () => {
    vi.mocked(listInvoices).mockResolvedValue({
      items: Array.from({ length: 25 }, (_, index) => invoice({ id: `inv-${index}` })),
      page: 2,
      pageSize: 25,
      totalCount: 58,
      totalPages: 3,
    })
    const router = await render('/invoices?tenantId=tenant-1&q=abc&page=2')

    expect(container.textContent).toContain('26–50 z 58 faktur, od najnowszej')

    await act(async () => byTestId('invoice-next-page')?.click())
    await settle()
    expect(router.state.location.search).toBe('?tenantId=tenant-1&q=abc&page=3')
    expect(lastListCall()?.[1]).toMatchObject({ filterByVendor: 'abc', page: 3 })

    await act(async () => {
      await router.navigate('/invoices?tenantId=tenant-1&q=abc&page=2')
    })
    await settle()
    await act(async () => byTestId('invoice-prev-page')?.click())
    await settle()
    expect(router.state.location.search).toBe('?tenantId=tenant-1&q=abc')
  })

  it('queries by company, so the same filters on another company are not served from cache', async () => {
    const router = await render('/invoices?tenantId=tenant-1&q=abc')
    expect(listInvoices).toHaveBeenCalledWith('tenant-1', expect.objectContaining({ filterByVendor: 'abc' }))

    await act(async () => {
      await router.navigate('/invoices?tenantId=tenant-2&q=abc')
    })
    await settle()

    expect(listInvoices).toHaveBeenCalledWith('tenant-2', expect.objectContaining({ filterByVendor: 'abc' }))
  })

  describe('row', () => {
    function navigations(router: ReturnType<typeof renderPage>): Set<string> {
      const keys = new Set<string>()
      router.subscribe((state) => keys.add(state.location.key))
      return keys
    }

    it('opens the invoice on a click anywhere and passes the list search to details', async () => {
      const router = await render('/invoices?tenantId=tenant-1&q=abc&page=2')

      await act(async () => (byTestId('invoice-row')?.querySelector('.received-gross') as HTMLElement).click())
      await settle()

      expect(router.state.location.pathname).toBe('/invoices/KSEF-001')
      expect(router.state.location.search).toBe('?tenantId=tenant-1')
      expect(router.state.location.state).toEqual({ listSearch: '?tenantId=tenant-1&q=abc&page=2', siblings: ['KSEF-001'] })
    })

    it('navigates once on a click on the link and keeps its URL and state', async () => {
      const router = await render('/invoices?tenantId=tenant-1&q=abc')
      const keys = navigations(router)
      const link = byTestId('invoice-view-details') as HTMLAnchorElement

      expect(link.getAttribute('href')).toBe('/invoices/KSEF-001?tenantId=tenant-1')
      expect(link.getAttribute('aria-label')).toBe('Vendor A, FV/2026/001')

      await act(async () => link.click())
      await settle()

      expect(keys.size).toBe(1)
      expect(router.state.location.pathname).toBe('/invoices/KSEF-001')
      expect(router.state.location.state).toEqual({ listSearch: '?tenantId=tenant-1&q=abc', siblings: ['KSEF-001'] })
    })

    it('passes the numbers of the current page, in row order, to details', async () => {
      vi.mocked(listInvoices).mockResolvedValue({
        items: [
          invoice({ id: 'inv-3', ksefInvoiceNumber: 'KSEF-003' }),
          invoice({ id: 'inv-1', ksefInvoiceNumber: 'KSEF-001' }),
          invoice({ id: 'inv-2', ksefInvoiceNumber: 'KSEF-002' }),
        ],
        page: 1,
        pageSize: 25,
        totalCount: 3,
        totalPages: 1,
      })
      const router = await render('/invoices?tenantId=tenant-1')

      await act(async () => container.querySelectorAll<HTMLElement>('[data-testid="invoice-view-details"]')[1].click())
      await settle()

      expect(router.state.location.pathname).toBe('/invoices/KSEF-001')
      expect(router.state.location.state).toEqual({
        listSearch: '?tenantId=tenant-1',
        siblings: ['KSEF-003', 'KSEF-001', 'KSEF-002'],
      })
    })

    it('ctrl-click on the link leaves this tab', async () => {
      const router = await render('/invoices?tenantId=tenant-1')
      const link = byTestId('invoice-view-details') as HTMLAnchorElement

      await act(async () => {
        link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0, ctrlKey: true }))
      })
      await settle()

      expect(router.state.location.pathname).toBe('/invoices')
    })

    it('does not open when text is selected', async () => {
      const router = await render('/invoices?tenantId=tenant-1')
      const range = document.createRange()
      range.selectNodeContents(byTestId('invoice-number') as HTMLElement)
      window.getSelection()?.addRange(range)

      await act(async () => byTestId('invoice-row')?.click())
      await settle()

      expect(router.state.location.pathname).toBe('/invoices')
    })
  })

  describe('KSeF fetch', () => {
    const fetchButton = () => byTestId('invoice-fetch-button') as HTMLButtonElement

    it('posts once for the current company, shows the result and refreshes the list', async () => {
      await render('/invoices?tenantId=tenant-1')
      const before = vi.mocked(listInvoices).mock.calls.length

      await act(async () => fetchButton().click())
      await settle()

      expect(forceCredentialSync).toHaveBeenCalledTimes(1)
      expect(vi.mocked(forceCredentialSync).mock.calls[0]?.[0]).toBe('tenant-1')
      expect(byTestId('invoice-fetch-note')?.textContent).toBe('Pobrane: 3, nowe: 1.')
      expect(byTestId('invoice-fetch-note')?.getAttribute('role')).toBe('status')
      expect(vi.mocked(listInvoices).mock.calls.length).toBeGreaterThan(before)
      expect(vi.mocked(getDashboardOverview).mock.calls.length).toBe(2)
    })

    it('is disabled and says so while pending', async () => {
      vi.mocked(forceCredentialSync).mockReturnValue(new Promise(() => {}))
      await render('/invoices?tenantId=tenant-1')

      await act(async () => fetchButton().click())
      await settle()

      expect(fetchButton().disabled).toBe(true)
      expect(fetchButton().textContent).toBe('Pobieranie…')
    })

    it('does not carry the result over to another company', async () => {
      const router = await render('/invoices?tenantId=tenant-1')
      await act(async () => fetchButton().click())
      await settle()
      expect(byTestId('invoice-fetch-note')?.textContent).not.toBe('')

      await act(async () => {
        await router.navigate('/invoices?tenantId=tenant-2')
      })
      await settle()

      expect(byTestId('invoice-fetch-note')?.textContent).toBe('')
    })

    const serverText = 'No credential configured for this tenant.'

    it('on a 409 says in Polish that the token is missing and links to it, without the server text', async () => {
      vi.mocked(forceCredentialSync).mockRejectedValue(
        new ApiError(serverText, 409, Response.json({ error: serverText }, { status: 409 })),
      )
      await render('/invoices?tenantId=tenant-1')

      await act(async () => fetchButton().click())
      await settle()

      const note = byTestId('invoice-fetch-note')
      expect(note?.textContent).toBe('Ta firma nie ma tokenu KSeF. Dodaj token KSeF')
      expect(note?.querySelector('a')?.getAttribute('href')).toBe('/credentials')
      expect(container.textContent).not.toContain(serverText)
      expect(toast.error).not.toHaveBeenCalled()
      expect(fetchButton().disabled).toBe(false)
    })

    it('does not carry the missing token message over to another company', async () => {
      vi.mocked(forceCredentialSync).mockRejectedValue(new ApiError(serverText, 409))
      const router = await render('/invoices?tenantId=tenant-1')
      await act(async () => fetchButton().click())
      await settle()
      expect(byTestId('invoice-fetch-note')?.textContent).not.toBe('')

      await act(async () => {
        await router.navigate('/invoices?tenantId=tenant-2')
      })
      await settle()

      expect(byTestId('invoice-fetch-note')?.textContent).toBe('')
    })

    it('on a JSON 502 shows a fixed Polish message, not the raw server text', async () => {
      const raw = 'Response status code does not indicate success: 500'
      vi.mocked(forceCredentialSync).mockRejectedValue(new ApiError(raw, 502, Response.json({ error: raw }, { status: 502 })))
      await render('/invoices?tenantId=tenant-1')

      await act(async () => fetchButton().click())
      await settle()

      expect(toast.error).toHaveBeenCalledTimes(1)
      expect(toast.error).toHaveBeenCalledWith(
        'Tenant A: Pobieranie z KSeF nie powiodło się. Spróbuj ponownie później. Jeśli błąd wraca, sprawdź token KSeF.',
      )
      expect(fetchButton().disabled).toBe(false)
    })

    it.each([
      ['a proxy 504', new ApiError('<html>504</html>', 504, new Response('<html>504</html>', { status: 504, headers: { 'Content-Type': 'text/html' } }))],
      ['a proxy 502', new ApiError('<html>502</html>', 502, new Response('<html>502</html>', { status: 502, headers: { 'Content-Type': 'text/html' } }))],
      ['a lost connection', new ApiError('Service Unavailable', 503, new TypeError('Failed to fetch'))],
    ])('does not claim the fetch failed on %s', async (_, error) => {
      vi.mocked(forceCredentialSync).mockRejectedValue(error)
      await render('/invoices?tenantId=tenant-1')

      await act(async () => fetchButton().click())
      await settle()

      expect(toast.error).toHaveBeenCalledWith(
        'Tenant A: Nie mamy potwierdzenia pobrania z KSeF. Część faktur mogła się pobrać. Sprawdź listę i w razie potrzeby spróbuj ponownie.',
      )
    })
  })

  describe('fetch line', () => {
    const line = () => byTestId('invoice-sync-line')

    it('states the last fetch when it is current', async () => {
      await render('/invoices?tenantId=tenant-1')

      expect(line()?.textContent).toBe('Pobrano z KSeF: 01.11.2026, 06:00')
      expect(line()?.querySelector('a')).toBeNull()
    })

    it.each(['Warning', 'Error'])('warns and links to the token on %s', async (syncStatus) => {
      vi.mocked(getDashboardOverview).mockResolvedValue([summary({ syncStatus })])
      await render('/invoices?tenantId=tenant-1')

      expect(line()?.textContent).toContain('Nie pobieramy faktur od 01.11.2026, 06:00')
      expect(line()?.querySelector('a')?.textContent).toBe('Sprawdź token KSeF')
      expect(line()?.querySelector('a')?.getAttribute('href')).toBe('/credentials')
    })

    it('shows nothing before the first successful fetch', async () => {
      vi.mocked(getDashboardOverview).mockResolvedValue([summary({ lastSuccessfulSync: null, syncStatus: 'Error' })])
      await render('/invoices?tenantId=tenant-1')

      expect(line()).toBeNull()
    })

    it('reads the row of the current company', async () => {
      vi.mocked(getDashboardOverview).mockResolvedValue([
        summary({ tenantId: 'tenant-2', syncStatus: 'Error' }),
        summary({ lastSuccessfulSync: '2026-10-30T08:00:00' }),
      ])
      await render('/invoices?tenantId=tenant-1')

      expect(line()?.textContent).toBe('Pobrano z KSeF: 30.10.2026, 08:00')
    })

    it('a dashboard failure hides the line and leaves the list', async () => {
      vi.mocked(getDashboardOverview).mockRejectedValue(new Error('Dashboard failed'))
      await render('/invoices?tenantId=tenant-1')

      expect(line()).toBeNull()
      expect(container.textContent).not.toContain('Dashboard failed')
      expect(byTestId('invoice-row')).toBeTruthy()
    })
  })

  describe('company states', () => {
    const empty = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 }
    const useEmpty = (row: Partial<TenantDashboardSummaryResponse>, cred: Partial<TenantCredentialStatusResponse> = {}) => {
      vi.mocked(listInvoices).mockResolvedValue(empty)
      vi.mocked(getDashboardOverview).mockResolvedValue([summary({ totalInvoices: 0, ...row })])
      vi.mocked(listCredentials).mockResolvedValue([credential(cred)])
    }

    it('no token: card with the link, fetch disabled, no filters', async () => {
      useEmpty({ lastSuccessfulSync: null }, { hasCredential: false })
      await render('/invoices?tenantId=tenant-1')
      expect(container.textContent).toContain('Ta firma nie ma tokenu KSeF')
      expect(container.querySelector('.ui-empty-state a[href="/credentials"]')?.textContent).toContain('Dodaj token KSeF')
      expect(fetchButtonNode().disabled).toBe(true)
      expect(byTestId('invoice-search')).toBeNull()
    })

    it('first fetch: card, fetch stays enabled', async () => {
      useEmpty({ lastSuccessfulSync: null })
      await render('/invoices?tenantId=tenant-1')
      expect(container.textContent).toContain('Czekamy na pierwsze pobranie z KSeF')
      expect(fetchButtonNode().disabled).toBe(false)
    })

    it('first fetch after 24 h: says when the token was added', async () => {
      useEmpty({ lastSuccessfulSync: null }, { lastUpdatedAt: '2026-10-01T10:00:00Z' })
      await render('/invoices?tenantId=tenant-1')
      expect(container.textContent).toContain('Nie pobraliśmy jeszcze faktur.')
      expect(container.textContent).toContain('Token dodano 01.10.2026')
    })

    it('zero: names the fetch date and NIP, and offers the first invoice', async () => {
      useEmpty({})
      await render('/invoices?tenantId=tenant-1')
      expect(container.textContent).toContain('Brak faktur otrzymanych w KSeF')
      expect(container.textContent).toContain('NIP 1234567890')
      expect(container.textContent).toContain('Wystaw pierwszą fakturę')
    })

    it.each(['q=abc', 'dateFrom=2026-01-01', 'dateTo=2026-01-01', 'page=2'])('with %s keeps the plain empty message and the filters', async (filter) => {
      useEmpty({ lastSuccessfulSync: null }, { hasCredential: false })
      await render(`/invoices?tenantId=tenant-1&${filter}`)
      expect(container.textContent).not.toContain('Bez tokenu')
      expect(container.textContent).toContain('Nie znaleziono faktur')
      expect(byTestId('invoice-search')).not.toBeNull()
    })

    it('rows win over a stale zero on the dashboard', async () => {
      vi.mocked(getDashboardOverview).mockResolvedValue([summary({ totalInvoices: 0 })])
      await render('/invoices?tenantId=tenant-1')
      expect(container.querySelector('[data-testid="invoice-row"]')).not.toBeNull()
      expect(container.textContent).not.toContain('Brak faktur otrzymanych')
    })

    it('polls the list and the dashboard every minute in first fetch only', async () => {
      useEmpty({ lastSuccessfulSync: null })
      vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'setTimeout', 'clearTimeout', 'Date'], now: Date.now() })
      try {
        const router = renderPage('/invoices?tenantId=tenant-1')
        await act(async () => {
          root.render(<RouterProvider router={router} />)
        })
        await act(() => vi.advanceTimersByTimeAsync(1000))
        const lists = vi.mocked(listInvoices).mock.calls.length
        const dashboards = vi.mocked(getDashboardOverview).mock.calls.length
        await act(() => vi.advanceTimersByTimeAsync(60_000))
        expect(vi.mocked(listInvoices).mock.calls.length).toBe(lists + 1)
        expect(vi.mocked(getDashboardOverview).mock.calls.length).toBe(dashboards + 1)

        vi.mocked(getDashboardOverview).mockResolvedValue([summary({ totalInvoices: 3 })])
        await act(() => vi.advanceTimersByTimeAsync(60_000))
        const after = vi.mocked(listInvoices).mock.calls.length
        await act(() => vi.advanceTimersByTimeAsync(180_000))
        expect(vi.mocked(listInvoices).mock.calls.length).toBe(after)
      } finally {
        vi.useRealTimers()
      }
    })

    it('no state claim when empty and the credentials query failed', async () => {
      useEmpty({})
      vi.mocked(listCredentials).mockRejectedValue(new Error('credentials down'))
      await render('/invoices?tenantId=tenant-1')
      expect(container.textContent).toContain('credentials down')
      expect(container.textContent).not.toContain('Brak faktur otrzymanych')
    })
  })

  it('shows an error banner with a Polish retry when the list fails, keeping the search box', async () => {
    vi.mocked(listInvoices).mockRejectedValue(new Error('Network failed'))
    await render('/invoices?tenantId=tenant-1')

    expect(container.textContent).toContain('Network failed')
    expect(container.textContent).toContain('Spróbuj ponownie')
    expect(container.textContent).not.toContain('Retry')
    expect(byTestId('invoice-search')).toBeTruthy()
  })
})
