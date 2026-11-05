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

const NOTE = 'Pobieranie faktur sprzedaży z KSeF jeszcze trwa. Lista może być niepełna.'
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

  it('a filtered empty list with the flag shows the note instead of claiming no matches', async () => {
    await render(feed([], { issuedFetchIncomplete: true }), '/issued?tenantId=a&status=Rejected')

    expect(note()?.textContent).toBe(NOTE)
    expect(container.textContent).not.toContain(NO_MATCHES)
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
})
