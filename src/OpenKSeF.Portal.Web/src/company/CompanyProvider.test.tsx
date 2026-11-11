// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, type ReactNode } from 'react'
import toast from 'react-hot-toast'
import { createRoot, type Root } from 'react-dom/client'
import { Route, RouterProvider, Routes, createMemoryRouter, useLocation, type InitialEntry } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { getDashboardOverview } from '@/api/endpoints/dashboard'
import { getInvoiceByKSeFNumber, listInvoices } from '@/api/endpoints/invoices'
import { getIssuedInvoice, listIssuedFeed, listIssuedInvoices } from '@/api/endpoints/issuedInvoices'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { listTenants } from '@/api/endpoints/tenants'
import type { SellerProfileResponse } from '@/api/types'
import { InvoiceDetailsPage } from '@/pages/InvoiceDetails'
import { InvoiceListPage } from '@/pages/InvoiceList'
import { IssuedInvoiceDetailsPage } from '@/pages/IssuedInvoiceDetails'
import { IssuedInvoiceListPage } from '@/pages/IssuedInvoiceList'
import { CompanyCard } from './CompanyCard'
import { CompanyProvider } from './CompanyProvider'
import { memoryStorage } from './memoryStorage'
import { useCompany } from './useCompany'

vi.mock('react-hot-toast', () => ({ default: Object.assign(vi.fn(), { error: vi.fn(), success: vi.fn() }) }))
vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))
vi.mock('@/api/endpoints/invoices', () => ({ listInvoices: vi.fn(), getInvoiceByKSeFNumber: vi.fn() }))
vi.mock('@/api/endpoints/issuedInvoices', () => ({ listIssuedInvoices: vi.fn(), listIssuedFeed: vi.fn(), getIssuedInvoice: vi.fn() }))
vi.mock('@/api/endpoints/sellerProfile', () => ({ getSellerProfile: vi.fn() }))
vi.mock('@/api/endpoints/dashboard', () => ({ getDashboardOverview: vi.fn() }))
vi.mock('@/api/endpoints/credentials', () => ({ forceCredentialSync: vi.fn() }))

const STORAGE_KEY = 'openksef.company'
const tenant = (id: string, displayName: string | null, nip: string) => ({
  id,
  nip,
  displayName,
  notificationEmail: null,
  createdAt: '2026-01-01T00:00:00Z',
})

function Probe() {
  const { tenantId } = useCompany()
  const location = useLocation()
  return (
    <output data-testid="probe" data-tenant={tenantId}>
      {location.pathname + location.search}
    </output>
  )
}

const settle = () => act(async () => void (await new Promise((resolve) => setTimeout(resolve, 0))))

describe('CompanyProvider', () => {
  let root: Root
  let container: HTMLDivElement

  const probe = () => container.querySelector<HTMLElement>('[data-testid="probe"]')!
  const byTestId = (id: string) => container.querySelector<HTMLElement>(`[data-testid="${id}"]`)!

  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('localStorage', memoryStorage())
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
    vi.mocked(listTenants).mockResolvedValue([
      tenant('a', 'Firma A', '1111111111'),
      tenant('b', 'Firma B', '2222222222'),
      tenant('c', null, '3333333333'),
    ])
    vi.mocked(getSellerProfile).mockResolvedValue({ ksefEnvironment: 'test' } as SellerProfileResponse)
    vi.mocked(getDashboardOverview).mockResolvedValue([])
    vi.mocked(listInvoices).mockResolvedValue({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 })
    vi.mocked(listIssuedInvoices).mockResolvedValue({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 })
    vi.mocked(listIssuedFeed).mockResolvedValue({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0, issuedFetchIncomplete: false })
    vi.mocked(getInvoiceByKSeFNumber).mockReturnValue(new Promise(() => {}))
    vi.mocked(getIssuedInvoice).mockReturnValue(new Promise(() => {}))
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  async function render(path: InitialEntry, page: ReactNode = null): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter(
      [
        {
          path: '*',
          element: (
            <CompanyProvider>
              <CompanyCard />
              <Probe />
              {page}
            </CompanyProvider>
          ),
        },
      ],
      { initialEntries: [path] },
    )
    await act(async () =>
      root.render(
        <QueryClientProvider client={client}>
          <RouterProvider router={router} />
        </QueryClientProvider>,
      ),
    )
    await settle()
  }

  async function chooseCompany(id: string): Promise<void> {
    await act(async () => byTestId('company-card').click())
    await settle()
    await act(async () => byTestId(`company-option-${id}`).click())
    await settle()
  }

  it('is empty until tenants load, so no query runs with a stale id', async () => {
    localStorage.setItem(STORAGE_KEY, 'b')
    let release: (value: ReturnType<typeof tenant>[]) => void = () => {}
    vi.mocked(listTenants).mockReturnValue(new Promise((resolve) => (release = resolve)))
    await render('/invoices', <InvoiceListPage />)

    expect(probe().dataset.tenant).toBe('')
    expect(listInvoices).not.toHaveBeenCalled()

    await act(async () => release([tenant('b', 'Firma B', '2222222222')]))
    await settle()
    expect(probe().dataset.tenant).toBe('b')
  })

  it('lets a URL id win and persists it', async () => {
    localStorage.setItem(STORAGE_KEY, 'a')
    await render('/invoices?tenantId=b')

    expect(probe().dataset.tenant).toBe('b')
    expect(localStorage.getItem(STORAGE_KEY)).toBe('b')
  })

  it('keeps the stored company across a remount', async () => {
    await render('/invoices')
    await chooseCompany('b')
    act(() => root.unmount())
    root = createRoot(container)

    await render('/issued')
    expect(probe().dataset.tenant).toBe('b')
  })

  it('falls back to the first company when the stored one is gone', async () => {
    localStorage.setItem(STORAGE_KEY, 'deleted')
    await render('/invoices')

    expect(probe().dataset.tenant).toBe('a')
  })

  it('ignores a URL id outside the list and does not store it', async () => {
    localStorage.setItem(STORAGE_KEY, 'b')
    await render('/invoices?tenantId=foreign')

    expect(probe().dataset.tenant).toBe('b')
    expect(localStorage.getItem(STORAGE_KEY)).toBe('b')
  })

  it('tells the user a URL company is unknown and queries only the shown one', async () => {
    localStorage.setItem(STORAGE_KEY, 'b')
    await render('/invoices?tenantId=foreign', <InvoiceListPage />)

    expect(toast).toHaveBeenCalledExactlyOnceWith('Nie znaleziono firmy z linku. Pokazujemy firmę Firma B.')
    expect(listInvoices).toHaveBeenCalledWith('b', expect.anything())
    expect(listInvoices).not.toHaveBeenCalledWith('foreign', expect.anything())
  })

  it('a switch on a row opened from the issued list goes to Wystawione', async () => {
    await render({ pathname: '/invoices/KSEF-1', search: '?tenantId=a', state: { fromIssued: true } })
    await chooseCompany('b')

    expect(probe().textContent).toBe('/issued')
    expect(probe().dataset.tenant).toBe('b')
  })

  it.each([
    ['/invoices?tenantId=a&dateFrom=2026-01-01&page=3&pageSize=25', '/invoices?dateFrom=2026-01-01&pageSize=25'],
    ['/invoices/KSEF-1?tenantId=a', '/invoices'],
    ['/issued/i-1?tenantId=a', '/issued'],
    ['/issued/i-1/edit?tenantId=a', '/issued/new'],
    ['/issued/new?tenantId=a', '/issued/new'],
    ['/issued?tenantId=a&status=Draft&q=abc&page=2', '/issued?status=Draft&q=abc'],
    ['/company/invoice-data?tenantId=a&next=new', '/company/invoice-data?next=new'],
    ['/company/details', '/company/details'],
  ])('switching from %s goes to %s', async (from, to) => {
    await render(from)
    await chooseCompany('b')

    expect(probe().textContent).toBe(to)
    expect(probe().dataset.tenant).toBe('b')
    expect(localStorage.getItem(STORAGE_KEY)).toBe('b')
  })

  it.each([
    ['with the company in the URL', '/invoices?tenantId=a&page=3&pageSize=10'],
    ['without the company in the URL', '/invoices?page=3&pageSize=10'],
  ])('never queries the new company with the old page, %s', async (_, path) => {
    localStorage.setItem(STORAGE_KEY, 'a')
    await render(path, <InvoiceListPage />)
    expect(listInvoices).toHaveBeenCalledWith('a', expect.objectContaining({ page: 3 }))

    await chooseCompany('b')

    expect(listInvoices).toHaveBeenCalledWith('b', expect.objectContaining({ page: 1 }))
    expect(listInvoices).not.toHaveBeenCalledWith('b', expect.objectContaining({ page: 3 }))
    expect(probe().textContent).toBe('/invoices?pageSize=10')
  })

  it('a switch on the received list keeps the search, drops the page and queries the new company', async () => {
    localStorage.setItem(STORAGE_KEY, 'a')
    await render('/invoices?q=abc&page=3', <InvoiceListPage />)

    await chooseCompany('b')

    expect(listInvoices).toHaveBeenLastCalledWith('b', expect.objectContaining({ filterByVendor: 'abc', page: 1 }))
    expect(probe().textContent).toBe('/invoices?q=abc')
    expect(byTestId('invoice-search')).toHaveProperty('value', 'abc')
  })

  it.each([
    ['received', '/invoices', <InvoiceListPage />, listInvoices],
    ['issued', '/issued', <IssuedInvoiceListPage />, listIssuedFeed],
  ])('a switch on the %s list with no page in the URL queries the new company', async (_, path, page, list) => {
    localStorage.setItem(STORAGE_KEY, 'a')
    await render(path, page)
    expect(list).toHaveBeenCalledWith('a', expect.anything())

    await chooseCompany('b')

    expect(list).toHaveBeenCalledWith('b', expect.anything())
  })

  it.each([
    ['/invoices/K-1', getInvoiceByKSeFNumber, 'K-1'],
    ['/issued/i-1', getIssuedInvoice, 'i-1'],
  ])('%s reads the current company, not the first one', async (path, read, id) => {
    localStorage.setItem(STORAGE_KEY, 'b')
    await render(
      path,
      <Routes>
        <Route path="/invoices/:ksefInvoiceNumber" element={<InvoiceDetailsPage />} />
        <Route path="/issued/:id" element={<IssuedInvoiceDetailsPage />} />
      </Routes>,
    )

    expect(read).toHaveBeenCalledWith('b', id)
  })

  it('shows name and NIP, falls back to NIP for an unnamed company', async () => {
    await render('/invoices?tenantId=c')

    expect(byTestId('company-card').textContent).toContain('3333333333')
    await chooseCompany('a')
    expect(byTestId('company-card').textContent).toContain('Firma A')
    expect(byTestId('company-card').textContent).toContain('NIP 1111111111')
  })

  it('filters the menu by name or NIP and marks the current company', async () => {
    await render('/invoices')
    await act(async () => byTestId('company-card').click())
    await settle()

    expect(byTestId('company-option-a').getAttribute('aria-pressed')).toBe('true')
    expect(byTestId('company-option-b').getAttribute('aria-pressed')).toBe('false')
    expect(byTestId('company-option-a').textContent).toContain('TEST')

    const search = byTestId('company-search') as HTMLInputElement
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(search, '2222')
      search.dispatchEvent(new Event('input', { bubbles: true }))
    })

    expect(byTestId('company-option-a')).toBeNull()
    expect(byTestId('company-option-b')).not.toBeNull()
  })

  it('reads the environment only while the menu is open', async () => {
    await render('/invoices')
    expect(getSellerProfile).not.toHaveBeenCalled()

    await act(async () => byTestId('company-card').click())
    await settle()
    expect(getSellerProfile).toHaveBeenCalledWith('a')
  })

  it('closes the menu on Escape and on an outside click', async () => {
    await render('/invoices')
    const details = container.querySelector('details')!

    await act(async () => byTestId('company-card').click())
    expect(details.open).toBe(true)
    await act(async () => void document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })))
    expect(details.open).toBe(false)

    await act(async () => byTestId('company-card').click())
    expect(details.open).toBe(true)
    await act(async () => void document.body.dispatchEvent(new Event('pointerdown', { bubbles: true })))
    expect(details.open).toBe(false)
  })
})
