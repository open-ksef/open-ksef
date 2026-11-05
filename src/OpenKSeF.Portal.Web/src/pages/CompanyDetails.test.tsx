// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, type ReactElement } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import toast from 'react-hot-toast'
import { RouterProvider, createMemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { listIssuedFeed } from '@/api/endpoints/issuedInvoices'
import { createTenant, deleteTenant, listTenants, updateTenant } from '@/api/endpoints/tenants'
import { normalizeError } from '@/api/errors'
import { CompanyCard } from '@/company/CompanyCard'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { CompanyDetailsPage } from './CompanyDetails'

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn(),
  createTenant: vi.fn(),
  updateTenant: vi.fn(),
  deleteTenant: vi.fn(),
}))
vi.mock('@/api/endpoints/issuedInvoices', () => ({ listIssuedFeed: vi.fn() }))
vi.mock('@/api/endpoints/sellerProfile', () => ({ getSellerProfile: vi.fn().mockResolvedValue({ ksefEnvironment: 'test' }) }))
vi.mock('react-hot-toast', () => ({ default: { error: vi.fn(), success: vi.fn() } }))

const flush = () => new Promise<void>((resolve) => setTimeout(resolve, 0))
const company = (id: string, name: string, nip: string) => ({ id, nip, displayName: name, notificationEmail: 'a@b.pl', createdAt: '2026-09-14T10:00:00Z' })

function Location(): ReactElement {
  const { pathname, search } = useLocation()
  return <output data-testid="location">{pathname + search}</output>
}

describe('Firma, Dane firmy', () => {
  let root: Root
  let container: HTMLDivElement
  const by = (id: string) => container.querySelector<HTMLElement>(`[data-testid="${id}"]`)

  async function settle(): Promise<void> {
    for (let tick = 0; tick < 8; tick++) await act(flush)
  }

  async function render(path = '/company/details?tenantId=a'): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter(
      [
        {
          path: '*',
          element: (
            <QueryClientProvider client={client}>
              <CompanyProvider>
                <CompanyCard />
                <Location />
                <CompanyDetailsPage />
              </CompanyProvider>
            </QueryClientProvider>
          ),
        },
      ],
      { initialEntries: [path] },
    )
    await act(async () => root.render(<RouterProvider router={router} />))
    await settle()
  }

  async function type(id: string, value: string): Promise<void> {
    const input = by(id) as HTMLInputElement
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(input, value)
      input.dispatchEvent(new Event('input', { bubbles: true }))
    })
  }

  const openDelete = async () => {
    await act(async () => by('tenant-delete-button')?.click())
    await settle()
    return container.querySelector('[role="alertdialog"]')!
  }

  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('localStorage', memoryStorage())
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    vi.mocked(listTenants).mockResolvedValue([company('a', 'Firma A', '1111111111'), company('b', 'Firma B', '2222222222')])
    vi.mocked(listIssuedFeed).mockResolvedValue({ items: [], page: 1, pageSize: 1, totalCount: 7, totalPages: 7, issuedFetchIncomplete: false })
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  it('edits the current company only', async () => {
    vi.mocked(updateTenant).mockResolvedValue(company('b', 'Nowa B', '2222222222'))
    await render('/company/details?tenantId=b')

    expect((container.querySelector('#company-nip') as HTMLInputElement).value).toBe('2222222222')
    await type('company-display-name', 'Nowa B')
    await act(async () => by('company-save')?.click())
    await settle()

    expect(updateTenant).toHaveBeenCalledExactlyOnceWith('b', { displayName: 'Nowa B', notificationEmail: 'a@b.pl' })
  })

  it('names the company, the issued invoice count and the loss in the delete dialog', async () => {
    vi.mocked(deleteTenant).mockResolvedValue(undefined)
    await render()
    const dialog = await openDelete()

    expect(dialog.textContent).toContain('Firma A')
    expect(dialog.textContent).toContain('NIP 1111111111')
    expect(by('tenant-delete-invoices')?.textContent).toBe(
      'Usuniemy też wystawione faktury i szkice tej firmy (liczba: 7). Nie będzie można ich przywrócić. Firmy nie usuniemy, jeśli faktury wystawione w OpenKSeF są w trakcie wysyłki do KSeF lub zostały przyjęte przez produkcyjny KSeF. Faktury spoza OpenKSeF usuniemy tylko z tej aplikacji: w KSeF zostają.',
    )
    expect(dialog.textContent).toContain('numeracja zacznie się od 1')
    expect(deleteTenant).not.toHaveBeenCalled()

    await act(async () => dialog.querySelector<HTMLElement>('.ui-button--danger')?.click())
    await settle()
    expect(deleteTenant).toHaveBeenCalledExactlyOnceWith('a')
  })

  it('counts only the company being deleted', async () => {
    await render('/company/details?tenantId=b')
    await openDelete()
    expect(listIssuedFeed).toHaveBeenCalledExactlyOnceWith('b', { pageSize: 1 })
  })

  it('says there is nothing to lose when the count is zero', async () => {
    vi.mocked(listIssuedFeed).mockResolvedValue({ items: [], page: 1, pageSize: 1, totalCount: 0, totalPages: 0, issuedFetchIncomplete: false })
    await render()
    await openDelete()
    expect(by('tenant-delete-invoices')?.textContent).toBe('Firma nie ma wystawionych faktur ani szkiców.')
  })

  it('states the unknown count and still allows confirm when the count fails', async () => {
    vi.mocked(listIssuedFeed).mockRejectedValue(new Error('down'))
    await render()
    const dialog = await openDelete()
    expect(by('tenant-delete-invoices')?.textContent).toContain('Nie udało się sprawdzić liczby')
    expect(dialog.querySelector<HTMLButtonElement>('.ui-button--danger')?.disabled).toBe(false)
  })

  it('shows the API message as is when delete returns 409', async () => {
    const message = 'Firma ma faktury przyjęte przez produkcyjny KSeF.'
    vi.mocked(deleteTenant).mockRejectedValue(
      await normalizeError(new Response(JSON.stringify({ message }), { status: 409, headers: { 'Content-Type': 'application/json' } })),
    )
    await render()
    const dialog = await openDelete()
    await act(async () => dialog.querySelector<HTMLElement>('.ui-button--danger')?.click())
    await settle()
    expect(toast.error).toHaveBeenCalledWith(message, { duration: 10_000 })
  })

  it('reports a company already gone on 404', async () => {
    vi.mocked(deleteTenant).mockRejectedValue(await normalizeError(new Response('', { status: 404 })))
    await render()
    const dialog = await openDelete()
    await act(async () => dialog.querySelector<HTMLElement>('.ui-button--danger')?.click())
    await settle()
    expect(vi.mocked(toast.error).mock.calls[0][0]).toContain('Firma już nie istnieje.')
  })

  it('adds a company from the company menu and opens its token tab', async () => {
    vi.mocked(createTenant).mockResolvedValue(company('c', 'Firma C', '3333333333'))
    await render()

    await act(async () => by('company-card')?.click())
    await settle()
    await act(async () => by('company-add')?.click())
    await settle()
    expect(container.querySelector('[role="dialog"]')?.textContent).toContain('Dodaj firmę')

    await act(async () => by('tenant-form-submit')?.click())
    expect(container.querySelector('[role="alert"]')?.textContent).toBe('NIP musi zawierać dokładnie 10 cyfr')
    expect(createTenant).not.toHaveBeenCalled()

    await type('tenant-form-nip', '3333333333')
    await act(async () => by('tenant-form-submit')?.click())
    await settle()

    expect(vi.mocked(createTenant).mock.calls[0][0]).toEqual({ nip: '3333333333', displayName: null, notificationEmail: null })
    expect(by('location')?.textContent).toBe('/company/token?tenantId=c')
  })
})
