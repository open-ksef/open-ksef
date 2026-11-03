// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, type ReactElement } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  addOrUpdateCredential,
  deleteCredential,
  forceCredentialSync,
  forceFullResync,
  listCredentials,
} from '@/api/endpoints/credentials'
import { getDashboardOverview } from '@/api/endpoints/dashboard'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { listTenants } from '@/api/endpoints/tenants'
import type { TenantCredentialStatusResponse } from '@/api/types'
import { CompanyCard } from '@/company/CompanyCard'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { CompanyLayoutPage } from './CompanyLayout'
import { CompanyTokenPage } from './CompanyToken'

vi.mock('@/api/endpoints/credentials', () => ({
  listCredentials: vi.fn(),
  addOrUpdateCredential: vi.fn(),
  deleteCredential: vi.fn(),
  forceCredentialSync: vi.fn(),
  forceFullResync: vi.fn(),
}))
vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))
vi.mock('@/api/endpoints/dashboard', () => ({ getDashboardOverview: vi.fn() }))
vi.mock('@/api/endpoints/sellerProfile', () => ({ getSellerProfile: vi.fn() }))
vi.mock('react-hot-toast', () => ({ default: { error: vi.fn(), success: vi.fn() } }))

const flush = () => new Promise<void>((resolve) => setTimeout(resolve, 0))
const company = (id: string, name: string, nip: string) => ({ id, nip, displayName: name, notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' })
const row = (tenantId: string, overrides: Partial<TenantCredentialStatusResponse> = {}): TenantCredentialStatusResponse => ({
  tenantId,
  tenantDisplayName: tenantId,
  hasCredential: true,
  credentialType: 'Token',
  lastUpdatedAt: '2026-09-14T10:00:00Z',
  ...overrides,
})

function Location(): ReactElement {
  const { pathname, search } = useLocation()
  return <output data-testid="location">{pathname + search}</output>
}

describe('Firma, Token KSeF i pobieranie', () => {
  let root: Root
  let container: HTMLDivElement
  const by = (id: string) => container.querySelector<HTMLElement>(`[data-testid="${id}"]`)
  const buttonByText = (scope: Element, text: string) => [...scope.querySelectorAll('button')].find((button) => button.textContent?.includes(text))

  async function settle(): Promise<void> {
    for (let tick = 0; tick < 8; tick++) await act(flush)
  }

  async function render(entry: string | { pathname: string; search: string; state: unknown } = '/company/token?tenantId=a'): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter(
      [
        {
          path: '/',
          element: (
            <QueryClientProvider client={client}>
              <CompanyProvider>
                <CompanyCard />
                <Location />
                <CompanyLayoutPage />
              </CompanyProvider>
            </QueryClientProvider>
          ),
          children: [{ path: 'company/token', element: <CompanyTokenPage /> }],
        },
      ],
      { initialEntries: [entry] },
    )
    await act(async () => root.render(<RouterProvider router={router} />))
    await settle()
  }

  async function typeToken(value: string): Promise<void> {
    const input = by('credential-token-input') as HTMLInputElement
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(input, value)
      input.dispatchEvent(new Event('input', { bubbles: true }))
    })
  }

  async function switchTo(id: string): Promise<void> {
    await act(async () => by('company-card')?.click())
    await settle()
    await act(async () => by(`company-option-${id}`)?.click())
    await settle()
  }

  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('localStorage', memoryStorage())
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    vi.mocked(listTenants).mockResolvedValue([company('a', 'Firma A', '1111111111'), company('b', 'Firma B', '2222222222')])
    vi.mocked(listCredentials).mockResolvedValue([row('a'), row('b')])
    vi.mocked(getDashboardOverview).mockResolvedValue([])
    vi.mocked(getSellerProfile).mockResolvedValue({ ksefEnvironment: 'test' } as never)
    vi.mocked(addOrUpdateCredential).mockResolvedValue({ message: 'ok' })
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  it('shows the token form only, and the first fetch starts with one click', async () => {
    vi.mocked(forceCredentialSync).mockResolvedValue({ tenantId: 'a', fetchedInvoices: 3, newInvoices: 1 } as never)
    await render()

    expect(by('credential-token-input')).not.toBeNull()
    expect(container.querySelector('input[type="file"]')).toBeNull()
    expect(container.textContent).not.toContain('PFX')
    expect(by('credential-token-help')?.textContent).toContain('odczytu i zapisu')
    expect(by('credential-token-help')?.textContent).toContain('Aplikacji Podatnika KSeF')

    await act(async () => by('invoice-fetch-button')?.click())
    await settle()
    expect(forceCredentialSync).toHaveBeenCalledExactlyOnceWith('a')
  })

  it('saves the token for the company on screen', async () => {
    await render()
    await switchTo('b')
    await typeToken('  nowy-token  ')
    await act(async () => buttonByText(container, 'Zapisz token')!.click())
    await settle()

    expect(addOrUpdateCredential).toHaveBeenCalledExactlyOnceWith('b', 'nowy-token')
    expect((by('credential-token-input') as HTMLInputElement).value).toBe('')
  })

  it('refuses an empty token', async () => {
    await render()
    await act(async () => buttonByText(container, 'Zapisz token')!.click())
    expect(container.querySelector('[role="alert"]')?.textContent).toBe('Token jest wymagany')
    expect(addOrUpdateCredential).not.toHaveBeenCalled()
  })

  it('an existing certificate keeps its badge and hint, and stays deletable', async () => {
    vi.mocked(listCredentials).mockResolvedValue([row('a', { credentialType: 'Certificate' })])
    vi.mocked(deleteCredential).mockResolvedValue(undefined)
    await render()

    expect(by('credential-type-badge')?.textContent).toBe('Certyfikat')
    expect(by('credential-certificate-hint')?.textContent).toContain('zastąpić go tokenem')
    expect(deleteCredential).not.toHaveBeenCalled()

    await act(async () => by('credential-delete-button')?.click())
    const dialog = container.querySelector('[role="alertdialog"]')!
    expect(dialog.textContent).toContain('Usunąć certyfikat KSeF?')
    expect(dialog.textContent).toContain('Firma A')
    await act(async () => buttonByText(dialog, 'Usuń certyfikat KSeF')!.click())
    await settle()
    expect(deleteCredential).toHaveBeenCalledExactlyOnceWith('a')
  })

  it('full resync and token removal need a dialog naming the company', async () => {
    vi.mocked(forceFullResync).mockResolvedValue({ fetchedInvoices: 1, newInvoices: 0 } as never)
    await render()

    await act(async () => by('credential-full-resync-button')?.click())
    expect(forceFullResync).not.toHaveBeenCalled()
    const dialog = container.querySelector('[role="alertdialog"]')!
    expect(dialog.textContent).toContain('Firma A')
    expect(dialog.textContent).toContain('NIP 1111111111')
    expect(dialog.querySelector('.ui-button--danger')?.textContent).toBe('Wykonaj pełną resynchronizację')
    await act(async () => buttonByText(dialog, 'Wykonaj pełną resynchronizację')!.click())
    await settle()
    expect(forceFullResync).toHaveBeenCalledExactlyOnceWith('a')
  })

  it('a company without a token has no danger card and no fetch', async () => {
    vi.mocked(listCredentials).mockResolvedValue([row('a', { hasCredential: false, credentialType: null, lastUpdatedAt: null })])
    await render()

    expect(by('credential-delete-button')).toBeNull()
    expect((by('invoice-fetch-button') as HTMLButtonElement).disabled).toBe(true)
    expect(container.textContent).toContain('Dodaj token')
  })

  it('after a token save from a rejected invoice it links back to that invoice', async () => {
    await render({
      pathname: '/company/token',
      search: '?tenantId=a',
      state: { rejectedInvoice: { id: 'i-9', number: 'FV/9/2026', tenantId: 'a' } },
    })
    expect(container.textContent).toContain('Podmieniasz token po odrzuceniu faktury FV/9/2026')
    expect(by('token-saved-return')).toBeNull()

    await typeToken('token')
    await act(async () => buttonByText(container, 'Zapisz token')!.click())
    await settle()

    const link = by('token-saved-return')?.querySelector('a')
    expect(link?.textContent).toBe('Wróć do faktury FV/9/2026')
    expect(link?.getAttribute('href')).toBe('/issued/i-9?tenantId=a')
  })

  it('never offers the way back on another company', async () => {
    await render({
      pathname: '/company/token',
      search: '',
      state: { rejectedInvoice: { id: 'i-9', number: 'FV/9/2026', tenantId: 'b' } },
    })
    expect(container.textContent).not.toContain('FV/9/2026')
  })

  it('a typed token never moves to another company: the switch asks, and clearing empties it', async () => {
    await render()
    await typeToken('token-firmy-a')
    await switchTo('b')

    const dialog = by('company-switch-dialog')!
    expect(dialog.textContent).toContain('niezapisane zmiany w firmie Firma A')
    expect(by('company-card')?.textContent).toContain('Firma A')

    await act(async () => by('company-switch-clear')?.click())
    await settle()
    expect(by('company-card')?.textContent).toContain('Firma B')
    expect((by('credential-token-input') as HTMLInputElement).value).toBe('')
    expect(addOrUpdateCredential).not.toHaveBeenCalled()
  })

  it('save and switch stores the token under the company it was typed for', async () => {
    await render()
    await typeToken('token-firmy-a')
    await switchTo('b')
    await act(async () => by('company-switch-save')?.click())
    await settle()

    expect(addOrUpdateCredential).toHaveBeenCalledExactlyOnceWith('a', 'token-firmy-a')
    expect(by('company-card')?.textContent).toContain('Firma B')
  })
})
