// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import axe from 'axe-core'
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
import type {
  InvoiceResponse,
  IssuedInvoiceListItemResponse,
  PagedResult,
  TenantCredentialStatusResponse,
  TenantDashboardSummaryResponse,
} from '@/api/types'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { useCompany } from '@/company/useCompany'
import { StartPage } from './Start'

vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))
vi.mock('@/api/endpoints/invoices', () => ({ listInvoices: vi.fn() }))
vi.mock('@/api/endpoints/issuedInvoices', () => ({ listIssuedInvoices: vi.fn() }))
vi.mock('@/api/endpoints/dashboard', () => ({ getDashboardOverview: vi.fn() }))
vi.mock('@/api/endpoints/credentials', () => ({ forceCredentialSync: vi.fn(), listCredentials: vi.fn() }))
vi.mock('react-hot-toast', () => ({ default: { error: vi.fn(), success: vi.fn() } }))

const flush = () => new Promise<void>((resolve) => setTimeout(resolve, 0))
const settle = async () => {
  for (let tick = 0; tick < 6; tick++) await act(flush)
}
const paged = <T,>(items: T[], totalCount = items.length): PagedResult<T> => ({ items, page: 1, pageSize: 25, totalCount, totalPages: 1 })

const summary = (overrides: Partial<TenantDashboardSummaryResponse> = {}): TenantDashboardSummaryResponse => ({
  tenantId: 'a',
  nip: '1111111111',
  displayName: 'Firma A',
  lastSyncedAt: '2026-11-03T06:00:00Z',
  lastSuccessfulSync: '2026-11-03T06:00:00Z',
  totalInvoices: 12,
  invoicesLast7Days: 2,
  invoicesLast30Days: 5,
  syncStatus: 'Success',
  ...overrides,
})

const credential = (overrides: Partial<TenantCredentialStatusResponse> = {}): TenantCredentialStatusResponse => ({
  tenantId: 'a',
  tenantDisplayName: 'Firma A',
  hasCredential: true,
  credentialType: 'Token',
  lastUpdatedAt: new Date().toISOString(),
  ...overrides,
})

const received = (overrides: Partial<InvoiceResponse> = {}): InvoiceResponse => ({
  id: 'r-1',
  ksefInvoiceNumber: 'KSEF-1',
  ksefReferenceNumber: 'REF-1',
  invoiceNumber: 'FZ/1',
  vendorName: 'Dostawca Testowy',
  vendorNip: '2222222222',
  buyerName: null,
  buyerNip: null,
  amountNet: 100,
  amountVat: 23,
  amountGross: 123,
  currency: 'PLN',
  issueDate: '2026-11-01T00:00:00Z',
  acquisitionDate: null,
  invoiceType: null,
  firstSeenAt: '2026-11-01T12:00:00Z',
  isPaid: false,
  paidAt: null,
  ...overrides,
})

const issued = (overrides: Partial<IssuedInvoiceListItemResponse> = {}): IssuedInvoiceListItemResponse => ({
  id: 'i-1',
  status: 'Accepted',
  number: 'FV/1',
  buyerName: 'Nabywca Testowy',
  buyerNip: '3333333333',
  issueDate: '2026-11-01',
  dueDate: '2026-11-15',
  totalNet: 100,
  totalGross: 123,
  currency: 'PLN',
  ksefNumber: 'K-1',
  needsAttention: false,
  ...overrides,
})

function CurrentCompany() {
  return <p data-testid="current-company">{useCompany().tenantId}</p>
}

describe('StartPage', () => {
  let root: Root
  let container: HTMLDivElement

  const setViewport = (width: number, height: number) => {
    vi.stubGlobal('innerWidth', width)
    vi.stubGlobal('innerHeight', height)
  }

  const useData = ({
    summaries = [summary()],
    credentials = [credential()],
    rejected = [],
    rejectedTotal = rejected.length,
    recent = [],
    recentTotal = recent.length,
    invoices = [received()],
  }: {
    summaries?: TenantDashboardSummaryResponse[]
    credentials?: TenantCredentialStatusResponse[]
    rejected?: IssuedInvoiceListItemResponse[]
    rejectedTotal?: number
    recent?: IssuedInvoiceListItemResponse[]
    recentTotal?: number
    invoices?: InvoiceResponse[]
  } = {}) => {
    vi.mocked(getDashboardOverview).mockResolvedValue(summaries)
    vi.mocked(listCredentials).mockResolvedValue(credentials)
    vi.mocked(listInvoices).mockResolvedValue(paged(invoices))
    vi.mocked(listIssuedInvoices).mockImplementation(async (_, params) =>
      params?.status === 'Rejected' ? paged(rejected, rejectedTotal) : paged(recent, recentTotal),
    )
  }

  async function render(path = '/', seed?: (client: QueryClient) => void) {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    seed?.(client)
    const page = (
      <QueryClientProvider client={client}>
        <CompanyProvider>
          <StartPage />
          <CurrentCompany />
        </CompanyProvider>
      </QueryClientProvider>
    )
    const router = createMemoryRouter(
      [
        { path: '/', element: page },
        { path: '/invoices/:ksefInvoiceNumber', element: <p data-testid="details-page" /> },
      ],
      { initialEntries: [path] },
    )
    await act(async () => {
      root.render(<RouterProvider router={router} />)
    })
    await settle()
    return router
  }

  const text = () => container.textContent ?? ''
  const attention = () => container.querySelector('.start-card--attention')
  const linkTo = (href: string) => container.querySelector<HTMLAnchorElement>(`a[href="${href}"]`)
  const fetchButton = () => container.querySelector<HTMLButtonElement>('[data-testid="invoice-fetch-button"]')!

  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('localStorage', memoryStorage())
    setViewport(1280, 720)
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
    vi.mocked(listTenants).mockResolvedValue([
      { id: 'a', nip: '1111111111', displayName: 'Firma A', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' },
      { id: 'b', nip: '2222222222', displayName: 'Firma B', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' },
    ])
    useData()
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
    vi.unstubAllGlobals()
  })

  describe('Wymaga uwagi', () => {
    it('says nothing needs attention when all queries are clean', async () => {
      await render()
      expect(attention()?.textContent).toContain('Nic nie wymaga uwagi.')
      expect(attention()?.querySelector('li')).toBeNull()
    })

    it('lists a rejected invoice with its fix link and the total of rejected', async () => {
      useData({ rejected: [issued({ id: 'rej', number: 'FV/9', status: 'Rejected' })], rejectedTotal: 7 })
      await render()
      expect(attention()?.textContent).toContain('FV/9: KSeF nie przyjął faktury')
      expect(attention()?.textContent).not.toContain('Nic nie wymaga uwagi.')
      expect(linkTo('/issued/rej?tenantId=a')?.textContent).toBe('FV/9: KSeF nie przyjął faktury')
      expect(linkTo('/issued?status=Rejected')?.textContent).toBe('Wszystkie odrzucone (7)')
      expect(vi.mocked(listIssuedInvoices)).toHaveBeenCalledWith('a', expect.objectContaining({ status: 'Rejected', pageSize: 5 }))
    })

    it('warns on a Sending invoice that needs attention, and tells not to send it again', async () => {
      useData({ recent: [issued({ id: 's', number: 'FV/2', status: 'Sending', needsAttention: true })] })
      await render()
      expect(attention()?.textContent).toContain('FV/2: wysyłanie trwa dłużej')
      expect(attention()?.textContent).toContain('Nie wysyłaj faktury drugi raz.')
    })

    it('asks to check an Accepted invoice that needs attention', async () => {
      useData({ recent: [issued({ number: 'FV/3', needsAttention: true })] })
      await render()
      expect(attention()?.textContent).toContain('FV/3: sprawdź status w KSeF')
    })

    it('ignores issued invoices without needsAttention', async () => {
      useData({ recent: [issued({ number: 'FV/4', status: 'Sending' }), issued({ id: 'i-2', number: 'FV/5' })] })
      await render()
      expect(attention()?.textContent).toContain('Nic nie wymaga uwagi.')
    })

    it('scans the newest 100 issued invoices', async () => {
      await render()
      expect(vi.mocked(listIssuedInvoices)).toHaveBeenCalledWith('a', expect.objectContaining({ pageSize: 100 }))
    })

    it('states a stale fetch without any reason, with a link to the token', async () => {
      useData({ summaries: [summary({ syncStatus: 'Warning', lastSuccessfulSync: '2026-10-31T10:00:00Z' })] })
      await render()
      expect(attention()?.textContent).toContain('Nie pobieramy faktur od 31.10.2026')
      expect(attention()?.textContent).toContain('Sprawdź token KSeF')
      expect(attention()?.querySelector('a[href="/credentials"]')).not.toBeNull()
      expect(text()).not.toContain('Powód')
    })

    it('with invoices and no token shows the normal page and the token item', async () => {
      useData({ credentials: [credential({ hasCredential: false, lastUpdatedAt: null })] })
      await render()
      expect(text()).toContain('Dostawca Testowy')
      expect(attention()?.textContent).toContain('Ta firma nie ma tokenu KSeF')
      expect(attention()?.textContent).toContain('Dodaj token KSeF')
      expect(text()).not.toContain('Brakuje tokenu KSeF')
    })

    it('does not claim all clear when more issued invoices exist than were scanned', async () => {
      useData({ recent: [issued()], recentTotal: 101 })
      await render()
      expect(attention()?.textContent).not.toContain('Nic nie wymaga uwagi.')
      expect(attention()?.textContent).toContain('W 100 najnowszych fakturach wystawionych nic nie wymaga uwagi.')
    })

    it('does not claim all is clear when an issued query failed', async () => {
      vi.mocked(listIssuedInvoices).mockRejectedValue(new Error('issued down'))
      await render()
      expect(attention()?.textContent).not.toContain('Nic nie wymaga uwagi.')
      expect(attention()?.querySelector('[role="alert"]')).not.toBeNull()
    })
  })

  describe('states', () => {
    it('reads the token of the current company, not of another one', async () => {
      useData({
        summaries: [summary({ totalInvoices: 0, lastSuccessfulSync: null }), summary({ tenantId: 'b', totalInvoices: 0, lastSuccessfulSync: null })],
        credentials: [credential({ hasCredential: false }), credential({ tenantId: 'b' })],
      })
      await render('/?tenantId=b')
      expect(text()).toContain('Czekamy na pierwsze pobranie z KSeF')
      expect(text()).not.toContain('Brakuje tokenu KSeF')
    })

    it('no token: links to adding it and disables the fetch', async () => {
      useData({ summaries: [summary({ totalInvoices: 0, lastSuccessfulSync: null })], credentials: [credential({ hasCredential: false })] })
      await render()
      expect(text()).toContain('Firma dodana. Brakuje tokenu KSeF')
      expect(text()).not.toContain('Firma Firma')
      expect(linkTo('/credentials')?.textContent).toContain('Dodaj token KSeF')
      expect(fetchButton().disabled).toBe(true)
      expect(container.querySelector('.start-grid')).toBeNull()
    })

    it('no token after an earlier sync: no sync line, the card says it all', async () => {
      useData({ summaries: [summary({ totalInvoices: 0, syncStatus: 'Warning', lastSuccessfulSync: '2026-10-01T10:00:00Z' })], credentials: [credential({ hasCredential: false })] })
      await render()
      expect(text()).toContain('Brakuje tokenu KSeF')
      expect(text()).not.toContain('Nie pobieramy faktur')
      expect(attention()).toBeNull()
    })

    it('first fetch: says it runs and keeps the fetch enabled', async () => {
      useData({ summaries: [summary({ totalInvoices: 0, lastSuccessfulSync: null })] })
      await render()
      expect(text()).toContain('Czekamy na pierwsze pobranie z KSeF')
      expect(text()).toContain('Także te już zapłacone.')
      expect(fetchButton().disabled).toBe(false)
      expect(vi.mocked(listInvoices)).not.toHaveBeenCalled()
    })

    it('first fetch after 24 h: says none came, when the token was added, and links to it', async () => {
      useData({
        summaries: [summary({ totalInvoices: 0, lastSuccessfulSync: null })],
        credentials: [credential({ lastUpdatedAt: '2026-10-01T10:00:00Z' })],
      })
      await render()
      expect(text()).toContain('Nie pobraliśmy jeszcze faktur.')
      expect(text()).toContain('Token dodano 01.10.2026')
      expect(text()).not.toContain('Czekamy na pierwsze pobranie z KSeF')
      expect(linkTo('/credentials')?.textContent).toContain('Sprawdź token KSeF')
      expect(fetchButton().disabled).toBe(false)
    })

    it('zero: says when it fetched and offers the first invoice', async () => {
      useData({ summaries: [summary({ totalInvoices: 0 })] })
      await render()
      expect(text()).toContain('Brak faktur do pokazania')
      expect(text()).toContain('Pobrano 03.11.2026')
      expect(text()).toContain('NIP 1111111111')
      expect(text()).toContain('Nic nie wymaga uwagi.')
      expect(text()).toContain('Wystaw pierwszą fakturę')
    })

    it('zero with a rejected invoice: attention above, no clear claim, no first invoice', async () => {
      const rejected = [issued({ id: 'rej', number: 'FV/9', status: 'Rejected' })]
      useData({ summaries: [summary({ totalInvoices: 0 })], rejected, recent: rejected })
      await render()
      const card = container.querySelector('.ui-empty-state')!
      expect(attention()?.textContent).toContain('FV/9: KSeF nie przyjął faktury')
      expect(attention()!.compareDocumentPosition(card) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
      expect(text()).not.toContain('Nic nie wymaga uwagi.')
      expect(text()).not.toContain('Wystaw pierwszą fakturę')
      expect(card.textContent).toContain('Nowa faktura')
    })

    it('makes no state claim while the current company is missing from the cached rows', async () => {
      vi.mocked(getDashboardOverview).mockReturnValue(new Promise(() => {}))
      await render('/', (client) => client.setQueryData(['dashboard'], [summary({ tenantId: 'other' })], { updatedAt: 1 }))
      expect(container.querySelector('.ui-skeleton')).not.toBeNull()
      expect(attention()).toBeNull()
    })

    it('makes no state claim while loading', async () => {
      vi.mocked(getDashboardOverview).mockReturnValue(new Promise(() => {}))
      await render()
      expect(container.querySelector('.ui-skeleton')).not.toBeNull()
      expect(container.querySelector('.ui-empty-state')).toBeNull()
      expect(text()).not.toContain('Nic nie wymaga uwagi.')
    })

    it.each([
      ['dashboard', () => vi.mocked(getDashboardOverview).mockRejectedValue(new Error('dashboard down'))],
      ['credentials', () => vi.mocked(listCredentials).mockRejectedValue(new Error('credentials down'))],
    ])('makes no state claim when %s fails, and offers a retry', async (name, fail) => {
      fail()
      await render()
      expect(text()).toContain(`${name} down`)
      expect(text()).toContain('Spróbuj ponownie')
      expect(container.querySelector('.ui-empty-state')).toBeNull()
      expect(container.querySelector('.start-grid')).toBeNull()
    })

    it('has one h1 in a state card', async () => {
      useData({ summaries: [summary({ totalInvoices: 0 })] })
      await render()
      expect(container.querySelectorAll('h1')).toHaveLength(1)
    })
  })

  describe('first fetch polling', () => {
    const renderWithFakeTimers = () =>
      act(async () => {
        const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
        const page = (
          <QueryClientProvider client={client}>
            <CompanyProvider>
              <StartPage />
            </CompanyProvider>
          </QueryClientProvider>
        )
        root.render(<RouterProvider router={createMemoryRouter([{ path: '/', element: page }], { initialEntries: ['/'] })} />)
      }).then(() => act(() => vi.advanceTimersByTimeAsync(1000)))

    it('polls the dashboard only in first fetch, and stops when invoices arrive', async () => {
      useData({ summaries: [summary({ totalInvoices: 0, lastSuccessfulSync: null })] })
      vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'setTimeout', 'clearTimeout', 'Date'], now: Date.now() })
      try {
        await renderWithFakeTimers()
        const initial = vi.mocked(getDashboardOverview).mock.calls.length
        vi.mocked(getDashboardOverview).mockResolvedValue([summary()])
        await act(() => vi.advanceTimersByTimeAsync(60_000))
        expect(vi.mocked(getDashboardOverview).mock.calls.length).toBe(initial + 1)
        await act(() => vi.advanceTimersByTimeAsync(180_000))
        expect(vi.mocked(getDashboardOverview).mock.calls.length).toBe(initial + 1)
        expect(container.textContent).toContain('Dostawca Testowy')
      } finally {
        vi.useRealTimers()
      }
    })

    it('does not poll in the full state', async () => {
      vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'setTimeout', 'clearTimeout', 'Date'], now: Date.now() })
      try {
        await renderWithFakeTimers()
        const initial = vi.mocked(getDashboardOverview).mock.calls.length
        await act(() => vi.advanceTimersByTimeAsync(180_000))
        expect(vi.mocked(getDashboardOverview).mock.calls.length).toBe(initial)
      } finally {
        vi.useRealTimers()
      }
    })
  })

  describe('full page', () => {
    it('shows recent received and issued, without drafts, and no total anywhere', async () => {
      useData({
        invoices: [received(), received({ id: 'r-2', ksefInvoiceNumber: 'KSEF-2', vendorName: 'Inny Dostawca' })],
        recent: [issued({ number: 'FV/1' }), issued({ id: 'd', number: null, status: 'Draft', buyerName: 'Szkicowy Nabywca' })],
      })
      await render()
      expect(text()).toContain('Co dziś do zrobienia')
      expect(text()).toContain('Dostawca Testowy')
      expect(text()).toContain('FV/1')
      expect(text()).not.toContain('Szkicowy Nabywca')
      expect(text()).not.toContain('Pulpit')
      expect(text().match(/PLN/g)).toHaveLength(3)
      expect(container.querySelectorAll('.start-row__end b')).toHaveLength(3)
      expect(text()).not.toContain('Nieopłacone')
      expect(text()).not.toContain('Termin')
    })

    it('opens a received row with the list search and the siblings', async () => {
      useData({ invoices: [received(), received({ id: 'r-2', ksefInvoiceNumber: 'KSEF-2' })] })
      const router = await render()
      await act(async () => linkTo('/invoices/KSEF-1?tenantId=a')!.click())
      await settle()
      expect(router.state.location.pathname).toBe('/invoices/KSEF-1')
      expect(router.state.location.state).toEqual({ listSearch: '', siblings: ['KSEF-1', 'KSEF-2'] })
    })

    it('uses a received query key under invoices and company, so undo and fetch refresh it', async () => {
      await render()
      await act(async () => fetchButton().click())
      await settle()
      expect(vi.mocked(listInvoices).mock.calls.length).toBeGreaterThan(1)
    })

    it.each([
      [1280, 720, 6],
      [1799, 1440, 6],
      [2560, 1440, 18],
      [1800, 800, 6],
      [1800, 600, 5],
    ])('at %ix%i asks for %i rows', async (width, height, rows) => {
      setViewport(width, height)
      await render()
      expect(vi.mocked(listInvoices)).toHaveBeenCalledWith('a', { page: 1, pageSize: rows })
    })

    it('limits recent issued to the row count', async () => {
      useData({ recent: Array.from({ length: 10 }, (_, index) => issued({ id: `i-${index}`, number: `FV/${index}` })) })
      await render()
      expect(container.querySelectorAll('.start-card--issued li')).toHaveLength(6)
    })

    it('hides Twoje firmy for one company', async () => {
      await render()
      expect(text()).not.toContain('Twoje firmy')
      expect(container.querySelector('.start-grid--solo')).not.toBeNull()
    })

    it('lists 5 companies at most, with plural text, and switches on Wybierz', async () => {
      const summaries = [
        summary({ totalInvoices: 1, invoicesLast7Days: 1 }),
        summary({ tenantId: 'b', displayName: 'Firma B', totalInvoices: 2, invoicesLast7Days: 0 }),
        ...[12, 22, 5, 7].map((total, index) => summary({ tenantId: `c${index}`, displayName: `Firma C${index}`, totalInvoices: total })),
      ]
      useData({ summaries, credentials: [credential()] })
      await render()
      const card = container.querySelector('.start-card--companies')!
      expect(card.querySelectorAll('li')).toHaveLength(5)
      expect(card.textContent).toContain('1 faktura, 1 z ostatnich 7 dni')
      expect(card.textContent).toContain('2 faktury, 0 z ostatnich 7 dni')
      expect(card.textContent).toContain('12 faktur,')
      expect(card.textContent).toContain('22 faktury,')
      expect(card.textContent).toContain('5 faktur,')
      expect(card.textContent).toContain('Pozostałe: 1.')
      expect(card.textContent).toContain('Wybrana')
      expect(card.textContent).not.toContain('nowych')

      const pick = card.querySelector<HTMLButtonElement>('[aria-label="Wybierz firmę: Firma B"]')!
      await act(async () => pick.click())
      await settle()
      expect(container.querySelector('[data-testid="current-company"]')?.textContent).toBe('b')
    })

    it('has no axe violations in the full and state views', async () => {
      useData({ summaries: [summary(), summary({ tenantId: 'b', displayName: 'Firma B' })], rejected: [issued({ id: 'rej', status: 'Rejected' })] })
      await render()
      expect((await axe.run(container, { rules: { 'color-contrast': { enabled: false }, region: { enabled: false } } })).violations).toEqual([])
    })
  })

  describe('KSeF fetch after a company switch', () => {
    const toA = '/?tenantId=a'

    it('leaves the button on B enabled while A is pending', async () => {
      vi.mocked(forceCredentialSync).mockReturnValue(new Promise(() => {}))
      useData({ summaries: [summary(), summary({ tenantId: 'b' })], credentials: [credential(), credential({ tenantId: 'b' })] })
      const router = await render(toA)
      await act(async () => fetchButton().click())
      await settle()
      expect(fetchButton().textContent).toBe('Pobieranie…')

      await act(() => router.navigate('/?tenantId=b'))
      await settle()
      expect(fetchButton().disabled).toBe(false)
      expect(fetchButton().textContent).toBe('Pobierz z KSeF')

      await act(() => router.navigate(toA))
      await settle()
      expect(fetchButton().disabled).toBe(true)
    })

    it('names company A when its fetch fails after a switch to B', async () => {
      let fail: (error: Error) => void = () => {}
      vi.mocked(forceCredentialSync).mockReturnValue(new Promise((_, reject) => (fail = reject)))
      useData({ summaries: [summary(), summary({ tenantId: 'b' })], credentials: [credential(), credential({ tenantId: 'b' })] })
      const router = await render(toA)
      await act(async () => fetchButton().click())
      await settle()
      await act(() => router.navigate('/?tenantId=b'))
      await settle()

      await act(async () => fail(new ApiError('boom', 503, new TypeError('Failed to fetch'))))
      await settle()

      expect(vi.mocked(toast.error).mock.calls[0]?.[0]).toMatch(/^Firma A: /)
    })

    it.each([401, 403, 404, 429])('on %i says the fetch did not start, without a hint that invoices came', async (status) => {
      vi.mocked(forceCredentialSync).mockRejectedValue(new ApiError('raw', status))
      await render()
      await act(async () => fetchButton().click())
      await settle()
      expect(toast.error).toHaveBeenCalledWith('Firma A: Nie uruchomiono pobierania z KSeF.')
    })
  })
})
