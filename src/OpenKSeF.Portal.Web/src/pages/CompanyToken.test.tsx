// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, type ReactElement } from 'react'
import toast from 'react-hot-toast'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  addOrUpdateCertificate,
  addOrUpdateCredential,
  deleteCredential,
  forceCredentialSync,
  forceFullResync,
  getCredentialStatus,
  listCredentials,
} from '@/api/endpoints/credentials'
import { getDashboardOverview } from '@/api/endpoints/dashboard'
import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { listTenants } from '@/api/endpoints/tenants'
import type { TenantCredentialStatusResponse } from '@/api/types'
import { CompanyCard } from '@/company/CompanyCard'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { formatWarsawDate } from '@/invoicing/format'
import { CompanyLayoutPage } from './CompanyLayout'
import { CompanyTokenPage } from './CompanyToken'

vi.mock('@/api/endpoints/credentials', () => ({
  listCredentials: vi.fn(),
  getCredentialStatus: vi.fn(),
  addOrUpdateCredential: vi.fn(),
  addOrUpdateCertificate: vi.fn(),
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

describe('Firma, Dostęp do KSeF i pobieranie', () => {
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

  async function chooseFile(id: string, name: string, bytes = 1): Promise<File> {
    const file = new File(['x'.repeat(bytes)], name)
    const input = by(id) as HTMLInputElement
    Object.defineProperty(input, 'files', { value: [file], configurable: true })
    await act(async () => input.dispatchEvent(new Event('change', { bubbles: true })))
    return file
  }

  async function typePassword(value: string): Promise<void> {
    const input = by('credential-certificate-password') as HTMLInputElement
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(input, value)
      input.dispatchEvent(new Event('input', { bubbles: true }))
    })
  }

  async function chooseCertificate(): Promise<void> {
    await act(async () => by('credential-method-certificate')?.click())
  }

  const certificateStatus = (certificateValidTo: string | null) =>
    vi.mocked(getCredentialStatus).mockResolvedValue({ exists: true, credentialType: 'Certificate', updatedAt: '2026-09-14T10:00:00Z', certificateValidTo })
  const inDays = (days: number) => new Date(Date.now() + days * 86_400_000).toISOString()

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
    vi.mocked(addOrUpdateCertificate).mockResolvedValue({ message: 'ok' })
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  it('token is the default way, and the first fetch starts with one click', async () => {
    vi.mocked(forceCredentialSync).mockResolvedValue({ tenantId: 'a', fetchedInvoices: 3, newInvoices: 1 } as never)
    await render()

    expect(by('credential-token-input')).not.toBeNull()
    expect(container.querySelector('input[type="file"]')).toBeNull()
    expect((by('credential-method-token') as HTMLInputElement).checked).toBe(true)
    expect(container.querySelector('legend')?.textContent).toBe('Sposób logowania do KSeF')
    expect(container.querySelector('#token-title')?.textContent).toBe('Dostęp do KSeF')
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

  it('a token company never asks for the certificate status', async () => {
    await render()
    expect(getCredentialStatus).not.toHaveBeenCalled()
    expect(by('certificate-validity')).toBeNull()
  })

  it('uploads a .pfx with its password, then clears the fields and confirms', async () => {
    await render()
    await chooseCertificate()
    expect(by('credential-token-input')).toBeNull()
    expect(by('credential-certificate-help')?.textContent).toBe('Certyfikat typu Uwierzytelnianie z Aplikacji Podatnika KSeF 2.0. Użyj go, gdy nie masz tokenu.')
    const file = await chooseFile('credential-certificate-file', 'firma.pfx')
    expect(by('credential-key-file')).toBeNull()
    await typePassword('tajne')
    await act(async () => buttonByText(container, 'Zapisz certyfikat')!.click())
    await settle()

    expect(addOrUpdateCertificate).toHaveBeenCalledExactlyOnceWith('a', { file, key: null, password: 'tajne' })
    expect(addOrUpdateCredential).not.toHaveBeenCalled()
    expect(toast.success).toHaveBeenCalledWith('Certyfikat KSeF zapisany pomyślnie')
    expect((by('credential-certificate-password') as HTMLInputElement).value).toBe('')
    expect(container.textContent).not.toContain('tajne')
  })

  it('a .crt needs its private key', async () => {
    await render()
    await chooseCertificate()
    const file = await chooseFile('credential-certificate-file', 'firma.crt')
    await typePassword('tajne')
    await act(async () => buttonByText(container, 'Zapisz certyfikat')!.click())
    expect(container.querySelector('[role="alert"]')?.textContent).toBe('Do pliku .crt dodaj klucz prywatny (.key)')

    const key = await chooseFile('credential-key-file', 'firma.key')
    await act(async () => buttonByText(container, 'Zapisz certyfikat')!.click())
    await settle()
    expect(addOrUpdateCertificate).toHaveBeenCalledExactlyOnceWith('a', { file, key, password: 'tajne' })
  })

  it('refuses a certificate without a file or a password', async () => {
    vi.mocked(listCredentials).mockResolvedValue([row('a', { hasCredential: false, credentialType: null, lastUpdatedAt: null })])
    await render()
    await chooseCertificate()
    await act(async () => buttonByText(container, 'Dodaj certyfikat')!.click())
    expect(container.querySelector('[role="alert"]')?.textContent).toBe('Wybierz plik certyfikatu')

    await chooseFile('credential-certificate-file', 'firma.p12')
    await act(async () => buttonByText(container, 'Dodaj certyfikat')!.click())
    expect(container.querySelector('[role="alert"]')?.textContent).toBe('Hasło jest wymagane')
    expect(addOrUpdateCertificate).not.toHaveBeenCalled()
  })

  it('refuses certificate and key over 64 KB together without sending them', async () => {
    await render()
    await chooseCertificate()
    await chooseFile('credential-certificate-file', 'firma.crt', 40 * 1024)
    await chooseFile('credential-key-file', 'firma.key', 30 * 1024)
    await typePassword('tajne')
    await act(async () => buttonByText(container, 'Zapisz certyfikat')!.click())

    expect(container.querySelector('[role="alert"]')?.textContent).toBe('Plik jest za duży. Maksymalny rozmiar to 64 KB.')
    expect(addOrUpdateCertificate).not.toHaveBeenCalled()
  })

  it('a saved certificate opens the form on the certificate way', async () => {
    certificateStatus(inDays(200))
    vi.mocked(listCredentials).mockResolvedValue([row('a', { credentialType: 'Certificate' })])
    await render()

    expect((by('credential-method-certificate') as HTMLInputElement).checked).toBe(true)
    expect(by('credential-certificate-file')).not.toBeNull()
  })

  it('shows the API reason when the certificate is refused', async () => {
    vi.mocked(addOrUpdateCertificate).mockRejectedValue(new Error('Certyfikat wygasł. Wygeneruj nowy w Aplikacji Podatnika KSeF i wgraj go.'))
    await render()
    await chooseCertificate()
    await chooseFile('credential-certificate-file', 'firma.pfx')
    await typePassword('tajne')
    await act(async () => buttonByText(container, 'Zapisz certyfikat')!.click())
    await settle()

    expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('Certyfikat wygasł. Wygeneruj nowy'))
    expect(toast.success).not.toHaveBeenCalled()
  })

  it.each([
    ['valid', 200, 'company-muted', 'Certyfikat ważny do '],
    ['expiring', 10, 'inv-alert--warning', 'Certyfikat wygasa '],
    ['expired', -1, 'inv-alert--error', 'Certyfikat wygasł '],
  ])('a %s certificate shows its date', async (_name, days, className, text) => {
    const validTo = inDays(days)
    certificateStatus(validTo)
    vi.mocked(listCredentials).mockResolvedValue([row('a', { credentialType: 'Certificate' })])
    await render()

    const box = by('certificate-validity')!
    expect(getCredentialStatus).toHaveBeenCalledWith('a')
    expect(box.className).toContain(className)
    expect(box.textContent).toContain(text + formatWarsawDate(validTo))
  })

  it('an unreadable stored certificate asks for a new upload', async () => {
    certificateStatus(null)
    vi.mocked(listCredentials).mockResolvedValue([row('a', { credentialType: 'Certificate' })])
    await render()

    expect(by('certificate-validity')?.textContent).toBe('Nie można odczytać zapisanego certyfikatu. Wgraj go ponownie.')
    expect(by('credential-delete-button')).not.toBeNull()
  })

  it('an existing certificate keeps its badge and stays deletable', async () => {
    certificateStatus(inDays(200))
    vi.mocked(listCredentials).mockResolvedValue([row('a', { credentialType: 'Certificate' })])
    vi.mocked(deleteCredential).mockResolvedValue(undefined)
    await render()

    expect(by('credential-type-badge')?.textContent).toBe('Certyfikat')
    expect(container.textContent).not.toContain('w portalu nie wgrasz go ponownie')
    expect(container.querySelector('.danger-list')?.textContent).toContain('do czasu dodania nowego tokenu lub certyfikatu.')
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
    expect(dialog.textContent).toContain('Niczego nie usuwa')
    expect(container.querySelector('.danger-list')?.textContent).toContain('Niczego nie usuwa')
    expect(dialog.querySelector('.ui-button--danger')?.textContent).toBe('Wykonaj pełną resynchronizację')
    await act(async () => buttonByText(dialog, 'Wykonaj pełną resynchronizację')!.click())
    await settle()
    expect(forceFullResync).toHaveBeenCalledExactlyOnceWith('a')
  })

  async function fullResync(result: object): Promise<void> {
    vi.mocked(forceFullResync).mockResolvedValue({ fetchedInvoices: 5, newInvoices: 0, ...result } as never)
    await render()

    await act(async () => by('credential-full-resync-button')?.click())
    const dialog = container.querySelector('[role="alertdialog"]')!
    await act(async () => buttonByText(dialog, 'Wykonaj pełną resynchronizację')!.click())
    await settle()
  }

  const LONG_TOAST = { duration: 12_000 }

  it('full resync on an API with only failedDownloads reports the count, not success', async () => {
    await fullResync({ failedDownloads: 2 })

    expect(toast.success).not.toHaveBeenCalled()
    expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('Bez szczegółów z KSeF (liczba: 2)'), LONG_TOAST)
    expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('opłacone przy kolejnej pełnej resynchronizacji'), LONG_TOAST)
  })

  it('full resync with errored downloads says how many failed', async () => {
    await fullResync({ failedDownloads: 2, erroredDownloads: 2, queuedDownloads: 0 })

    expect(toast.success).not.toHaveBeenCalled()
    expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('Bez szczegółów z KSeF: 2 z błędem. '), LONG_TOAST)
    expect(toast.error).not.toHaveBeenCalledWith(expect.stringContaining('w kolejce'), LONG_TOAST)
  })

  it('full resync with errored and queued downloads counts them apart, short enough to read in its duration', async () => {
    await fullResync({ failedDownloads: 6, erroredDownloads: 2, queuedDownloads: 4 })

    expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('Bez szczegółów z KSeF: 2 z błędem, 4 w kolejce.'), LONG_TOAST)
    const [message, options] = vi.mocked(toast.error).mock.calls[0] as unknown as [string, { duration: number }]
    expect(message.match(/pełnej resynchronizacji/g)).toHaveLength(1)
    expect(message.length / (options.duration / 1000)).toBeLessThanOrEqual(16)
  })

  it('full resync with only queued downloads is a success that names the next cycles', async () => {
    await fullResync({ failedDownloads: 4, erroredDownloads: 0, queuedDownloads: 4 })

    expect(toast.error).not.toHaveBeenCalled()
    expect(toast.success).toHaveBeenCalledWith(expect.stringContaining('Szczegóły pozostałych faktur (liczba: 4) pobierzemy w kolejnych cyklach'), LONG_TOAST)
  })

  it('full resync with nothing outstanding reports plain success', async () => {
    await fullResync({ failedDownloads: 0, erroredDownloads: 0, queuedDownloads: 0 })

    expect(toast.error).not.toHaveBeenCalled()
    expect(toast.success).toHaveBeenCalledWith('Pełna resynchronizacja zakończona. Pobrane: 5, nowe: 0.')
  })

  it('full resync drops cached transfer details of the company', async () => {
    const invalidate = vi.spyOn(QueryClient.prototype, 'invalidateQueries')
    await fullResync({ failedDownloads: 0, erroredDownloads: 0, queuedDownloads: 0 })

    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['transfer-details', 'a'] })
    invalidate.mockRestore()
  })

  it('a company without a token has no danger card and no fetch', async () => {
    vi.mocked(listCredentials).mockResolvedValue([row('a', { hasCredential: false, credentialType: null, lastUpdatedAt: null })])
    await render()

    expect(by('credential-delete-button')).toBeNull()
    expect((by('credential-method-token') as HTMLInputElement).checked).toBe(true)
    expect((by('invoice-fetch-button') as HTMLButtonElement).disabled).toBe(true)
    expect(container.textContent).toContain('Dodaj token')
    expect(container.textContent).toContain('Brak tokenu ani certyfikatu dla tej firmy.')
    expect(container.textContent).toContain('Pobieranie ruszy po dodaniu tokenu lub certyfikatu.')
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
