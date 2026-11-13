// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter, Route, Routes, type InitialEntry } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { getInvoiceByKSeFNumber, getTransferDetails } from '@/api/endpoints/invoices'
import { listTenants } from '@/api/endpoints/tenants'
import { ApiError } from '@/api/errors'
import type { InvoiceResponse } from '@/api/types'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { InvoiceDetailsPage } from '@/pages/InvoiceDetails'
import { Layout } from './Layout'

vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))
vi.mock('@/api/endpoints/invoices', () => ({ getInvoiceByKSeFNumber: vi.fn(), getTransferDetails: vi.fn(), setInvoicePaid: vi.fn() }))

vi.mock('@/auth/useAuth', () => ({
  useAuth: vi.fn().mockReturnValue({
    user: { profile: { name: 'Portal User' } },
    isLoading: false,
    isAuthenticated: true,
    login: vi.fn(),
    sessionEnded: false,
    logout: vi.fn(),
    getAccessToken: vi.fn(),
  }),
}))

const row = (direction: InvoiceResponse['direction']): InvoiceResponse => ({
  id: 'inv-1',
  ksefInvoiceNumber: 'KSEF-1',
  ksefReferenceNumber: 'REF-1',
  invoiceNumber: 'FV/1',
  vendorName: 'Vendor',
  vendorNip: '9876543210',
  buyerName: null,
  buyerNip: null,
  amountNet: 100,
  amountVat: 23,
  amountGross: 123,
  currency: 'PLN',
  issueDate: '2026-02-01T00:00:00Z',
  acquisitionDate: null,
  invoiceType: 'Vat',
  firstSeenAt: '2026-02-01T12:00:00Z',
  isPaid: false,
  paidAt: null,
  direction,
})

describe('Layout', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.stubGlobal('localStorage', memoryStorage())
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
    vi.mocked(listTenants).mockResolvedValue([
      { id: 't-1', nip: '1111111111', displayName: 'Firma Testowa', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' },
    ])
    vi.mocked(getInvoiceByKSeFNumber).mockReset()
    vi.mocked(getInvoiceByKSeFNumber).mockRejectedValue(new ApiError('Not found', 404))
    vi.mocked(getTransferDetails).mockReturnValue(new Promise(() => {}))
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  async function render(path: InitialEntry): Promise<void> {
    await act(async () => {
      root.render(
        <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retryDelay: 0 } } })}>
          <MemoryRouter initialEntries={[path]}>
            <CompanyProvider>
              <Routes>
                <Route element={<Layout />}>
                  <Route path="/invoices/:ksefInvoiceNumber" element={<InvoiceDetailsPage />} />
                  <Route path="*" element={null} />
                </Route>
              </Routes>
            </CompanyProvider>
          </MemoryRouter>
        </QueryClientProvider>,
      )
    })
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 10))
    })
  }

  const settled = async (text: string) => {
    for (let i = 0; i < 100 && !container.textContent?.includes(text); i++) {
      await act(async () => {
        await new Promise((resolve) => setTimeout(resolve, 5))
      })
    }
    expect(container.textContent).toContain(text)
  }

  const currentItems = () =>
    [...container.querySelectorAll('[aria-current="page"]')].map((item) => item.getAttribute('aria-label') ?? item.textContent)

  it('has at most 6 top-level menu items', async () => {
    await render('/')

    const items = [...container.querySelectorAll('[data-testid="mobile-nav"] a')].map((link) => link.textContent)
    expect(items).toEqual(['Start', 'Otrzymane', 'Wystawione', 'Firma', 'Telefon'])
    expect(items.length + container.querySelectorAll('[data-testid="nav-settings"]').length).toBeLessThanOrEqual(6)
  })

  it.each([
    ['/', 'Start'],
    ['/invoices/KSEF-1', 'Otrzymane'],
    ['/issued/new', 'Wystawione'],
    ['/company', 'Firma'],
    ['/company/invoice-data', 'Firma'],
    ['/company/token', 'Firma'],
    ['/company/details', 'Firma'],
    ['/mobile-setup', 'Telefon'],
    ['/settings', 'Ustawienia'],
  ])('marks exactly one item on %s', async (path, item) => {
    await render(path)

    expect(currentItems()).toEqual([item])
  })

  describe('invoice details', () => {
    it('marks Wystawione on a deep link to an issued row', async () => {
      vi.mocked(getInvoiceByKSeFNumber).mockResolvedValue(row('Issued'))
      await render('/invoices/KSEF-1')
      await settled('FV/1')

      expect(currentItems()).toEqual(['Wystawione'])
    })

    it('marks Wystawione on an encoded number', async () => {
      vi.mocked(getInvoiceByKSeFNumber).mockResolvedValue(row('Issued'))
      await render('/invoices/KSEF%2F1')
      await settled('FV/1')

      expect(getInvoiceByKSeFNumber).toHaveBeenCalledWith('t-1', 'KSEF/1')
      expect(currentItems()).toEqual(['Wystawione'])
    })

    it('marks Otrzymane on a received invoice', async () => {
      vi.mocked(getInvoiceByKSeFNumber).mockResolvedValue(row('Received'))
      await render('/invoices/KSEF-1')
      await settled('FV/1')

      expect(currentItems()).toEqual(['Otrzymane'])
    })

    it('marks Wystawione from the list before the row loads', async () => {
      vi.mocked(getInvoiceByKSeFNumber).mockReturnValue(new Promise(() => {}))
      await render({ pathname: '/invoices/KSEF-1', state: { fromIssued: true } })

      expect(currentItems()).toEqual(['Wystawione'])
    })

    it('does not fetch or retry on its own: the page keeps retry off', async () => {
      await render({ pathname: '/invoices/KSEF-404', state: { fromIssued: true } })
      await settled('Nie znaleziono')

      expect(getInvoiceByKSeFNumber).toHaveBeenCalledTimes(1)
      expect(currentItems()).toEqual(['Wystawione'])
    })
  })

  it('renders every icon from the sprite, with no glyphs', async () => {
    await render('/')

    const hrefs = [...container.querySelectorAll('use')].map((use) => use.getAttribute('href'))
    expect(hrefs).toContain('/icons.svg#i-house')
    expect(hrefs).toContain('/icons.svg#i-log-out')
    expect(container.textContent).not.toMatch(/[⚡⊞≡✎▤◉⚿◈⬡⚙↪☰]/u)
  })

  it('shows the current company and a sign-out control', async () => {
    await render('/')

    expect(container.querySelector('[data-testid="company-card"]')?.textContent).toContain('Firma Testowa')
    expect(container.querySelector('[data-testid="company-card"]')?.textContent).toContain('NIP 1111111111')
    expect(container.querySelector('[data-testid="nav-logout"]')?.getAttribute('aria-label')).toBe('Wyloguj')
  })
})
